using Concord.Api.DTOs.Messages;

namespace Concord.Api.Services;

public enum MessageOperationStatus { Success, NotFound, Forbidden, InvalidChannel }

public sealed record MessageOperationResult<T>(MessageOperationStatus Status, T? Value = default);

public interface IMessageService
{
    Task<MessageOperationResult<MessageResponse>> CreateAsync(
        Guid channelId, Guid userId, SaveMessageRequest request, CancellationToken cancellationToken);
    Task<MessageOperationResult<PagedMessagesResponse>> ListAsync(
        Guid channelId, Guid userId, int page, int pageSize, CancellationToken cancellationToken);
    Task<MessageOperationResult<MessageResponse>> UpdateAsync(
        Guid messageId, Guid userId, SaveMessageRequest request, CancellationToken cancellationToken);
    Task<MessageOperationStatus> DeleteAsync(Guid messageId, Guid userId, CancellationToken cancellationToken);
}
