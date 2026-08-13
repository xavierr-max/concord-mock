using Concord.Api.Models;

namespace Concord.Api.Services;

public sealed record PresenceUser(Guid Id, string Username, string? Avatar);

public interface IPresenceService
{
    Task ConnectedAsync(
        PresenceUser user, string connectionId, IReadOnlyCollection<Guid> serverIds,
        CancellationToken cancellationToken);
    Task DisconnectedAsync(Guid userId, string connectionId);
    void TrackServer(Guid userId, Guid serverId);
    UserStatus GetStatus(Guid userId);
}
