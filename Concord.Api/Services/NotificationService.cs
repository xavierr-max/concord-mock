using System.Text.RegularExpressions;
using Concord.Api.Data;
using Concord.Api.DTOs.Notifications;
using Concord.Api.Hubs;
using Concord.Api.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Concord.Api.Services;

public sealed partial class NotificationService(
    ConcordDbContext dbContext,
    IHubContext<NotificationHub> hubContext) : INotificationService
{
    public async Task CreateMessageNotificationsAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var message = await dbContext.Messages.AsNoTracking()
            .Include(item => item.Author).Include(item => item.Channel).ThenInclude(item => item.Server)
            .SingleAsync(item => item.Id == messageId, cancellationToken);
        var recipients = await dbContext.ServerMembers.AsNoTracking().Include(item => item.User)
            .Where(item => item.ServerId == message.Channel.ServerId && item.UserId != message.AuthorId)
            .Select(item => new { item.UserId, item.User.Username }).ToListAsync(cancellationToken);
        var mentions = MentionRegex().Matches(message.Content)
            .Select(match => match.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var notifications = recipients.Select(recipient => new Notification
        {
            Id = Guid.NewGuid(), UserId = recipient.UserId, ActorUserId = message.AuthorId,
            ServerId = message.Channel.ServerId, ChannelId = message.ChannelId, MessageId = message.Id,
            Type = mentions.Contains(recipient.Username) ? NotificationType.Mention : NotificationType.NewMessage,
            Title = mentions.Contains(recipient.Username)
                ? $"{message.Author.Username} mencionou você"
                : $"Nova mensagem em #{message.Channel.Name}",
            Content = TrimContent(message.Content), CreatedAt = message.CreatedAt
        }).ToArray();
        await PersistAndPublishAsync(notifications, cancellationToken);
    }

    public async Task CreateInviteCreatedNotificationsAsync(
        Guid inviteId, Guid actorUserId, CancellationToken cancellationToken)
    {
        var invite = await dbContext.ServerInvites.AsNoTracking().Include(item => item.Server)
            .SingleAsync(item => item.Id == inviteId, cancellationToken);
        var recipientIds = await dbContext.ServerMembers.AsNoTracking()
            .Where(item => item.ServerId == invite.ServerId && item.UserId != actorUserId)
            .Select(item => item.UserId).ToListAsync(cancellationToken);
        await PersistAndPublishAsync(recipientIds.Select(userId => new Notification
        {
            Id = Guid.NewGuid(), UserId = userId, ActorUserId = actorUserId, ServerId = invite.ServerId,
            Type = NotificationType.InviteCreated, Title = $"Novo convite em {invite.Server.Name}",
            Content = "Um novo convite para o servidor foi criado.", CreatedAt = DateTimeOffset.UtcNow
        }), cancellationToken);
    }

    public async Task CreateMemberJoinedNotificationsAsync(
        Guid serverId, Guid actorUserId, Guid? inviteCreatorUserId, CancellationToken cancellationToken)
    {
        var actor = await dbContext.Users.AsNoTracking().SingleAsync(item => item.Id == actorUserId, cancellationToken);
        var serverName = await dbContext.Servers.AsNoTracking().Where(item => item.Id == serverId)
            .Select(item => item.Name).SingleAsync(cancellationToken);
        var recipientIds = await dbContext.ServerMembers.AsNoTracking()
            .Where(item => item.ServerId == serverId && item.UserId != actorUserId)
            .Select(item => item.UserId).Distinct().ToListAsync(cancellationToken);
        var notifications = recipientIds.Select(userId => new Notification
        {
            Id = Guid.NewGuid(), UserId = userId, ActorUserId = actorUserId, ServerId = serverId,
            Type = inviteCreatorUserId == userId ? NotificationType.InviteAccepted : NotificationType.ServerMemberJoined,
            Title = inviteCreatorUserId == userId ? "Seu convite foi aceito" : $"Novo membro em {serverName}",
            Content = $"{actor.Username} entrou no servidor.", CreatedAt = DateTimeOffset.UtcNow
        });
        await PersistAndPublishAsync(notifications, cancellationToken);
    }

    public async Task<PagedNotificationsResponse> ListAsync(
        Guid userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = dbContext.Notifications.AsNoTracking().Where(item => item.UserId == userId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).Select(item => new NotificationResponse(
                item.Id, item.Type.ToString(), item.Title, item.Content, item.ActorUserId,
                item.ServerId, item.ChannelId, item.MessageId, item.IsRead, item.CreatedAt, item.ReadAt))
            .ToArrayAsync(cancellationToken);
        return new(items, page, pageSize, total, (int)Math.Ceiling(total / (double)pageSize));
    }

    public Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.Notifications.CountAsync(item => item.UserId == userId && !item.IsRead, cancellationToken);

    public async Task<NotificationResponse?> MarkReadAsync(
        Guid notificationId, Guid userId, CancellationToken cancellationToken)
    {
        var notification = await dbContext.Notifications.SingleOrDefaultAsync(
            item => item.Id == notificationId && item.UserId == userId, cancellationToken);
        if (notification is null) return null;
        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            await hubContext.Clients.User(userId.ToString()).SendAsync(
                NotificationHubEvents.NotificationRead, ToResponse(notification), cancellationToken);
        }
        return ToResponse(notification);
    }

    public async Task<int> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var count = await dbContext.Notifications.Where(item => item.UserId == userId && !item.IsRead)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsRead, true)
                .SetProperty(item => item.ReadAt, now), cancellationToken);
        if (count > 0) await hubContext.Clients.User(userId.ToString())
            .SendAsync(NotificationHubEvents.AllNotificationsRead, now, cancellationToken);
        return count;
    }

    private async Task PersistAndPublishAsync(
        IEnumerable<Notification> source, CancellationToken cancellationToken)
    {
        var notifications = source.ToArray();
        if (notifications.Length == 0) return;
        dbContext.Notifications.AddRange(notifications);
        await dbContext.SaveChangesAsync(cancellationToken);
        foreach (var notification in notifications)
            await hubContext.Clients.User(notification.UserId.ToString()).SendAsync(
                NotificationHubEvents.NotificationCreated, ToResponse(notification), cancellationToken);
    }

    private static NotificationResponse ToResponse(Notification item) => new(
        item.Id, item.Type.ToString(), item.Title, item.Content, item.ActorUserId,
        item.ServerId, item.ChannelId, item.MessageId, item.IsRead, item.CreatedAt, item.ReadAt);

    private static string TrimContent(string content) => content.Length <= 240 ? content : content[..237] + "...";

    [GeneratedRegex(@"(?<![\w])@([A-Za-z0-9_.-]{3,32})", RegexOptions.CultureInvariant)]
    private static partial Regex MentionRegex();
}
