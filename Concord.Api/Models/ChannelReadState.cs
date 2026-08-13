namespace Concord.Api.Models;

public sealed class ChannelReadState
{
    public Guid ChannelId { get; set; }
    public Channel Channel { get; set; } = null!;
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public Guid LastReadMessageId { get; set; }
    public Message LastReadMessage { get; set; } = null!;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
