using Concord.Api.Data;
using Concord.Api.DTOs.Messages;
using Concord.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Concord.Api.Services;

public sealed class MessageService(
    ConcordDbContext dbContext,
    IServerAuthorizationService authorizationService) : IMessageService
{
    public async Task<MessageOperationResult<MessageResponse>> CreateAsync(
        Guid channelId, Guid userId, SaveMessageRequest request, CancellationToken cancellationToken)
    {
        var channel = await dbContext.Channels.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == channelId, cancellationToken);
        if (channel is null) return new(MessageOperationStatus.NotFound);
        if (channel.Type != ChannelType.Text) return new(MessageOperationStatus.InvalidChannel);
        if (!await authorizationService.HasPermissionAsync(
                channel.ServerId, userId, ServerPermission.SendMessages, cancellationToken))
            return new(MessageOperationStatus.Forbidden);

        var now = DateTimeOffset.UtcNow;
        var message = new Message
        {
            Id = Guid.NewGuid(), ChannelId = channelId, AuthorId = userId,
            Content = request.Content.Trim(), CreatedAt = now, UpdatedAt = now
        };
        dbContext.Messages.Add(message);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(MessageOperationStatus.Success,
            await LoadResponseAsync(message.Id, cancellationToken));
    }

    public async Task<MessageOperationResult<PagedMessagesResponse>> ListAsync(
        Guid channelId, Guid userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var channel = await dbContext.Channels.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == channelId, cancellationToken);
        if (channel is null) return new(MessageOperationStatus.NotFound);
        if (!await authorizationService.HasPermissionAsync(
                channel.ServerId, userId, ServerPermission.ViewChannels, cancellationToken))
            return new(MessageOperationStatus.Forbidden);

        var query = dbContext.Messages.AsNoTracking().Where(message => message.ChannelId == channelId);
        var totalCount = await query.CountAsync(cancellationToken);
        var messages = await query.Include(message => message.Author)
            .OrderByDescending(message => message.CreatedAt).ThenByDescending(message => message.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        var response = new PagedMessagesResponse(messages.Select(ToResponse).ToArray(), page, pageSize,
            totalCount, (int)Math.Ceiling(totalCount / (double)pageSize));
        return new(MessageOperationStatus.Success, response);
    }

    public async Task<MessageOperationResult<MessageResponse>> UpdateAsync(
        Guid messageId, Guid userId, SaveMessageRequest request, CancellationToken cancellationToken)
    {
        var message = await dbContext.Messages.Include(item => item.Channel)
            .SingleOrDefaultAsync(item => item.Id == messageId, cancellationToken);
        if (message is null || message.IsDeleted) return new(MessageOperationStatus.NotFound);
        if (!await authorizationService.IsMemberAsync(message.Channel.ServerId, userId, cancellationToken))
            return new(MessageOperationStatus.Forbidden);
        if (message.AuthorId != userId && !await authorizationService.HasPermissionAsync(
                message.Channel.ServerId, userId, ServerPermission.ModerateMessages, cancellationToken))
            return new(MessageOperationStatus.Forbidden);

        message.Content = request.Content.Trim();
        message.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(MessageOperationStatus.Success,
            await LoadResponseAsync(message.Id, cancellationToken));
    }

    public async Task<MessageOperationStatus> DeleteAsync(
        Guid messageId, Guid userId, CancellationToken cancellationToken)
    {
        var message = await dbContext.Messages.Include(item => item.Channel)
            .SingleOrDefaultAsync(item => item.Id == messageId, cancellationToken);
        if (message is null || message.IsDeleted) return MessageOperationStatus.NotFound;
        if (!await authorizationService.IsMemberAsync(message.Channel.ServerId, userId, cancellationToken))
            return MessageOperationStatus.Forbidden;
        if (message.AuthorId != userId && !await authorizationService.HasPermissionAsync(
                message.Channel.ServerId, userId, ServerPermission.ModerateMessages, cancellationToken))
            return MessageOperationStatus.Forbidden;

        message.IsDeleted = true;
        message.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return MessageOperationStatus.Success;
    }

    private async Task<MessageResponse?> LoadResponseAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var message = await dbContext.Messages.AsNoTracking().Include(item => item.Author)
            .SingleOrDefaultAsync(item => item.Id == messageId, cancellationToken);
        return message is null ? null : ToResponse(message);
    }

    private static MessageResponse ToResponse(Message message) => new(
        message.Id, message.ChannelId,
        new MessageAuthorResponse(message.AuthorId, message.Author.Username, message.Author.Avatar),
        message.IsDeleted ? null : message.Content, message.CreatedAt, message.UpdatedAt, message.IsDeleted);
}
