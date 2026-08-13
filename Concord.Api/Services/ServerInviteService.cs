using System.Security.Cryptography;
using Concord.Api.Data;
using Concord.Api.DTOs.Invites;
using Concord.Api.DTOs.Servers;
using Concord.Api.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Concord.Api.Services;

public sealed class ServerInviteService(
    ConcordDbContext dbContext,
    IServerAuthorizationService authorizationService,
    INotificationService notificationService) : IServerInviteService
{
    public async Task<InviteOperationResult<ServerInviteResponse>> CreateAsync(
        Guid serverId, Guid userId, CreateServerInviteRequest request, CancellationToken cancellationToken)
    {
        if (!await dbContext.Servers.AnyAsync(server => server.Id == serverId, cancellationToken))
            return new(InviteOperationStatus.NotFound);
        if (!await authorizationService.HasPermissionAsync(
                serverId, userId, ServerPermission.ManageInvites, cancellationToken))
            return new(InviteOperationStatus.Forbidden);

        var invite = new ServerInvite
        {
            Id = Guid.NewGuid(), ServerId = serverId, Code = await CreateUniqueCodeAsync(cancellationToken),
            CreatedByUserId = userId, ExpiresAt = request.ExpiresAt,
            MaxUses = request.MaxUses, CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.ServerInvites.Add(invite);
        await dbContext.SaveChangesAsync(cancellationToken);
        await notificationService.CreateInviteCreatedNotificationsAsync(invite.Id, userId, cancellationToken);
        return new(InviteOperationStatus.Success,
            await LoadResponseAsync(invite.Code, cancellationToken));
    }

    public async Task<InviteOperationResult<ServerInviteResponse>> GetAsync(
        string code, CancellationToken cancellationToken)
    {
        var invite = await LoadResponseAsync(code, cancellationToken);
        if (invite is null) return new(InviteOperationStatus.NotFound);
        if (invite.ExpiresAt <= DateTimeOffset.UtcNow) return new(InviteOperationStatus.Expired);
        if (invite.MaxUses.HasValue && invite.Uses >= invite.MaxUses.Value)
            return new(InviteOperationStatus.LimitReached);
        return new(InviteOperationStatus.Success, invite);
    }

    public async Task<InviteOperationResult<ServerMemberResponse>> AcceptAsync(
        string code, Guid userId, CancellationToken cancellationToken)
    {
        var invite = await dbContext.ServerInvites.Include(item => item.Server)
            .SingleOrDefaultAsync(item => item.Code == code, cancellationToken);
        if (invite is null) return new(InviteOperationStatus.NotFound);
        if (invite.ExpiresAt <= DateTimeOffset.UtcNow) return new(InviteOperationStatus.Expired);
        if (invite.MaxUses.HasValue && invite.Uses >= invite.MaxUses.Value)
            return new(InviteOperationStatus.LimitReached);
        if (await dbContext.ServerMembers.AnyAsync(member =>
                member.ServerId == invite.ServerId && member.UserId == userId, cancellationToken))
            return new(InviteOperationStatus.AlreadyMember);

        var member = new ServerMember
        {
            Id = Guid.NewGuid(), ServerId = invite.ServerId, UserId = userId,
            Role = ServerRole.MEMBER, JoinedAt = DateTimeOffset.UtcNow
        };
        invite.Uses++;
        dbContext.ServerMembers.Add(member);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return await AcceptAsync(code, userId, cancellationToken);
        }

        var loaded = await dbContext.ServerMembers.AsNoTracking().Include(item => item.User)
            .SingleAsync(item => item.Id == member.Id, cancellationToken);
        await notificationService.CreateMemberJoinedNotificationsAsync(
            invite.ServerId, userId, invite.CreatedByUserId, cancellationToken);
        return new(InviteOperationStatus.Success, new ServerMemberResponse(
            loaded.Id, loaded.UserId, loaded.User.Username, loaded.User.Avatar,
            loaded.Role.ToString(), loaded.JoinedAt));
    }

    public async Task<InviteOperationStatus> DeleteAsync(
        string code, Guid userId, CancellationToken cancellationToken)
    {
        var invite = await dbContext.ServerInvites.Include(item => item.Server)
            .SingleOrDefaultAsync(item => item.Code == code, cancellationToken);
        if (invite is null) return InviteOperationStatus.NotFound;
        if (!await authorizationService.HasPermissionAsync(
                invite.ServerId, userId, ServerPermission.ManageInvites, cancellationToken))
            return InviteOperationStatus.Forbidden;
        dbContext.ServerInvites.Remove(invite);
        await dbContext.SaveChangesAsync(cancellationToken);
        return InviteOperationStatus.Success;
    }

    private async Task<ServerInviteResponse?> LoadResponseAsync(string code, CancellationToken cancellationToken)
    {
        var invite = await dbContext.ServerInvites.AsNoTracking().Include(item => item.Server)
            .SingleOrDefaultAsync(item => item.Code == code, cancellationToken);
        return invite is null ? null : new ServerInviteResponse(
            invite.Id, invite.ServerId, invite.Server.Name, invite.Server.Icon, invite.Code,
            invite.CreatedByUserId, invite.ExpiresAt, invite.MaxUses, invite.Uses, invite.CreatedAt);
    }

    private static string CreateSecureCode() =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(24));

    private async Task<string> CreateUniqueCodeAsync(CancellationToken cancellationToken)
    {
        string code;
        do code = CreateSecureCode();
        while (await dbContext.ServerInvites.AnyAsync(invite => invite.Code == code, cancellationToken));
        return code;
    }
}
