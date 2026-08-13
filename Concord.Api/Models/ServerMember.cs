namespace Concord.Api.Models;

public sealed class ServerMember
{
    public Guid Id { get; set; }
    public Guid ServerId { get; set; }
    public Server Server { get; set; } = null!;
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public ServerRole Role { get; set; } = ServerRole.MEMBER;
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
}
