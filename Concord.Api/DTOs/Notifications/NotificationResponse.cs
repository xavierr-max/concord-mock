namespace Concord.Api.DTOs.Notifications;

public sealed record NotificationResponse(
    Guid Id,
    string Type,
    string Title,
    string Content,
    Guid? ActorUserId,
    Guid? ServerId,
    Guid? ChannelId,
    Guid? MessageId,
    bool IsRead,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

public sealed record PagedNotificationsResponse(
    IReadOnlyCollection<NotificationResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record UnreadNotificationsResponse(int UnreadCount);
