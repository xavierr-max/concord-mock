using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Concord.Api.Data;
using Concord.Api.Configurations;
using Concord.Api.DTOs.Auth;
using Concord.Api.DTOs.Channels;
using Concord.Api.DTOs.Messages;
using Concord.Api.DTOs.Servers;
using Concord.Api.Models;
using Concord.Api.DTOs.Presence;
using Concord.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Concord.Api.Tests;

public sealed class ChatHubTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Connection_without_jwt_is_rejected()
    {
        await using var factory = new ChatWebApplicationFactory();
        await using var connection = CreateConnection(factory, null);

        await Assert.ThrowsAnyAsync<HttpRequestException>(() => connection.StartAsync());
    }

    [Fact]
    public async Task Member_can_join_send_receive_and_persist_message()
    {
        await using var factory = new ChatWebApplicationFactory();
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client, "owner");
        var channel = await CreateTextChannelAsync(client);
        await using var connection = CreateConnection(factory, auth.AccessToken);
        var received = new TaskCompletionSource<MessageResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<MessageResponse>("MessageCreated", message => received.TrySetResult(message));

        await connection.StartAsync();
        await connection.InvokeAsync("JoinChannel", channel.Id);
        var sent = await connection.InvokeAsync<MessageResponse>("SendMessage", channel.Id, "tempo real");
        var published = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var history = await client.GetFromJsonAsync<PagedMessagesResponse>(
            $"/api/channels/{channel.Id}/messages?page=1&pageSize=20", JsonOptions);

        Assert.Equal(sent.Id, published.Id);
        Assert.Equal("tempo real", published.Content);
        Assert.Contains(history!.Items, message => message.Id == sent.Id);
    }

    [Fact]
    public async Task Authenticated_non_member_cannot_join_or_send()
    {
        await using var factory = new ChatWebApplicationFactory();
        var owner = factory.CreateClient();
        await RegisterAsync(owner, "owner");
        var channel = await CreateTextChannelAsync(owner);
        var outsider = factory.CreateClient();
        var outsiderAuth = await RegisterAsync(outsider, "outsider");
        await using var connection = CreateConnection(factory, outsiderAuth.AccessToken);

        await connection.StartAsync();
        await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("JoinChannel", channel.Id));
        await Assert.ThrowsAsync<HubException>(() =>
            connection.InvokeAsync<MessageResponse>("SendMessage", channel.Id, "bloqueada"));
        await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("StartTyping", channel.Id));
    }

    [Fact]
    public async Task Rest_updates_and_deletes_are_broadcast_to_channel_group()
    {
        await using var factory = new ChatWebApplicationFactory();
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client, "owner");
        var channel = await CreateTextChannelAsync(client);
        await using var connection = CreateConnection(factory, auth.AccessToken);
        var updatedEvent = new TaskCompletionSource<MessageResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var deletedEvent = new TaskCompletionSource<MessageResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<MessageResponse>("MessageUpdated", message => updatedEvent.TrySetResult(message));
        connection.On<MessageResponse>("MessageDeleted", message => deletedEvent.TrySetResult(message));
        await connection.StartAsync();
        await connection.InvokeAsync("JoinChannel", channel.Id);
        var created = await connection.InvokeAsync<MessageResponse>("SendMessage", channel.Id, "original");

        var updateResponse = await client.PutAsJsonAsync($"/api/messages/{created.Id}",
            new SaveMessageRequest { Content = "editada" });
        updateResponse.EnsureSuccessStatusCode();
        var updated = await updatedEvent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var deleteResponse = await client.DeleteAsync($"/api/messages/{created.Id}");
        deleteResponse.EnsureSuccessStatusCode();
        var deleted = await deletedEvent.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("editada", updated.Content);
        Assert.True(deleted.IsDeleted);
        Assert.Null(deleted.Content);
    }

    [Fact]
    public async Task Server_members_receive_online_and_delayed_offline_events()
    {
        await using var factory = new ChatWebApplicationFactory();
        var ownerClient = factory.CreateClient();
        var ownerAuth = await RegisterAsync(ownerClient, "owner");
        var channel = await CreateTextChannelAsync(ownerClient);
        var memberClient = factory.CreateClient();
        var memberAuth = await RegisterAsync(memberClient, "member");
        await memberClient.PostAsync($"/api/servers/{channel.ServerId}/members", null);
        await using var ownerConnection = CreateConnection(factory, ownerAuth.AccessToken);
        await using var memberConnection = CreateConnection(factory, memberAuth.AccessToken);
        var onlineEvent = new TaskCompletionSource<UserPresenceResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var offlineEvent = new TaskCompletionSource<UserPresenceResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var onlineStatusEvent = new TaskCompletionSource<UserPresenceResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var offlineStatusEvent = new TaskCompletionSource<UserPresenceResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        ownerConnection.On<UserPresenceResponse>("UserOnline", update =>
        {
            if (update.UserId == memberAuth.User.Id) onlineEvent.TrySetResult(update);
        });
        ownerConnection.On<UserPresenceResponse>("UserOffline", update =>
        {
            if (update.UserId == memberAuth.User.Id) offlineEvent.TrySetResult(update);
        });
        ownerConnection.On<UserPresenceResponse>("UserStatusChanged", update =>
        {
            if (update.UserId != memberAuth.User.Id) return;
            if (update.Status == UserStatus.Online) onlineStatusEvent.TrySetResult(update);
            if (update.Status == UserStatus.Offline) offlineStatusEvent.TrySetResult(update);
        });

        await ownerConnection.StartAsync();
        await memberConnection.StartAsync();
        var online = await onlineEvent.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await memberConnection.StopAsync();

        await Task.Delay(30);
        Assert.False(offlineEvent.Task.IsCompleted);
        var offline = await offlineEvent.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await onlineStatusEvent.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await offlineStatusEvent.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(UserStatus.Online, online.Status);
        Assert.Equal(UserStatus.Offline, offline.Status);
        Assert.Equal(UserStatus.Offline, factory.GetPresenceStatus(memberAuth.User.Id));
    }

    [Fact]
    public async Task Reconnection_within_grace_period_does_not_publish_offline()
    {
        await using var factory = new ChatWebApplicationFactory();
        var ownerClient = factory.CreateClient();
        var ownerAuth = await RegisterAsync(ownerClient, "owner");
        var channel = await CreateTextChannelAsync(ownerClient);
        var memberClient = factory.CreateClient();
        var memberAuth = await RegisterAsync(memberClient, "member");
        await memberClient.PostAsync($"/api/servers/{channel.ServerId}/members", null);
        await using var observer = CreateConnection(factory, ownerAuth.AccessToken);
        await using var firstConnection = CreateConnection(factory, memberAuth.AccessToken);
        await using var replacementConnection = CreateConnection(factory, memberAuth.AccessToken);
        var offlineEvent = new TaskCompletionSource<UserPresenceResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        observer.On<UserPresenceResponse>("UserOffline", update =>
        {
            if (update.UserId == memberAuth.User.Id) offlineEvent.TrySetResult(update);
        });

        await observer.StartAsync();
        await firstConnection.StartAsync();
        await firstConnection.StopAsync();
        await Task.Delay(30);
        await replacementConnection.StartAsync();
        await Task.Delay(250);

        Assert.False(offlineEvent.Task.IsCompleted);
        Assert.Equal(UserStatus.Online, factory.GetPresenceStatus(memberAuth.User.Id));
    }

    [Fact]
    public async Task Typing_events_are_authorized_deduplicated_and_not_sent_to_caller()
    {
        await using var factory = new ChatWebApplicationFactory();
        var ownerClient = factory.CreateClient();
        var ownerAuth = await RegisterAsync(ownerClient, "owner");
        var channel = await CreateTextChannelAsync(ownerClient);
        var memberClient = factory.CreateClient();
        var memberAuth = await RegisterAsync(memberClient, "member");
        await memberClient.PostAsync($"/api/servers/{channel.ServerId}/members", null);
        await using var observer = CreateConnection(factory, ownerAuth.AccessToken);
        await using var typingConnection = CreateConnection(factory, memberAuth.AccessToken);
        var started = new TaskCompletionSource<TypingIndicatorResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource<TypingIndicatorResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedCount = 0;
        var callerReceivedOwnEvent = false;
        observer.On<TypingIndicatorResponse>("TypingStarted", update =>
        {
            Interlocked.Increment(ref startedCount);
            started.TrySetResult(update);
        });
        observer.On<TypingIndicatorResponse>("TypingStopped", update => stopped.TrySetResult(update));
        typingConnection.On<TypingIndicatorResponse>("TypingStarted", _ => callerReceivedOwnEvent = true);
        await observer.StartAsync();
        await typingConnection.StartAsync();
        await observer.InvokeAsync("JoinChannel", channel.Id);
        await typingConnection.InvokeAsync("JoinChannel", channel.Id);

        await typingConnection.InvokeAsync("StartTyping", channel.Id);
        await typingConnection.InvokeAsync("StartTyping", channel.Id);
        var startUpdate = await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Task.Delay(50);
        await typingConnection.InvokeAsync("StopTyping", channel.Id);
        var stopUpdate = await stopped.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(channel.Id, startUpdate.ChannelId);
        Assert.Equal(memberAuth.User.Id, startUpdate.UserId);
        Assert.Equal(memberAuth.User.Id, stopUpdate.UserId);
        Assert.Equal(1, startedCount);
        Assert.False(callerReceivedOwnEvent);
    }

    private static HubConnection CreateConnection(ChatWebApplicationFactory factory, string? accessToken) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/chat"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                if (accessToken is not null)
                    options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
            })
            .Build();

    private static async Task<ChannelResponse> CreateTextChannelAsync(HttpClient owner)
    {
        var serverResponse = await owner.PostAsJsonAsync("/api/servers",
            new CreateServerRequest { Name = "Realtime" });
        serverResponse.EnsureSuccessStatusCode();
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
            Username = username,
            Email = $"{username}@concord.test",
            Password = "Concord1"
        });
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return auth;
    }

    private sealed class ChatWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly InMemoryDatabaseRoot _databaseRoot = new();
        private readonly string _databaseName = $"concord-chat-tests-{Guid.NewGuid()}";

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
                services.Configure<PresenceSettings>(options =>
                    options.DisconnectGracePeriod = TimeSpan.FromMilliseconds(150));
            });
        }

        public UserStatus GetPresenceStatus(Guid userId) =>
            Services.GetRequiredService<IPresenceService>().GetStatus(userId);
    }
}
