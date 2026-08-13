namespace Concord.Api.Models;

public sealed class Notification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public NotificationType Type { get; set; }
    public required string Title { get; set; }
    public required string Content { get; set; }
    public Guid? ActorUserId { get; set; }
    public Guid? ServerId { get; set; }
    public Guid? ChannelId { get; set; }
    public Guid? MessageId { get; set; }
    public bool IsRead { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadAt { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public ApplicationUser? ActorUser { get; set; }
}
