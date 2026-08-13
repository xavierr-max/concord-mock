using Concord.Api.Data;
using Concord.Api.DTOs.Messages;
using Concord.Api.Hubs;
using Concord.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Concord.Api.Configurations;
using Microsoft.Extensions.Options;

namespace Concord.Api.Services;

public sealed class MessageService(
    ConcordDbContext dbContext,
    IServerAuthorizationService authorizationService,
    IHubContext<ChatHub> hubContext,
    IFileStorageService fileStorageService,
    IOptions<FileStorageSettings> fileStorageOptions) : IMessageService
{
    public async Task<MessageOperationResult<MessageResponse>> CreateAsync(
        Guid channelId, Guid userId, SaveMessageRequest request, CancellationToken cancellationToken)
    {
        if (!IsValidContent(request.Content)) return new(MessageOperationStatus.InvalidContent);
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
        var response = await LoadResponseAsync(message.Id, cancellationToken);
        await hubContext.Clients.Group(ChatHub.GroupName(channelId))
            .SendAsync(ChatHubEvents.MessageCreated, response, cancellationToken);
        return new(MessageOperationStatus.Success, response);
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
        var messages = await query.Include(message => message.Author).Include(message => message.Attachments)
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
        if (!IsValidContent(request.Content)) return new(MessageOperationStatus.InvalidContent);
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
        var response = await LoadResponseAsync(message.Id, cancellationToken);
        await hubContext.Clients.Group(ChatHub.GroupName(message.ChannelId))
            .SendAsync(ChatHubEvents.MessageUpdated, response, cancellationToken);
        return new(MessageOperationStatus.Success, response);
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
        var response = await LoadResponseAsync(message.Id, cancellationToken);
        await hubContext.Clients.Group(ChatHub.GroupName(message.ChannelId))
            .SendAsync(ChatHubEvents.MessageDeleted, response, cancellationToken);
        return MessageOperationStatus.Success;
    }

    public async Task<MessageOperationResult<MessageAttachmentResponse>> AddAttachmentAsync(
        Guid messageId, Guid userId, IFormFile file, CancellationToken cancellationToken)
    {
        var validationStatus = ValidateFile(file);
        if (validationStatus is not null) return new(validationStatus.Value);

        var message = await dbContext.Messages.Include(item => item.Channel)
            .SingleOrDefaultAsync(item => item.Id == messageId, cancellationToken);
        if (message is null || message.IsDeleted) return new(MessageOperationStatus.NotFound);
        if (!await authorizationService.IsMemberAsync(message.Channel.ServerId, userId, cancellationToken)
            || message.AuthorId != userId)
            return new(MessageOperationStatus.Forbidden);

        var extension = Path.GetExtension(file.FileName);
        StoredFile? storedFile = null;
        var persisted = false;
        try
        {
            await using var content = file.OpenReadStream();
            storedFile = await fileStorageService.SaveAsync(content, extension, cancellationToken);
            var attachment = new MessageAttachment
            {
                Id = Guid.NewGuid(), MessageId = messageId, FileName = file.FileName,
                ContentType = file.ContentType.ToLowerInvariant(), FileSize = file.Length,
                Url = storedFile.Url, CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.MessageAttachments.Add(attachment);
            message.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            persisted = true;
            var response = ToAttachmentResponse(attachment);
            var messageResponse = await LoadResponseAsync(messageId, cancellationToken);
            await hubContext.Clients.Group(ChatHub.GroupName(message.ChannelId))
                .SendAsync(ChatHubEvents.MessageUpdated, messageResponse, cancellationToken);
            return new(MessageOperationStatus.Success, response);
        }
        catch
        {
            if (storedFile is not null && !persisted)
                await fileStorageService.DeleteAsync(storedFile.StorageKey, CancellationToken.None);
            throw;
        }
    }

    private async Task<MessageResponse?> LoadResponseAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var message = await dbContext.Messages.AsNoTracking().Include(item => item.Author)
            .Include(item => item.Attachments)
            .SingleOrDefaultAsync(item => item.Id == messageId, cancellationToken);
        return message is null ? null : ToResponse(message);
    }

    private static MessageResponse ToResponse(Message message) => new(
        message.Id, message.ChannelId,
        new MessageAuthorResponse(message.AuthorId, message.Author.Username, message.Author.Avatar),
        message.IsDeleted ? null : message.Content, message.CreatedAt, message.UpdatedAt, message.IsDeleted,
        message.IsDeleted
            ? []
            : message.Attachments.OrderBy(item => item.CreatedAt)
                .Select(ToAttachmentResponse).ToArray());

    private static MessageAttachmentResponse ToAttachmentResponse(MessageAttachment attachment) => new(
        attachment.Id, attachment.FileName, attachment.ContentType, attachment.FileSize,
        attachment.Url, attachment.CreatedAt);

    private MessageOperationStatus? ValidateFile(IFormFile file)
    {
        if (file.Length == 0) return MessageOperationStatus.EmptyFile;
        if (file.Length > fileStorageOptions.Value.MaxFileSize) return MessageOperationStatus.FileTooLarge;

        var fileName = file.FileName;
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 255
            || fileName.Contains('/') || fileName.Contains('\\')
            || fileName != Path.GetFileName(fileName) || fileName.Contains("..", StringComparison.Ordinal)
            || fileName.Any(character => char.IsControl(character) || Path.GetInvalidFileNameChars().Contains(character)))
            return MessageOperationStatus.InvalidFileName;

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension)) return MessageOperationStatus.InvalidExtension;

        var allowedTypes = fileStorageOptions.Value.AllowedContentTypes;
        if (!allowedTypes.TryGetValue(file.ContentType, out var extensions))
            return MessageOperationStatus.InvalidContentType;
        if (!extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return MessageOperationStatus.InvalidExtension;
        return null;
    }

    private static bool IsValidContent(string? content) =>
        !string.IsNullOrWhiteSpace(content) && content.Length <= 2000;
}
