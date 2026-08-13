using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Concord.Api.Data;
using Concord.Api.DTOs.Auth;
using Concord.Api.DTOs.Channels;
using Concord.Api.DTOs.Servers;
using Concord.Api.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Concord.Api.Tests;

public sealed class ChannelEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Owner_can_create_text_and_voice_channels_in_position_order()
    {
        await using var factory = new ChannelWebApplicationFactory();
        var owner = factory.CreateClient();
        await RegisterAsync(owner, "owner");
        var server = await CreateServerAsync(owner);

        var voice = await CreateChannelAsync(owner, server.Id, "Voice", ChannelType.Voice, 2);
        var text = await CreateChannelAsync(owner, server.Id, "general", ChannelType.Text, 1);
        var channels = await owner.GetFromJsonAsync<ChannelResponse[]>(
            $"/api/servers/{server.Id}/channels", JsonOptions);

        Assert.Equal(ChannelType.Text, channels![0].Type);
        Assert.Equal(text.Id, channels[0].Id);
        Assert.Equal(ChannelType.Voice, voice.Type);
    }

    [Fact]
    public async Task Member_can_view_channels()
    {
        await using var factory = new ChannelWebApplicationFactory();
        var owner = factory.CreateClient();
        await RegisterAsync(owner, "owner");
        var server = await CreateServerAsync(owner);
        await CreateChannelAsync(owner, server.Id, "general", ChannelType.Text, 0);
        var member = factory.CreateClient();
        await RegisterAsync(member, "member");
        await member.PostAsync($"/api/servers/{server.Id}/members", null);

        var response = await member.GetAsync($"/api/servers/{server.Id}/channels");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Non_member_cannot_view_channels()
    {
        await using var factory = new ChannelWebApplicationFactory();
        var owner = factory.CreateClient();
        await RegisterAsync(owner, "owner");
        var server = await CreateServerAsync(owner);
        var outsider = factory.CreateClient();
        await RegisterAsync(outsider, "outsider");

        var response = await outsider.GetAsync($"/api/servers/{server.Id}/channels");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Regular_member_cannot_create_edit_or_delete_channels()
    {
        await using var factory = new ChannelWebApplicationFactory();
        var owner = factory.CreateClient();
        await RegisterAsync(owner, "owner");
        var server = await CreateServerAsync(owner);
        var channel = await CreateChannelAsync(owner, server.Id, "general", ChannelType.Text, 0);
        var member = factory.CreateClient();
        await RegisterAsync(member, "member");
        await member.PostAsync($"/api/servers/{server.Id}/members", null);
        var request = new SaveChannelRequest { Name = "changed", Type = ChannelType.Voice, Position = 1 };

        Assert.Equal(HttpStatusCode.Forbidden,
            (await member.PostAsJsonAsync($"/api/servers/{server.Id}/channels", request, JsonOptions)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await member.PutAsJsonAsync($"/api/channels/{channel.Id}", request, JsonOptions)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await member.DeleteAsync($"/api/channels/{channel.Id}")).StatusCode);
    }

    [Fact]
    public async Task Owner_can_edit_and_delete_channel()
    {
        await using var factory = new ChannelWebApplicationFactory();
        var owner = factory.CreateClient();
        await RegisterAsync(owner, "owner");
        var server = await CreateServerAsync(owner);
        var channel = await CreateChannelAsync(owner, server.Id, "general", ChannelType.Text, 0);

        var update = await owner.PutAsJsonAsync($"/api/channels/{channel.Id}", new SaveChannelRequest
        {
            Name = "Sala de voz", Type = ChannelType.Voice, Position = 4
        }, JsonOptions);
        var updated = await update.Content.ReadFromJsonAsync<ChannelResponse>(JsonOptions);
        var deleted = await owner.DeleteAsync($"/api/channels/{channel.Id}");

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(ChannelType.Voice, updated!.Type);
        Assert.Equal(4, updated.Position);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Theory]
    [InlineData("   ", 0)]
    [InlineData("general", -1)]
    [InlineData("general", 10001)]
    public async Task Invalid_names_and_positions_return_bad_request(string name, int position)
    {
        await using var factory = new ChannelWebApplicationFactory();
        var owner = factory.CreateClient();
        await RegisterAsync(owner, "owner");
        var server = await CreateServerAsync(owner);

        var response = await owner.PostAsJsonAsync($"/api/servers/{server.Id}/channels",
            new SaveChannelRequest { Name = name, Type = ChannelType.Text, Position = position }, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<ChannelResponse> CreateChannelAsync(
        HttpClient client, Guid serverId, string name, ChannelType type, int position)
    {
        var response = await client.PostAsJsonAsync($"/api/servers/{serverId}/channels",
            new SaveChannelRequest { Name = name, Type = type, Position = position }, JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ChannelResponse>(JsonOptions))!;
    }

    private static async Task<ServerResponse> CreateServerAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/servers", new CreateServerRequest { Name = "Channels Test" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ServerResponse>(JsonOptions))!;
    }

    private static async Task RegisterAsync(HttpClient client, string username)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Username = username, Email = $"{username}@concord.test", Password = "Concord1"
        });
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
    }

    private sealed class ChannelWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly InMemoryDatabaseRoot _databaseRoot = new();
        private readonly string _databaseName = $"concord-channel-tests-{Guid.NewGuid()}";

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
            });
        }
    }
}
