namespace Concord.Api.DTOs.Users;

public sealed record UserProfileResponse(
    Guid Id,
    string Username,
    string? DisplayName,
    string? Bio,
    string? Avatar,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string Status);
