using Concord.Api.Models;

namespace Concord.Api.DTOs.Presence;

public sealed record UserPresenceResponse(
    Guid UserId,
    string Username,
    string? Avatar,
    UserStatus Status,
    DateTimeOffset ChangedAt);
