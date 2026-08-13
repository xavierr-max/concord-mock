using Concord.Api.Models;

namespace Concord.Api.DTOs.Channels;

public sealed record ChannelResponse(
    Guid Id,
    Guid ServerId,
    string Name,
    ChannelType Type,
    int Position,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
