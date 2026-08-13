namespace Concord.Api.Models;

public sealed class Server
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Icon { get; set; }
    public Guid OwnerId { get; set; }
    public ApplicationUser Owner { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<ServerMember> Members { get; } = new List<ServerMember>();
    public ICollection<ServerInvite> Invites { get; } = new List<ServerInvite>();
    public ICollection<Channel> Channels { get; } = new List<Channel>();
}
