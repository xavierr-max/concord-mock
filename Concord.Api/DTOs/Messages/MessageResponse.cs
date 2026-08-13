namespace Concord.Api.DTOs.Messages;

public sealed record MessageAuthorResponse(Guid Id, string Username, string? Avatar);

public sealed record MessageResponse(
    Guid Id,
    Guid ChannelId,
    MessageAuthorResponse Author,
    string? Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsDeleted);

public sealed record PagedMessagesResponse(
    IReadOnlyCollection<MessageResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
