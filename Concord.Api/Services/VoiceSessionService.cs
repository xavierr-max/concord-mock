using Concord.Api.DTOs.Voice;
using Concord.Api.Models;

namespace Concord.Api.Services;

public sealed class VoiceSessionService : IVoiceSessionService
{
    private readonly object sync = new();
    private readonly Dictionary<Guid, VoiceSession> sessions = [];
    private readonly Dictionary<string, (Guid ChannelId, Guid UserId)> connections = [];

    public VoiceJoinResult Join(Guid channelId, Guid userId, string connectionId)
    {
        lock (sync)
        {
            VoiceParticipantResponse? previous = null;
            var leftPrevious = false;
            if (connections.TryGetValue(connectionId, out var current))
            {
                if (current.ChannelId == channelId)
                {
                    var existing = sessions[channelId].ParticipantMap[userId];
                    return new(ToResponse(existing), false, null, false);
                }

                var leave = LeaveCore(connectionId, current);
                previous = leave.Participant;
                leftPrevious = leave.UserLeft;
            }

            if (!sessions.TryGetValue(channelId, out var session))
            {
                session = new VoiceSession { ChannelId = channelId };
                sessions[channelId] = session;
            }

            var isNewParticipant = !session.ParticipantMap.TryGetValue(userId, out var participant);
            if (isNewParticipant)
            {
                participant = new VoiceParticipant
                {
                    UserId = userId, ChannelId = channelId, JoinedAt = DateTimeOffset.UtcNow
                };
                session.ParticipantMap[userId] = participant;
            }

            participant!.ConnectionIds.Add(connectionId);
            connections[connectionId] = (channelId, userId);
            return new(ToResponse(participant), isNewParticipant, previous, leftPrevious);
        }
    }

    public VoiceLeaveResult? Leave(string connectionId)
    {
        lock (sync)
        {
            return connections.TryGetValue(connectionId, out var current)
                ? LeaveCore(connectionId, current)
                : null;
        }
    }

    public VoiceParticipantResponse? SetMute(string connectionId, bool muted) =>
        Update(connectionId, participant => participant.Muted = muted);

    public VoiceParticipantResponse? SetDeafened(string connectionId, bool deafened) =>
        Update(connectionId, participant => participant.Deafened = deafened);

    public Guid? GetChannelId(string connectionId)
    {
        lock (sync)
            return connections.TryGetValue(connectionId, out var current) ? current.ChannelId : null;
    }

    public VoiceSignalRoute? GetSignalRoute(
        string connectionId, Guid senderUserId, Guid targetUserId)
    {
        lock (sync)
        {
            if (senderUserId == targetUserId
                || !connections.TryGetValue(connectionId, out var sender)
                || sender.UserId != senderUserId
                || !sessions.TryGetValue(sender.ChannelId, out var session)
                || !session.ParticipantMap.TryGetValue(targetUserId, out var target))
                return null;
            return new VoiceSignalRoute(sender.ChannelId, target.ConnectionIds.ToArray());
        }
    }

    public IReadOnlyCollection<VoiceParticipantResponse> GetParticipants(Guid channelId)
    {
        lock (sync)
            return sessions.TryGetValue(channelId, out var session)
                ? session.ParticipantMap.Values.Select(ToResponse).ToArray()
                : [];
    }

    private VoiceParticipantResponse? Update(string connectionId, Action<VoiceParticipant> update)
    {
        lock (sync)
        {
            if (!connections.TryGetValue(connectionId, out var current)) return null;
            var participant = sessions[current.ChannelId].ParticipantMap[current.UserId];
            update(participant);
            return ToResponse(participant);
        }
    }

    private VoiceLeaveResult LeaveCore(
        string connectionId, (Guid ChannelId, Guid UserId) current)
    {
        var session = sessions[current.ChannelId];
        var participant = session.ParticipantMap[current.UserId];
        connections.Remove(connectionId);
        participant.ConnectionIds.Remove(connectionId);
        var userLeft = participant.ConnectionIds.Count == 0;
        var response = ToResponse(participant);
        if (userLeft) session.ParticipantMap.Remove(current.UserId);
        if (session.ParticipantMap.Count == 0) sessions.Remove(current.ChannelId);
        return new(response, userLeft);
    }

    private static VoiceParticipantResponse ToResponse(VoiceParticipant participant) => new(
        participant.UserId, participant.ChannelId, participant.JoinedAt,
        participant.Muted, participant.Deafened);
}
