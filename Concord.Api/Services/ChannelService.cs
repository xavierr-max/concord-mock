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

    private static ChannelResponse ToResponse(Channel channel) => new(
        channel.Id, channel.ServerId, channel.Name, channel.Type, channel.Position,
        channel.CreatedAt, channel.UpdatedAt);
}
