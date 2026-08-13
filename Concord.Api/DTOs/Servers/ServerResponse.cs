namespace Concord.Api.DTOs.Servers;

public sealed record ServerMemberResponse(
    Guid Id,
    Guid UserId,
    string Username,
    string? Avatar,
    string Role,
    DateTimeOffset JoinedAt);

public sealed record ServerResponse(
    Guid Id,
    string Name,
    string? Icon,
    Guid OwnerId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyCollection<ServerMemberResponse> Members);
