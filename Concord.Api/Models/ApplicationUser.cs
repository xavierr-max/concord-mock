using Microsoft.AspNetCore.Identity;

namespace Concord.Api.Models;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public required string Username { get; set; }
    public string? Avatar { get; set; }
    public string? DisplayName { get; set; }
    public string? Bio { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public UserStatus Status { get; set; } = UserStatus.Offline;
    public ICollection<RefreshToken> RefreshTokens { get; } = new List<RefreshToken>();
    public ICollection<Server> OwnedServers { get; } = new List<Server>();
    public ICollection<ServerMember> ServerMemberships { get; } = new List<ServerMember>();
    public ICollection<ServerInvite> CreatedServerInvites { get; } = new List<ServerInvite>();
    public ICollection<Message> Messages { get; } = new List<Message>();
}
