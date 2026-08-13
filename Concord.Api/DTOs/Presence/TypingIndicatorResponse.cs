namespace Concord.Api.DTOs.Presence;

public sealed record TypingIndicatorResponse(
    Guid ChannelId,
    Guid UserId,
    string Username,
    string? Avatar,
    DateTimeOffset OccurredAt);
