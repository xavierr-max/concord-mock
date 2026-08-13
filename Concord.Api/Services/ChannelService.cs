using Concord.Api.Data;
using Concord.Api.DTOs.Channels;
using Concord.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Concord.Api.Services;

public sealed class ChannelService(
    ConcordDbContext dbContext,
    IServerAuthorizationService authorizationService) : IChannelService
{
    public async Task<ChannelOperationResult<ChannelResponse>> CreateAsync(
        Guid serverId, Guid userId, SaveChannelRequest request, CancellationToken cancellationToken)
    {
        if (!await dbContext.Servers.AnyAsync(server => server.Id == serverId, cancellationToken))
            return new(ChannelOperationStatus.NotFound);
        if (!await authorizationService.HasPermissionAsync(
                serverId, userId, ServerPermission.ManageChannels, cancellationToken))
            return new(ChannelOperationStatus.Forbidden);

        var now = DateTimeOffset.UtcNow;
        var channel = new Channel
        {
            Id = Guid.NewGuid(), ServerId = serverId, Name = request.Name.Trim(),
            Type = request.Type, Position = request.Position, CreatedAt = now, UpdatedAt = now
        };
        dbContext.Channels.Add(channel);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(ChannelOperationStatus.Success, ToResponse(channel));
    }

    public async Task<ChannelOperationResult<IReadOnlyCollection<ChannelResponse>>> ListAsync(
        Guid serverId, Guid userId, CancellationToken cancellationToken)
    {
        if (!await dbContext.Servers.AnyAsync(server => server.Id == serverId, cancellationToken))
            return new(ChannelOperationStatus.NotFound);
        if (!await authorizationService.HasPermissionAsync(
                serverId, userId, ServerPermission.ViewChannels, cancellationToken))
            return new(ChannelOperationStatus.Forbidden);

        var channels = await dbContext.Channels.AsNoTracking()
            .Where(channel => channel.ServerId == serverId)
            .OrderBy(channel => channel.Position).ThenBy(channel => channel.CreatedAt)
            .Select(channel => new ChannelResponse(channel.Id, channel.ServerId, channel.Name,
                channel.Type, channel.Position, channel.CreatedAt, channel.UpdatedAt))
            .ToListAsync(cancellationToken);
        return new(ChannelOperationStatus.Success, channels);
    }

    public async Task<ChannelOperationResult<ChannelResponse>> UpdateAsync(
        Guid channelId, Guid userId, SaveChannelRequest request, CancellationToken cancellationToken)
    {
        var channel = await dbContext.Channels.Include(item => item.Server)
            .SingleOrDefaultAsync(item => item.Id == channelId, cancellationToken);
        if (channel is null) return new(ChannelOperationStatus.NotFound);
        if (!await authorizationService.HasPermissionAsync(
                channel.ServerId, userId, ServerPermission.ManageChannels, cancellationToken))
            return new(ChannelOperationStatus.Forbidden);

        channel.Name = request.Name.Trim();
        channel.Type = request.Type;
        channel.Position = request.Position;
        channel.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(ChannelOperationStatus.Success, ToResponse(channel));
    }

    public async Task<ChannelOperationStatus> DeleteAsync(
        Guid channelId, Guid userId, CancellationToken cancellationToken)
    {
        var channel = await dbContext.Channels.Include(item => item.Server)
            .SingleOrDefaultAsync(item => item.Id == channelId, cancellationToken);
        if (channel is null) return ChannelOperationStatus.NotFound;
        if (!await authorizationService.HasPermissionAsync(
                channel.ServerId, userId, ServerPermission.ManageChannels, cancellationToken))
            return ChannelOperationStatus.Forbidden;
        dbContext.Channels.Remove(channel);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ChannelOperationStatus.Success;
    }

    public async Task<ChannelOperationStatus> MarkAsReadAsync(
        Guid channelId, Guid userId, CancellationToken cancellationToken)
    {
        var access = await GetChannelAccessAsync(channelId, userId, cancellationToken);
        if (access != ChannelOperationStatus.Success) return access;

        var latestMessage = await dbContext.Messages.AsNoTracking()
            .Where(message => message.ChannelId == channelId)
            .OrderByDescending(message => message.CreatedAt)
            .ThenByDescending(message => message.Id)
            .Select(message => new { message.Id, message.CreatedAt })
            .FirstOrDefaultAsync(cancellationToken);

        // An empty channel has no message cursor to persist and is already fully read.
        if (latestMessage is null) return ChannelOperationStatus.Success;

        var state = await dbContext.ChannelReadStates.FindAsync(
            [channelId, userId], cancellationToken);
        if (state is null)
        {
            dbContext.ChannelReadStates.Add(new ChannelReadState
            {
                ChannelId = channelId,
                UserId = userId,
                LastReadMessageId = latestMessage.Id,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            var currentCreatedAt = await dbContext.Messages.AsNoTracking()
                .Where(message => message.Id == state.LastReadMessageId)
                .Select(message => message.CreatedAt)
                .SingleAsync(cancellationToken);
            if (currentCreatedAt <= latestMessage.CreatedAt)
            {
                state.LastReadMessageId = latestMessage.Id;
                state.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return ChannelOperationStatus.Success;
    }

    public async Task<ChannelOperationResult<UnreadCountResponse>> GetUnreadCountAsync(
        Guid channelId, Guid userId, CancellationToken cancellationToken)
    {
        var access = await GetChannelAccessAsync(channelId, userId, cancellationToken);
        if (access != ChannelOperationStatus.Success) return new(access);

        var lastReadAt = await dbContext.ChannelReadStates.AsNoTracking()
            .Where(state => state.ChannelId == channelId && state.UserId == userId)
            .Select(state => (DateTimeOffset?)state.LastReadMessage.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var count = await dbContext.Messages.AsNoTracking()
            .Where(message => message.ChannelId == channelId
                && message.AuthorId != userId
                && !message.IsDeleted
                && (lastReadAt == null || message.CreatedAt > lastReadAt))
            .LongCountAsync(cancellationToken);
        return new(ChannelOperationStatus.Success, new UnreadCountResponse(count));
    }

    public async Task<ChannelOperationResult<UnreadMentionCountResponse>> GetUnreadMentionCountAsync(
        Guid channelId, Guid userId, CancellationToken cancellationToken)
    {
        var access = await GetChannelAccessAsync(channelId, userId, cancellationToken);
        if (access != ChannelOperationStatus.Success) return new(access);

        // The endpoint is intentionally stable while mention persistence is introduced later.
        return new(ChannelOperationStatus.Success, new UnreadMentionCountResponse(0));
    }

    private async Task<ChannelOperationStatus> GetChannelAccessAsync(
        Guid channelId, Guid userId, CancellationToken cancellationToken)
    {
        var serverId = await dbContext.Channels.AsNoTracking()
            .Where(channel => channel.Id == channelId)
            .Select(channel => (Guid?)channel.ServerId)
            .SingleOrDefaultAsync(cancellationToken);
        if (serverId is null) return ChannelOperationStatus.NotFound;
        return await authorizationService.HasPermissionAsync(
            serverId.Value, userId, ServerPermission.ViewChannels, cancellationToken)
            ? ChannelOperationStatus.Success
            : ChannelOperationStatus.Forbidden;
    }

    private static ChannelResponse ToResponse(Channel channel) => new(
        channel.Id, channel.ServerId, channel.Name, channel.Type, channel.Position,
        channel.CreatedAt, channel.UpdatedAt);
}
