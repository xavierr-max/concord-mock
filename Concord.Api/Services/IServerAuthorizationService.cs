namespace Concord.Api.Services;

public enum ServerPermission
{
    ManageChannels,
    ManageInvites,
    ModerateMembers,
    ModerateMessages,
    ViewChannels,
    SendMessages,
    JoinVoiceChannels
}

public interface IServerAuthorizationService
{
    Task<bool> IsMemberAsync(Guid serverId, Guid userId, CancellationToken cancellationToken);
    Task<bool> IsOwnerAsync(Guid serverId, Guid userId, CancellationToken cancellationToken);
    Task<bool> IsAdminAsync(Guid serverId, Guid userId, CancellationToken cancellationToken);
    Task<bool> HasPermissionAsync(
        Guid serverId, Guid userId, ServerPermission permission, CancellationToken cancellationToken);
}
