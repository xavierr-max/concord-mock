namespace Concord.Api.DTOs.Invites;

public sealed record ServerInviteResponse(
    Guid Id,
    Guid ServerId,
    string ServerName,
    string? ServerIcon,
    string Code,
    Guid CreatedByUserId,
    DateTimeOffset ExpiresAt,
    int? MaxUses,
    int Uses,
    DateTimeOffset CreatedAt);
