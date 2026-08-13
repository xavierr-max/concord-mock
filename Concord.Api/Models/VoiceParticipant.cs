namespace Concord.Api.Models;

public sealed class VoiceParticipant
{
    public Guid UserId { get; init; }
    public Guid ChannelId { get; init; }
    public DateTimeOffset JoinedAt { get; init; }
    public bool Muted { get; set; }
    public bool Deafened { get; set; }
    internal HashSet<string> ConnectionIds { get; } = [];
}
