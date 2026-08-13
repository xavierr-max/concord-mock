using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Concord.Api.Data;
using Concord.Api.DTOs.Auth;
using Concord.Api.DTOs.Channels;
using Concord.Api.DTOs.Messages;
using Concord.Api.DTOs.Servers;
using Concord.Api.Models;
using Concord.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Concord.Api.Tests;

public sealed class MessageEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Member_can_send_message_with_author_and_timestamps()
    {
        await using var factory = new MessageWebApplicationFactory();
        var owner = factory.CreateClient();
        var ownerAuth = await RegisterAsync(owner, "owner");
        var channel = await CreateTextChannelAsync(owner);

        var response = await SendAsync(owner, channel.Id, "Olá, Concord!");
        var message = await response.Content.ReadFromJsonAsync<MessageResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(ownerAuth.User.Id, message!.Author.Id);
        Assert.Equal("owner", message.Author.Username);
        Assert.Equal("Olá, Concord!", message.Content);
        Assert.NotEqual(default, message.CreatedAt);
        Assert.Equal(message.CreatedAt, message.UpdatedAt);
    }

    [Fact]
    public async Task Non_member_cannot_send_or_read_messages()
    {
        await using var factory = new MessageWebApplicationFactory();
        var owner = factory.CreateClient();
        await RegisterAsync(owner, "owner");
        var channel = await CreateTextChannelAsync(owner);
        var outsider = factory.CreateClient();
        await RegisterAsync(outsider, "outsider");

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(outsider, channel.Id, "blocked")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await outsider.GetAsync($"/api/channels/{channel.Id}/messages?page=1&pageSize=20")).StatusCode);
    }

    [Fact]
    public async Task Empty_oversized_and_voice_channel_messages_are_rejected()
    {
        await using var factory = new MessageWebApplicationFactory();
        var owner = factory.CreateClient();
        await RegisterAsync(owner, "owner");
        var text = await CreateTextChannelAsync(owner);
        var serverId = text.ServerId;
        var voiceResponse = await owner.PostAsJsonAsync($"/api/servers/{serverId}/channels",
            new SaveChannelRequest { Name = "voice", Type = ChannelType.Voice, Position = 1 }, JsonOptions);
        var voice = (await voiceResponse.Content.ReadFromJsonAsync<ChannelResponse>(JsonOptions))!;

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(owner, text.Id, "   ")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(owner, text.Id, new string('x', 2001))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(owner, voice.Id, "not allowed")).StatusCode);
    }

    [Fact]
    public async Task History_requires_pagination_and_returns_pages()
    {
        await using var factory = new MessageWebApplicationFactory();
        var owner = factory.CreateClient();
        await RegisterAsync(owner, "owner");
        var channel = await CreateTextChannelAsync(owner);
        await SendAsync(owner, channel.Id, "one");
        await SendAsync(owner, channel.Id, "two");
        await SendAsync(owner, channel.Id, "three");

        var missingPagination = await owner.GetAsync($"/api/channels/{channel.Id}/messages");
        var response = await owner.GetAsync($"/api/channels/{channel.Id}/messages?page=1&pageSize=2");
        var page = await response.Content.ReadFromJsonAsync<PagedMessagesResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, missingPagination.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, page!.Items.Count);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
    }

    [Fact]
    public async Task Only_author_or_admin_owner_can_edit_or_delete()
    {
        await using var factory = new MessageWebApplicationFactory();
        var owner = factory.CreateClient();
        await RegisterAsync(owner, "owner");
        var channel = await CreateTextChannelAsync(owner);
        var member = factory.CreateClient();
        await RegisterAsync(member, "member");
        await member.PostAsync($"/api/servers/{channel.ServerId}/members", null);
        var created = await SendAsync(member, channel.Id, "original");
        var message = (await created.Content.ReadFromJsonAsync<MessageResponse>(JsonOptions))!;
        var other = factory.CreateClient();
        var otherAuth = await RegisterAsync(other, "other");
        await other.PostAsync($"/api/servers/{channel.ServerId}/members", null);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await other.PutAsJsonAsync($"/api/messages/{message.Id}",
                new SaveMessageRequest { Content = "blocked" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await member.PutAsJsonAsync($"/api/messages/{message.Id}",
                new SaveMessageRequest { Content = "author edit" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/messages/{message.Id}")).StatusCode);

        var adminMessageResponse = await SendAsync(member, channel.Id, "moderate me");
        var adminMessage = (await adminMessageResponse.Content.ReadFromJsonAsync<MessageResponse>(JsonOptions))!;
        await factory.SetRoleAsync(channel.ServerId, otherAuth.User.Id, ServerRole.ADMIN);
        Assert.Equal(HttpStatusCode.OK,
            (await other.PutAsJsonAsync($"/api/messages/{adminMessage.Id}",
                new SaveMessageRequest { Content = "admin edit" })).StatusCode);
    }

    [Fact]
    public async Task Delete_is_soft_and_history_hides_content()
    {
        await using var factory = new MessageWebApplicationFactory();
        var owner = factory.CreateClient();
        await RegisterAsync(owner, "owner");
        var channel = await CreateTextChannelAsync(owner);
        var created = await SendAsync(owner, channel.Id, "retained internally");
        var message = (await created.Content.ReadFromJsonAsync<MessageResponse>(JsonOptions))!;

        await owner.DeleteAsync($"/api/messages/{message.Id}");
        var history = await owner.GetFromJsonAsync<PagedMessagesResponse>(
            $"/api/channels/{channel.Id}/messages?page=1&pageSize=20", JsonOptions);

        var tombstone = Assert.Single(history!.Items);
        Assert.True(tombstone.IsDeleted);
        Assert.Null(tombstone.Content);
        Assert.Equal("retained internally", await factory.GetStoredContentAsync(message.Id));
    }

    [Fact]
    public async Task Unread_count_excludes_own_messages_and_read_advances_the_cursor()
    {
        await using var factory = new MessageWebApplicationFactory();
        var owner = factory.CreateClient();
        await RegisterAsync(owner, "owner");
        var channel = await CreateTextChannelAsync(owner);
        var member = factory.CreateClient();
        await RegisterAsync(member, "member");
        await member.PostAsync($"/api/servers/{channel.ServerId}/members", null);

        await SendAsync(owner, channel.Id, "own message");
        await SendAsync(member, channel.Id, "first unread");
        await SendAsync(member, channel.Id, "second unread");

        Assert.Equal(2, (await owner.GetFromJsonAsync<UnreadCountResponse>(
            $"/api/channels/{channel.Id}/unread-count", JsonOptions))!.UnreadCount);
        Assert.Equal(HttpStatusCode.NoContent,
            (await owner.PostAsync($"/api/channels/{channel.Id}/read", null)).StatusCode);
        Assert.Equal(0, (await owner.GetFromJsonAsync<UnreadCountResponse>(
            $"/api/channels/{channel.Id}/unread-count", JsonOptions))!.UnreadCount);

        await SendAsync(owner, channel.Id, "still not unread");
        await SendAsync(member, channel.Id, "new unread");
        Assert.Equal(1, (await owner.GetFromJsonAsync<UnreadCountResponse>(
            $"/api/channels/{channel.Id}/unread-count", JsonOptions))!.UnreadCount);
    }

    [Fact]
    public async Task Unread_endpoints_require_membership_and_mentions_are_ready_for_future_support()
    {
        await using var factory = new MessageWebApplicationFactory();
        var owner = factory.CreateClient();
        await RegisterAsync(owner, "owner");
        var channel = await CreateTextChannelAsync(owner);
        var outsider = factory.CreateClient();
        await RegisterAsync(outsider, "outsider");

        var mentions = await owner.GetFromJsonAsync<UnreadMentionCountResponse>(
            $"/api/channels/{channel.Id}/unread-mention-count", JsonOptions);

        Assert.Equal(0, mentions!.UnreadMentionCount);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await outsider.PostAsync($"/api/channels/{channel.Id}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await outsider.GetAsync($"/api/channels/{channel.Id}/unread-count")).StatusCode);
    }

    [Fact]
    public async Task Author_can_upload_attachment_and_history_returns_metadata()
    {
        await using var factory = new MessageWebApplicationFactory();
        var owner = factory.CreateClient();
        await RegisterAsync(owner, "owner");
        var channel = await CreateTextChannelAsync(owner);
        var created = await SendAsync(owner, channel.Id, "with attachment");
        var message = (await created.Content.ReadFromJsonAsync<MessageResponse>(JsonOptions))!;

        var response = await UploadAsync(owner, message.Id, "photo.png", "image/png", [1, 2, 3]);
        var attachment = await response.Content.ReadFromJsonAsync<MessageAttachmentResponse>(JsonOptions);
        var history = await owner.GetFromJsonAsync<PagedMessagesResponse>(
            $"/api/channels/{channel.Id}/messages?page=1&pageSize=20", JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("photo.png", attachment!.FileName);
        Assert.Equal(3, attachment.FileSize);
        Assert.StartsWith("/test-files/", attachment.Url);
        Assert.Equal(attachment.Id, Assert.Single(Assert.Single(history!.Items).Attachments).Id);
    }

    [Theory]
    [InlineData("photo.exe", "image/png")]
    [InlineData("photo.png", "application/octet-stream")]
    [InlineData("../photo.png", "image/png")]
    [InlineData("photo", "image/png")]
    public async Task Invalid_extension_content_type_and_file_name_are_rejected(
        string fileName, string contentType)
    {
        await using var factory = new MessageWebApplicationFactory();
        var owner = factory.CreateClient();
        await RegisterAsync(owner, "owner");
        var channel = await CreateTextChannelAsync(owner);
        var created = await SendAsync(owner, channel.Id, "upload validation");
        var message = (await created.Content.ReadFromJsonAsync<MessageResponse>(JsonOptions))!;

        var response = await UploadAsync(owner, message.Id, fileName, contentType, [1]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Empty_oversized_and_unauthenticated_uploads_are_rejected()
    {
        await using var factory = new MessageWebApplicationFactory();
        var owner = factory.CreateClient();
        await RegisterAsync(owner, "owner");
        var channel = await CreateTextChannelAsync(owner);
        var created = await SendAsync(owner, channel.Id, "limits");
        var message = (await created.Content.ReadFromJsonAsync<MessageResponse>(JsonOptions))!;

        Assert.Equal(HttpStatusCode.BadRequest,
            (await UploadAsync(owner, message.Id, "empty.png", "image/png", [])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await UploadAsync(owner, message.Id, "large.png", "image/png", new byte[10 * 1024 * 1024 + 1])).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await UploadAsync(factory.CreateClient(), message.Id, "photo.png", "image/png", [1])).StatusCode);
    }

    [Fact]
    public async Task Another_member_cannot_attach_files_to_someone_elses_message()
    {
        await using var factory = new MessageWebApplicationFactory();
        var owner = factory.CreateClient();
        await RegisterAsync(owner, "owner");
        var channel = await CreateTextChannelAsync(owner);
        var created = await SendAsync(owner, channel.Id, "owned");
        var message = (await created.Content.ReadFromJsonAsync<MessageResponse>(JsonOptions))!;
        var member = factory.CreateClient();
        await RegisterAsync(member, "member");
        await member.PostAsync($"/api/servers/{channel.ServerId}/members", null);

        var response = await UploadAsync(member, message.Id, "photo.png", "image/png", [1]);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, Guid channelId, string content) =>
        client.PostAsJsonAsync($"/api/channels/{channelId}/messages", new SaveMessageRequest { Content = content });

    private static Task<HttpResponseMessage> UploadAsync(
        HttpClient client, Guid messageId, string fileName, string contentType, byte[] bytes)
    {
        var multipart = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        multipart.Add(file, "file", fileName);
        return client.PostAsync($"/api/messages/{messageId}/attachments", multipart);
    }

    private static async Task<ChannelResponse> CreateTextChannelAsync(HttpClient owner)
    {
        var serverResponse = await owner.PostAsJsonAsync("/api/servers", new CreateServerRequest { Name = "Messages" });
        var server = (await serverResponse.Content.ReadFromJsonAsync<ServerResponse>(JsonOptions))!;
        var channelResponse = await owner.PostAsJsonAsync($"/api/servers/{server.Id}/channels",
            new SaveChannelRequest { Name = "general", Type = ChannelType.Text, Position = 0 }, JsonOptions);
        channelResponse.EnsureSuccessStatusCode();
        return (await channelResponse.Content.ReadFromJsonAsync<ChannelResponse>(JsonOptions))!;
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, string username)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Username = username, Email = $"{username}@concord.test", Password = "Concord1"
        });
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return auth;
    }

    private sealed class MessageWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly InMemoryDatabaseRoot _databaseRoot = new();
        private readonly string _databaseName = $"concord-message-tests-{Guid.NewGuid()}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<ConcordDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ConcordDbContext>>();
                services.RemoveAll<ConcordDbContext>();
                services.AddDbContext<ConcordDbContext>(options =>
                    options.UseInMemoryDatabase(_databaseName, _databaseRoot));
                services.RemoveAll<IFileStorageService>();
                services.AddSingleton<IFileStorageService, TestFileStorageService>();
            });
        }

        public async Task SetRoleAsync(Guid serverId, Guid userId, ServerRole role)
        {
            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ConcordDbContext>();
            var membership = await context.ServerMembers.SingleAsync(item =>
                item.ServerId == serverId && item.UserId == userId);
            membership.Role = role;
            await context.SaveChangesAsync();
        }

        public async Task<string> GetStoredContentAsync(Guid messageId)
        {
            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ConcordDbContext>();
            return await context.Messages.Where(message => message.Id == messageId)
                .Select(message => message.Content).SingleAsync();
        }
    }

    private sealed class TestFileStorageService : IFileStorageService
    {
        public Task<StoredFile> SaveAsync(Stream content, string extension, CancellationToken cancellationToken)
        {
            var key = $"{Guid.NewGuid():N}{extension}";
            return Task.FromResult(new StoredFile($"/test-files/{key}", key));
        }

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
