using Concord.Api.DTOs.Channels;

namespace Concord.Api.Services;

public enum ChannelOperationStatus { Success, NotFound, Forbidden }

public sealed record ChannelOperationResult<T>(ChannelOperationStatus Status, T? Value = default);

public interface IChannelService
{
    Task<ChannelOperationResult<ChannelResponse>> CreateAsync(
        Guid serverId, Guid userId, SaveChannelRequest request, CancellationToken cancellationToken);
    Task<ChannelOperationResult<IReadOnlyCollection<ChannelResponse>>> ListAsync(
        Guid serverId, Guid userId, CancellationToken cancellationToken);
    Task<ChannelOperationResult<ChannelResponse>> UpdateAsync(
        Guid channelId, Guid userId, SaveChannelRequest request, CancellationToken cancellationToken);
    Task<ChannelOperationStatus> DeleteAsync(Guid channelId, Guid userId, CancellationToken cancellationToken);
    Task<ChannelOperationStatus> MarkAsReadAsync(
        Guid channelId, Guid userId, CancellationToken cancellationToken);
    Task<ChannelOperationResult<UnreadCountResponse>> GetUnreadCountAsync(
        Guid channelId, Guid userId, CancellationToken cancellationToken);
    Task<ChannelOperationResult<UnreadMentionCountResponse>> GetUnreadMentionCountAsync(
        Guid channelId, Guid userId, CancellationToken cancellationToken);
}
