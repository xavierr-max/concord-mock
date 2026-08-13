using Concord.Api.DTOs.Voice;

namespace Concord.Api.Services;

public sealed record VoiceJoinResult(
    VoiceParticipantResponse Participant,
    bool UserJoined,
    VoiceParticipantResponse? PreviousParticipant,
    bool UserLeftPreviousChannel);

public sealed record VoiceLeaveResult(VoiceParticipantResponse Participant, bool UserLeft);

public sealed record VoiceSignalRoute(Guid ChannelId, IReadOnlyCollection<string> TargetConnectionIds);

public interface IVoiceSessionService
{
    VoiceJoinResult Join(Guid channelId, Guid userId, string connectionId);
    VoiceLeaveResult? Leave(string connectionId);
    VoiceParticipantResponse? SetMute(string connectionId, bool muted);
    VoiceParticipantResponse? SetDeafened(string connectionId, bool deafened);
    Guid? GetChannelId(string connectionId);
    VoiceSignalRoute? GetSignalRoute(string connectionId, Guid senderUserId, Guid targetUserId);
    IReadOnlyCollection<VoiceParticipantResponse> GetParticipants(Guid channelId);
}
