using Concord.Api.Data;
using Concord.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Concord.Api.Services;

public sealed class ServerAuthorizationService(ConcordDbContext dbContext) : IServerAuthorizationService
{
    public Task<bool> IsMemberAsync(Guid serverId, Guid userId, CancellationToken cancellationToken) =>
        dbContext.ServerMembers.AsNoTracking().AnyAsync(member =>
            member.ServerId == serverId && member.UserId == userId, cancellationToken);

    public Task<bool> IsOwnerAsync(Guid serverId, Guid userId, CancellationToken cancellationToken) =>
        HasRoleAsync(serverId, userId, ServerRole.OWNER, cancellationToken);

    public Task<bool> IsAdminAsync(Guid serverId, Guid userId, CancellationToken cancellationToken) =>
        HasRoleAsync(serverId, userId, ServerRole.ADMIN, cancellationToken);

    public async Task<bool> HasPermissionAsync(
        Guid serverId, Guid userId, ServerPermission permission, CancellationToken cancellationToken)
    {
        var role = await dbContext.ServerMembers.AsNoTracking()
            .Where(member => member.ServerId == serverId && member.UserId == userId)
            .Select(member => (ServerRole?)member.Role)
            .SingleOrDefaultAsync(cancellationToken);
        return role switch
        {
            ServerRole.OWNER => true,
            ServerRole.ADMIN => true,
            ServerRole.MEMBER => permission is ServerPermission.ViewChannels
                or ServerPermission.SendMessages or ServerPermission.JoinVoiceChannels,
            _ => false
        };
    }

    private Task<bool> HasRoleAsync(
        Guid serverId, Guid userId, ServerRole role, CancellationToken cancellationToken) =>
        dbContext.ServerMembers.AsNoTracking().AnyAsync(member =>
            member.ServerId == serverId && member.UserId == userId && member.Role == role,
            cancellationToken);
}
