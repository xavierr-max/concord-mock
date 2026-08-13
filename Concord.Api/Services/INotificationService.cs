using Concord.Api.DTOs.Notifications;

namespace Concord.Api.Services;

public interface INotificationService
{
    Task CreateMessageNotificationsAsync(Guid messageId, CancellationToken cancellationToken);
    Task CreateInviteCreatedNotificationsAsync(Guid inviteId, Guid actorUserId, CancellationToken cancellationToken);
    Task CreateMemberJoinedNotificationsAsync(Guid serverId, Guid actorUserId, Guid? inviteCreatorUserId, CancellationToken cancellationToken);
    Task<PagedNotificationsResponse> ListAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken);
    Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken);
    Task<NotificationResponse?> MarkReadAsync(Guid notificationId, Guid userId, CancellationToken cancellationToken);
    Task<int> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken);
}
