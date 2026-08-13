namespace Concord.Api.Models;

public sealed class MessageAttachment
{
    public Guid Id { get; set; }
    public Guid MessageId { get; set; }
    public Message Message { get; set; } = null!;
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long FileSize { get; set; }
    public required string Url { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
