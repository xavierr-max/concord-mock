using System.Collections.Concurrent;
using Concord.Api.Configurations;
using Concord.Api.DTOs.Presence;
using Concord.Api.Hubs;
using Concord.Api.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Concord.Api.Services;

public sealed class PresenceService(
    IHubContext<ChatHub> hubContext,
    IOptions<PresenceSettings> settings,
    ILogger<PresenceService> logger) : IPresenceService
{
    private readonly ConcurrentDictionary<Guid, PresenceEntry> _entries = new();
    private readonly TimeSpan _gracePeriod = settings.Value.DisconnectGracePeriod;

    public async Task ConnectedAsync(
        PresenceUser user, string connectionId, IReadOnlyCollection<Guid> serverIds,
        CancellationToken cancellationToken)
    {
        var entry = _entries.GetOrAdd(user.Id, _ => new PresenceEntry(user));
        UserPresenceResponse? update = null;
        Guid[] groups;
        lock (entry.Gate)
        {
            entry.PendingOffline?.Cancel();
            entry.PendingOffline?.Dispose();
            entry.PendingOffline = null;
            entry.User = user;
            entry.ServerIds.UnionWith(serverIds);
            entry.ConnectionIds.Add(connectionId);
            if (entry.Status == UserStatus.Offline)
            {
                entry.Status = UserStatus.Online;
                update = ToResponse(entry);
            }
            groups = entry.ServerIds.ToArray();
        }

        if (update is not null)
            await BroadcastAsync(groups, ChatHubEvents.UserOnline, update, cancellationToken);
    }

    public Task DisconnectedAsync(Guid userId, string connectionId)
    {
        if (!_entries.TryGetValue(userId, out var entry)) return Task.CompletedTask;
        CancellationTokenSource? pending = null;
        lock (entry.Gate)
        {
            entry.ConnectionIds.Remove(connectionId);
            if (entry.ConnectionIds.Count == 0)
            {
                entry.PendingOffline?.Cancel();
                entry.PendingOffline?.Dispose();
                pending = new CancellationTokenSource();
                entry.PendingOffline = pending;
            }
        }

        if (pending is not null) _ = MarkOfflineAfterGracePeriodAsync(userId, entry, pending);
        return Task.CompletedTask;
    }

    public void TrackServer(Guid userId, Guid serverId)
    {
        if (!_entries.TryGetValue(userId, out var entry)) return;
        lock (entry.Gate) entry.ServerIds.Add(serverId);
    }

    public UserStatus GetStatus(Guid userId)
    {
        if (!_entries.TryGetValue(userId, out var entry)) return UserStatus.Offline;
        lock (entry.Gate) return entry.Status;
    }

    private async Task MarkOfflineAfterGracePeriodAsync(
        Guid userId, PresenceEntry entry, CancellationTokenSource pending)
    {
        try
        {
            await Task.Delay(_gracePeriod, pending.Token);
            UserPresenceResponse? update = null;
            Guid[] groups = [];
            lock (entry.Gate)
            {
                if (entry.PendingOffline != pending || entry.ConnectionIds.Count != 0) return;
                entry.PendingOffline = null;
                entry.Status = UserStatus.Offline;
                update = ToResponse(entry);
                groups = entry.ServerIds.ToArray();
            }
            pending.Dispose();
            await BroadcastAsync(groups, ChatHubEvents.UserOffline, update, CancellationToken.None);
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested)
        {
            // A reconnection within the grace period keeps the user online.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to update offline presence for user {UserId}", userId);
        }
    }

    private async Task BroadcastAsync(
        IReadOnlyCollection<Guid> serverIds, string eventName,
        UserPresenceResponse update, CancellationToken cancellationToken)
    {
        if (serverIds.Count == 0) return;
        var groups = serverIds.Select(ChatHub.ServerGroupName).Distinct().ToArray();
        await hubContext.Clients.Groups(groups).SendAsync(eventName, update, cancellationToken);
        await hubContext.Clients.Groups(groups).SendAsync(
            ChatHubEvents.UserStatusChanged, update, cancellationToken);
    }

    private static UserPresenceResponse ToResponse(PresenceEntry entry) => new(
        entry.User.Id, entry.User.Username, entry.User.Avatar, entry.Status, DateTimeOffset.UtcNow);

    private sealed class PresenceEntry(PresenceUser user)
    {
        public object Gate { get; } = new();
        public PresenceUser User { get; set; } = user;
        public HashSet<string> ConnectionIds { get; } = [];
        public HashSet<Guid> ServerIds { get; } = [];
        public UserStatus Status { get; set; } = UserStatus.Offline;
        public CancellationTokenSource? PendingOffline { get; set; }
    }
}
