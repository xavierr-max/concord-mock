using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Concord.Api.Data;
using Concord.Api.DTOs.Auth;
using Concord.Api.DTOs.Channels;
using Concord.Api.DTOs.Servers;
using Concord.Api.DTOs.Voice;
using Concord.Api.Models;
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

public sealed class VoiceHubTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Connection_without_jwt_is_rejected()
    {
        await using var factory = new VoiceWebApplicationFactory();
        await using var connection = CreateConnection(factory, null);

        await Assert.ThrowsAnyAsync<HttpRequestException>(() => connection.StartAsync());
    }

    [Fact]
    public async Task Member_can_join_update_and_leave_voice_channel_with_events()
    {
        await using var factory = new VoiceWebApplicationFactory();
        var ownerClient = factory.CreateClient();
        var ownerAuth = await RegisterAsync(ownerClient, "owner");
        var channels = await CreateChannelsAsync(ownerClient);
        var memberClient = factory.CreateClient();
        var memberAuth = await RegisterAsync(memberClient, "member");
        await memberClient.PostAsync($"/api/servers/{channels.Voice.ServerId}/members", null);
        await using var observer = CreateConnection(factory, ownerAuth.AccessToken);
        await using var member = CreateConnection(factory, memberAuth.AccessToken);
        var joined = NewEventSource();
        var muted = NewEventSource();
        var deafened = NewEventSource();
        var left = NewEventSource();
        observer.On<VoiceParticipantResponse>("VoiceUserJoined", update =>
        {
            if (update.UserId == memberAuth.User.Id) joined.TrySetResult(update);
        });
        observer.On<VoiceParticipantResponse>("VoiceUserUpdated", update =>
        {
            if (update.UserId != memberAuth.User.Id) return;
            if (update.Muted && !update.Deafened) muted.TrySetResult(update);
            if (update.Muted && update.Deafened) deafened.TrySetResult(update);
        });
        observer.On<VoiceParticipantResponse>("VoiceUserLeft", update =>
        {
            if (update.UserId == memberAuth.User.Id) left.TrySetResult(update);
        });

        await observer.StartAsync();
        await member.StartAsync();
        await observer.InvokeAsync("JoinVoiceChannel", channels.Voice.Id);
        var participant = await member.InvokeAsync<VoiceParticipantResponse>(
            "JoinVoiceChannel", channels.Voice.Id);
        await joined.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await member.InvokeAsync("SetMute", true);
        await muted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await member.InvokeAsync("SetDeafened", true);
        var updated = await deafened.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await member.InvokeAsync("LeaveVoiceChannel");
        var departed = await left.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(memberAuth.User.Id, participant.UserId);
        Assert.Equal(channels.Voice.Id, participant.ChannelId);
        Assert.True(updated.Muted);
        Assert.True(updated.Deafened);
        Assert.Equal(participant.JoinedAt, departed.JoinedAt);
    }

    [Fact]
    public async Task Missing_text_channel_and_non_member_are_rejected()
    {
        await using var factory = new VoiceWebApplicationFactory();
        var ownerClient = factory.CreateClient();
        var ownerAuth = await RegisterAsync(ownerClient, "owner");
        var channels = await CreateChannelsAsync(ownerClient);
        var outsiderClient = factory.CreateClient();
        var outsiderAuth = await RegisterAsync(outsiderClient, "outsider");
        await using var owner = CreateConnection(factory, ownerAuth.AccessToken);
        await using var outsider = CreateConnection(factory, outsiderAuth.AccessToken);
        await owner.StartAsync();
        await outsider.StartAsync();

        await Assert.ThrowsAsync<HubException>(() =>
            owner.InvokeAsync("JoinVoiceChannel", Guid.NewGuid()));
        await Assert.ThrowsAsync<HubException>(() =>
            owner.InvokeAsync("JoinVoiceChannel", channels.Text.Id));
        await Assert.ThrowsAsync<HubException>(() =>
            outsider.InvokeAsync("JoinVoiceChannel", channels.Voice.Id));
        await Assert.ThrowsAsync<HubException>(() => outsider.InvokeAsync("SetMute", true));
        await Assert.ThrowsAsync<HubException>(() => outsider.InvokeAsync("SetDeafened", true));
    }

    [Fact]
    public async Task Disconnect_removes_participant_and_publishes_left_event()
    {
        await using var factory = new VoiceWebApplicationFactory();
        var ownerClient = factory.CreateClient();
        var ownerAuth = await RegisterAsync(ownerClient, "owner");
        var channels = await CreateChannelsAsync(ownerClient);
        var memberClient = factory.CreateClient();
        var memberAuth = await RegisterAsync(memberClient, "member");
        await memberClient.PostAsync($"/api/servers/{channels.Voice.ServerId}/members", null);
        await using var observer = CreateConnection(factory, ownerAuth.AccessToken);
        await using var member = CreateConnection(factory, memberAuth.AccessToken);
        var left = NewEventSource();
        observer.On<VoiceParticipantResponse>("VoiceUserLeft", update =>
        {
            if (update.UserId == memberAuth.User.Id) left.TrySetResult(update);
        });
        await observer.StartAsync();
        await member.StartAsync();
        await observer.InvokeAsync("JoinVoiceChannel", channels.Voice.Id);
        await member.InvokeAsync("JoinVoiceChannel", channels.Voice.Id);

        await member.StopAsync();
        var departed = await left.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(memberAuth.User.Id, departed.UserId);
    }

    [Fact]
    public async Task WebRtc_signals_are_forwarded_only_to_target_in_same_voice_session()
    {
        await using var factory = new VoiceWebApplicationFactory();
        var ownerClient = factory.CreateClient();
        var ownerAuth = await RegisterAsync(ownerClient, "owner");
        var channels = await CreateChannelsAsync(ownerClient);
        var memberClient = factory.CreateClient();
        var memberAuth = await RegisterAsync(memberClient, "member");
        await memberClient.PostAsync($"/api/servers/{channels.Voice.ServerId}/members", null);
        var observerClient = factory.CreateClient();
        var observerAuth = await RegisterAsync(observerClient, "observer");
        await observerClient.PostAsync($"/api/servers/{channels.Voice.ServerId}/members", null);
        await using var owner = CreateConnection(factory, ownerAuth.AccessToken);
        await using var member = CreateConnection(factory, memberAuth.AccessToken);
        await using var observer = CreateConnection(factory, observerAuth.AccessToken);
        var offerReceived = new TaskCompletionSource<VoiceOfferResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var answerReceived = new TaskCompletionSource<VoiceAnswerResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var iceReceived = new TaskCompletionSource<VoiceIceCandidateResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observerReceivedSignal = false;
        member.On<VoiceOfferResponse>("VoiceOfferReceived", signal => offerReceived.TrySetResult(signal));
        owner.On<VoiceAnswerResponse>("VoiceAnswerReceived", signal => answerReceived.TrySetResult(signal));
        member.On<VoiceIceCandidateResponse>("VoiceIceCandidateReceived", signal => iceReceived.TrySetResult(signal));
        observer.On<VoiceOfferResponse>("VoiceOfferReceived", _ => observerReceivedSignal = true);
        observer.On<VoiceAnswerResponse>("VoiceAnswerReceived", _ => observerReceivedSignal = true);
        observer.On<VoiceIceCandidateResponse>("VoiceIceCandidateReceived", _ => observerReceivedSignal = true);
        await owner.StartAsync();
        await member.StartAsync();
        await observer.StartAsync();
        await owner.InvokeAsync("JoinVoiceChannel", channels.Voice.Id);
        await member.InvokeAsync("JoinVoiceChannel", channels.Voice.Id);
        await observer.InvokeAsync("JoinVoiceChannel", channels.Voice.Id);

        await owner.InvokeAsync("SendOffer", memberAuth.User.Id, "test-offer-sdp");
        await member.InvokeAsync("SendAnswer", ownerAuth.User.Id, "test-answer-sdp");
        await owner.InvokeAsync("SendIceCandidate", memberAuth.User.Id, "test-ice-candidate");
        var offer = await offerReceived.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var answer = await answerReceived.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var ice = await iceReceived.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Task.Delay(50);

        Assert.Equal(ownerAuth.User.Id, offer.SenderUserId);
        Assert.Equal(channels.Voice.Id, offer.ChannelId);
        Assert.Equal("test-offer-sdp", offer.Sdp);
        Assert.Equal(memberAuth.User.Id, answer.SenderUserId);
        Assert.Equal("test-answer-sdp", answer.Sdp);
        Assert.Equal("test-ice-candidate", ice.Candidate);
        Assert.False(observerReceivedSignal);
    }

    [Fact]
    public async Task Signaling_requires_sender_and_target_in_same_session()
    {
        await using var factory = new VoiceWebApplicationFactory();
        var ownerClient = factory.CreateClient();
        var ownerAuth = await RegisterAsync(ownerClient, "owner");
        var channels = await CreateChannelsAsync(ownerClient);
        var memberClient = factory.CreateClient();
        var memberAuth = await RegisterAsync(memberClient, "member");
        await memberClient.PostAsync($"/api/servers/{channels.Voice.ServerId}/members", null);
        var otherVoiceResponse = await ownerClient.PostAsJsonAsync(
            $"/api/servers/{channels.Voice.ServerId}/channels",
            new SaveChannelRequest { Name = "Other voice", Type = ChannelType.Voice, Position = 2 }, JsonOptions);
        var otherVoice = (await otherVoiceResponse.Content.ReadFromJsonAsync<ChannelResponse>(JsonOptions))!;
        await using var owner = CreateConnection(factory, ownerAuth.AccessToken);
        await using var member = CreateConnection(factory, memberAuth.AccessToken);
        await owner.StartAsync();
        await member.StartAsync();

        await Assert.ThrowsAsync<HubException>(() =>
            owner.InvokeAsync("SendOffer", memberAuth.User.Id, "offer"));
        await owner.InvokeAsync("JoinVoiceChannel", channels.Voice.Id);
        await member.InvokeAsync("JoinVoiceChannel", otherVoice.Id);
        await Assert.ThrowsAsync<HubException>(() =>
            owner.InvokeAsync("SendAnswer", memberAuth.User.Id, "answer"));
        await Assert.ThrowsAsync<HubException>(() =>
            owner.InvokeAsync("SendIceCandidate", Guid.NewGuid(), "candidate"));
        await Assert.ThrowsAsync<HubException>(() =>
            owner.InvokeAsync("SendOffer", memberAuth.User.Id, ""));
    }

    [Fact]
    public async Task Joining_user_receives_existing_participants_muted_state()
    {
        await using var factory = new VoiceWebApplicationFactory();
        var ownerClient = factory.CreateClient();
        var ownerAuth = await RegisterAsync(ownerClient, "owner");
        var channels = await CreateChannelsAsync(ownerClient);
        var memberClient = factory.CreateClient();
        var memberAuth = await RegisterAsync(memberClient, "member");
        await memberClient.PostAsync($"/api/servers/{channels.Voice.ServerId}/members", null);
        await using var owner = CreateConnection(factory, ownerAuth.AccessToken);
        await using var member = CreateConnection(factory, memberAuth.AccessToken);
        var ownerState = NewEventSource();
        member.On<VoiceParticipantResponse>("VoiceUserUpdated", participant =>
        {
            if (participant.UserId == ownerAuth.User.Id) ownerState.TrySetResult(participant);
        });
        await owner.StartAsync();
        await member.StartAsync();
        await owner.InvokeAsync("JoinVoiceChannel", channels.Voice.Id);
        await owner.InvokeAsync("SetMute", true);

        await member.InvokeAsync("JoinVoiceChannel", channels.Voice.Id);
        var existingParticipant = await ownerState.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.True(existingParticipant.Muted);
    }

    private static TaskCompletionSource<VoiceParticipantResponse> NewEventSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static HubConnection CreateConnection(VoiceWebApplicationFactory factory, string? token) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/voice"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                if (token is not null) options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();

    private static async Task<(ChannelResponse Voice, ChannelResponse Text)> CreateChannelsAsync(HttpClient owner)
    {
        var serverResponse = await owner.PostAsJsonAsync("/api/servers",
            new CreateServerRequest { Name = "Voice" });
        var server = (await serverResponse.Content.ReadFromJsonAsync<ServerResponse>(JsonOptions))!;
        var voiceResponse = await owner.PostAsJsonAsync($"/api/servers/{server.Id}/channels",
            new SaveChannelRequest { Name = "Voice", Type = ChannelType.Voice, Position = 0 }, JsonOptions);
        var textResponse = await owner.PostAsJsonAsync($"/api/servers/{server.Id}/channels",
            new SaveChannelRequest { Name = "Text", Type = ChannelType.Text, Position = 1 }, JsonOptions);
        return ((await voiceResponse.Content.ReadFromJsonAsync<ChannelResponse>(JsonOptions))!,
            (await textResponse.Content.ReadFromJsonAsync<ChannelResponse>(JsonOptions))!);
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, string username)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Username = username, Email = $"{username}@voice.test", Password = "Concord1"
        });
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return auth;
    }

    private sealed class VoiceWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly InMemoryDatabaseRoot databaseRoot = new();
        private readonly string databaseName = $"concord-voice-tests-{Guid.NewGuid()}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<ConcordDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ConcordDbContext>>();
                services.RemoveAll<ConcordDbContext>();
                services.AddDbContext<ConcordDbContext>(options =>
                    options.UseInMemoryDatabase(databaseName, databaseRoot));
            });
        }
    }
}
