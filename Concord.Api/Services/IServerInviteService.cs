using Concord.Api.DTOs.Invites;
using Concord.Api.DTOs.Servers;

namespace Concord.Api.Services;

public enum InviteOperationStatus
{
    Success,
    NotFound,
    Forbidden,
    Expired,
    LimitReached,
    AlreadyMember
}

public sealed record InviteOperationResult<T>(InviteOperationStatus Status, T? Value = default);

public interface IServerInviteService
{
    Task<InviteOperationResult<ServerInviteResponse>> CreateAsync(
        Guid serverId, Guid userId, CreateServerInviteRequest request, CancellationToken cancellationToken);
    Task<InviteOperationResult<ServerInviteResponse>> GetAsync(string code, CancellationToken cancellationToken);
    Task<InviteOperationResult<ServerMemberResponse>> AcceptAsync(
        string code, Guid userId, CancellationToken cancellationToken);
    Task<InviteOperationStatus> DeleteAsync(string code, Guid userId, CancellationToken cancellationToken);
}
