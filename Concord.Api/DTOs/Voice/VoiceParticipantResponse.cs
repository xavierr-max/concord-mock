namespace Concord.Api.DTOs.Voice;

public sealed record VoiceParticipantResponse(
    Guid UserId,
    Guid ChannelId,
    DateTimeOffset JoinedAt,
    bool Muted,
    bool Deafened);
