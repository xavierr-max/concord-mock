using Concord.Api.DTOs.Servers;

namespace Concord.Api.Services;

public enum ServerOperationStatus { Success, NotFound, Forbidden, Conflict }

public sealed record ServerOperationResult<T>(ServerOperationStatus Status, T? Value = default);

public interface IServerService
{
    Task<ServerResponse> CreateAsync(Guid userId, CreateServerRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ServerResponse>> ListAsync(Guid userId, CancellationToken cancellationToken);
    Task<ServerOperationResult<ServerResponse>> GetAsync(Guid serverId, Guid userId, CancellationToken cancellationToken);
    Task<ServerOperationResult<ServerResponse>> UpdateAsync(Guid serverId, Guid userId, UpdateServerRequest request, CancellationToken cancellationToken);
    Task<ServerOperationStatus> DeleteAsync(Guid serverId, Guid userId, CancellationToken cancellationToken);
    Task<ServerOperationResult<ServerMemberResponse>> JoinAsync(Guid serverId, Guid userId, CancellationToken cancellationToken);
    Task<ServerOperationStatus> RemoveMemberAsync(Guid serverId, Guid authenticatedUserId, Guid targetUserId, CancellationToken cancellationToken);
}
