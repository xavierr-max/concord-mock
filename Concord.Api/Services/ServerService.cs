using Concord.Api.Data;
using Concord.Api.DTOs.Servers;
using Concord.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Concord.Api.Services;

public sealed class ServerService(
    ConcordDbContext dbContext,
    IServerAuthorizationService authorizationService,
    INotificationService notificationService) : IServerService
{
    public async Task<ServerResponse> CreateAsync(
        Guid userId, CreateServerRequest request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var server = new Server
        {
            Id = Guid.NewGuid(), Name = request.Name.Trim(), Icon = NormalizeOptional(request.Icon),
            OwnerId = userId, CreatedAt = now, UpdatedAt = now
        };
        server.Members.Add(new ServerMember
        {
            Id = Guid.NewGuid(), UserId = userId, Role = ServerRole.OWNER, JoinedAt = now
        });
        dbContext.Servers.Add(server);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (await LoadResponseAsync(server.Id, cancellationToken))!;
    }

    public async Task<IReadOnlyCollection<ServerResponse>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var ids = await dbContext.ServerMembers.AsNoTracking()
            .Where(member => member.UserId == userId)
            .OrderBy(member => member.JoinedAt)
            .Select(member => member.ServerId)
            .ToListAsync(cancellationToken);
        var result = new List<ServerResponse>(ids.Count);
        foreach (var id in ids) result.Add((await LoadResponseAsync(id, cancellationToken))!);
        return result;
    }

    public async Task<ServerOperationResult<ServerResponse>> GetAsync(
        Guid serverId, Guid userId, CancellationToken cancellationToken)
    {
        var access = await CheckAccessAsync(serverId, userId, cancellationToken);
        if (access != ServerOperationStatus.Success) return new(access);
        return new(ServerOperationStatus.Success, await LoadResponseAsync(serverId, cancellationToken));
    }

    public async Task<ServerOperationResult<ServerResponse>> UpdateAsync(
        Guid serverId, Guid userId, UpdateServerRequest request, CancellationToken cancellationToken)
    {
        var server = await dbContext.Servers.SingleOrDefaultAsync(item => item.Id == serverId, cancellationToken);
        if (server is null) return new(ServerOperationStatus.NotFound);
        if (!await authorizationService.IsOwnerAsync(serverId, userId, cancellationToken))
            return new(ServerOperationStatus.Forbidden);
        server.Name = request.Name.Trim();
        server.Icon = NormalizeOptional(request.Icon);
        server.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(ServerOperationStatus.Success, await LoadResponseAsync(serverId, cancellationToken));
    }

    public async Task<ServerOperationStatus> DeleteAsync(
        Guid serverId, Guid userId, CancellationToken cancellationToken)
    {
        var server = await dbContext.Servers.SingleOrDefaultAsync(item => item.Id == serverId, cancellationToken);
        if (server is null) return ServerOperationStatus.NotFound;
        if (!await authorizationService.IsOwnerAsync(serverId, userId, cancellationToken))
            return ServerOperationStatus.Forbidden;
        dbContext.Servers.Remove(server);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ServerOperationStatus.Success;
    }

    public async Task<ServerOperationResult<ServerMemberResponse>> JoinAsync(
        Guid serverId, Guid userId, CancellationToken cancellationToken)
    {
        if (!await dbContext.Servers.AnyAsync(server => server.Id == serverId, cancellationToken))
            return new(ServerOperationStatus.NotFound);
        if (await dbContext.ServerMembers.AnyAsync(member =>
                member.ServerId == serverId && member.UserId == userId, cancellationToken))
            return new(ServerOperationStatus.Conflict);
        var member = new ServerMember
        {
            Id = Guid.NewGuid(), ServerId = serverId, UserId = userId,
            Role = ServerRole.MEMBER, JoinedAt = DateTimeOffset.UtcNow
        };
        dbContext.ServerMembers.Add(member);
        await dbContext.SaveChangesAsync(cancellationToken);
        var loaded = await dbContext.ServerMembers.AsNoTracking().Include(item => item.User)
            .SingleAsync(item => item.Id == member.Id, cancellationToken);
        await notificationService.CreateMemberJoinedNotificationsAsync(
            serverId, userId, null, cancellationToken);
        return new(ServerOperationStatus.Success, ToMemberResponse(loaded));
    }

    public async Task<ServerOperationStatus> RemoveMemberAsync(
        Guid serverId, Guid authenticatedUserId, Guid targetUserId, CancellationToken cancellationToken)
    {
        var server = await dbContext.Servers.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == serverId, cancellationToken);
        if (server is null) return ServerOperationStatus.NotFound;
        var member = await dbContext.ServerMembers.SingleOrDefaultAsync(item =>
            item.ServerId == serverId && item.UserId == targetUserId, cancellationToken);
        if (member is null) return ServerOperationStatus.NotFound;
        if (member.Role == ServerRole.OWNER) return ServerOperationStatus.Forbidden;
        if (authenticatedUserId != targetUserId)
        {
            if (!await authorizationService.HasPermissionAsync(
                    serverId, authenticatedUserId, ServerPermission.ModerateMembers, cancellationToken))
                return ServerOperationStatus.Forbidden;
            if (await authorizationService.IsAdminAsync(
                    serverId, authenticatedUserId, cancellationToken) && member.Role != ServerRole.MEMBER)
                return ServerOperationStatus.Forbidden;
        }
        dbContext.ServerMembers.Remove(member);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ServerOperationStatus.Success;
    }

    private async Task<ServerOperationStatus> CheckAccessAsync(
        Guid serverId, Guid userId, CancellationToken cancellationToken)
    {
        if (!await dbContext.Servers.AnyAsync(server => server.Id == serverId, cancellationToken))
            return ServerOperationStatus.NotFound;
        return await authorizationService.IsMemberAsync(serverId, userId, cancellationToken)
            ? ServerOperationStatus.Success : ServerOperationStatus.Forbidden;
    }

    private async Task<ServerResponse?> LoadResponseAsync(Guid serverId, CancellationToken cancellationToken)
    {
        var server = await dbContext.Servers.AsNoTracking().Include(item => item.Members).ThenInclude(member => member.User)
            .SingleOrDefaultAsync(item => item.Id == serverId, cancellationToken);
        return server is null ? null : new ServerResponse(server.Id, server.Name, server.Icon, server.OwnerId,
            server.CreatedAt, server.UpdatedAt, server.Members.OrderBy(member => member.JoinedAt)
                .Select(ToMemberResponse).ToArray());
    }

    private static ServerMemberResponse ToMemberResponse(ServerMember member) =>
        new(member.Id, member.UserId, member.User.Username, member.User.Avatar,
            member.Role.ToString(), member.JoinedAt);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
