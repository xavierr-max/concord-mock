namespace Concord.Api.Models;

public sealed class VoiceSession
{
    public Guid ChannelId { get; init; }
    public IReadOnlyCollection<VoiceParticipant> Participants => ParticipantMap.Values;
    internal Dictionary<Guid, VoiceParticipant> ParticipantMap { get; } = [];
}
