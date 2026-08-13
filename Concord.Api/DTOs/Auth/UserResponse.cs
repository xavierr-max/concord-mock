namespace Concord.Api.DTOs.Auth;

public sealed record UserResponse(
    Guid Id,
    string Username,
    string Email,
    string? Avatar,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string Status);
