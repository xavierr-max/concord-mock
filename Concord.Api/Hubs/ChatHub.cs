using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Concord.Api.Data;
using Concord.Api.DTOs.Messages;
using Concord.Api.DTOs.Presence;
using Concord.Api.Models;
using Concord.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Concord.Api.Hubs;

[Authorize]
public sealed class ChatHub(
    ConcordDbContext dbContext,
    IServerAuthorizationService authorizationService,
    IMessageService messageService,
    IPresenceService presenceService) : Hub
{
    private const string CurrentUserKey = "CurrentPresenceUser";
    private const string JoinedChannelsKey = "JoinedChannels";
    private const string TypingChannelsKey = "TypingChannels";

    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();
        var user = await dbContext.Users.AsNoTracking()
            .Where(item => item.Id == userId)
            .Select(item => new PresenceUser(item.Id, item.Username, item.Avatar))
            .SingleAsync(Context.ConnectionAborted);
        var serverIds = await dbContext.ServerMembers.AsNoTracking()
            .Where(member => member.UserId == userId)
            .Select(member => member.ServerId)
            .ToArrayAsync(Context.ConnectionAborted);
        Context.Items[CurrentUserKey] = user;
        Context.Items[JoinedChannelsKey] = new HashSet<Guid>();
        Context.Items[TypingChannelsKey] = new HashSet<Guid>();
        foreach (var serverId in serverIds)
            await Groups.AddToGroupAsync(
                Context.ConnectionId, ServerGroupName(serverId), Context.ConnectionAborted);
        await presenceService.ConnectedAsync(
            user, Context.ConnectionId, serverIds, Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        foreach (var channelId in TypingChannels.ToArray())
            await PublishTypingAsync(channelId, ChatHubEvents.TypingStopped, CancellationToken.None);
        await presenceService.DisconnectedAsync(GetUserId(), Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinChannel(Guid channelId)
    {
        var serverId = await GetServerIdAsync(channelId);
        if (!await authorizationService.HasPermissionAsync(
                serverId, GetUserId(), ServerPermission.ViewChannels, Context.ConnectionAborted))
            throw new HubException("Você não possui acesso a este canal.");

        await Groups.AddToGroupAsync(
            Context.ConnectionId, GroupName(channelId), Context.ConnectionAborted);
        await Groups.AddToGroupAsync(
            Context.ConnectionId, ServerGroupName(serverId), Context.ConnectionAborted);
        presenceService.TrackServer(GetUserId(), serverId);
        JoinedChannels.Add(channelId);
    }

    public async Task LeaveChannel(Guid channelId)
    {
        if (TypingChannels.Remove(channelId))
            await PublishTypingAsync(channelId, ChatHubEvents.TypingStopped, Context.ConnectionAborted);
        JoinedChannels.Remove(channelId);
        await Groups.RemoveFromGroupAsync(
            Context.ConnectionId, GroupName(channelId), Context.ConnectionAborted);
    }

    public async Task StartTyping(Guid channelId)
    {
        await ValidateCanTypeAsync(channelId);
        if (TypingChannels.Add(channelId))
            await PublishTypingAsync(channelId, ChatHubEvents.TypingStarted, Context.ConnectionAborted);
    }

    public async Task StopTyping(Guid channelId)
    {
        if (!JoinedChannels.Contains(channelId))
            throw new HubException("Entre no canal antes de atualizar o indicador de digitação.");
        if (TypingChannels.Remove(channelId))
            await PublishTypingAsync(channelId, ChatHubEvents.TypingStopped, Context.ConnectionAborted);
    }

    public async Task<MessageResponse> SendMessage(Guid channelId, string content)
    {
        var result = await messageService.CreateAsync(channelId, GetUserId(),
            new SaveMessageRequest { Content = content }, Context.ConnectionAborted);
        return result.Status switch
        {
            MessageOperationStatus.Success => result.Value!,
            MessageOperationStatus.NotFound => throw new HubException("Canal não encontrado."),
            MessageOperationStatus.InvalidChannel => throw new HubException(
                "Mensagens só podem ser enviadas em canais de texto."),
            MessageOperationStatus.InvalidContent => throw new HubException("Conteúdo da mensagem inválido."),
            _ => throw new HubException("Você não possui permissão para enviar mensagens neste canal.")
        };
    }

    internal static string GroupName(Guid channelId) => $"channel:{channelId:N}";
    internal static string ServerGroupName(Guid serverId) => $"server:{serverId:N}";

    private HashSet<Guid> JoinedChannels =>
        (HashSet<Guid>)Context.Items[JoinedChannelsKey]!;

    private HashSet<Guid> TypingChannels =>
        (HashSet<Guid>)Context.Items[TypingChannelsKey]!;

    private PresenceUser CurrentUser => (PresenceUser)Context.Items[CurrentUserKey]!;

    private async Task ValidateCanTypeAsync(Guid channelId)
    {
        if (!JoinedChannels.Contains(channelId))
            throw new HubException("Entre no canal antes de iniciar o indicador de digitação.");
        var channel = await dbContext.Channels.AsNoTracking()
            .Where(item => item.Id == channelId)
            .Select(item => new { item.ServerId, item.Type })
            .SingleOrDefaultAsync(Context.ConnectionAborted)
            ?? throw new HubException("Canal não encontrado.");
        if (channel.Type != ChannelType.Text || !await authorizationService.HasPermissionAsync(
                channel.ServerId, GetUserId(), ServerPermission.SendMessages, Context.ConnectionAborted))
            throw new HubException("Você não possui permissão para digitar neste canal.");
    }

    private Task PublishTypingAsync(Guid channelId, string eventName, CancellationToken cancellationToken)
    {
        var update = new TypingIndicatorResponse(
            channelId, CurrentUser.Id, CurrentUser.Username, CurrentUser.Avatar, DateTimeOffset.UtcNow);
        return Clients.OthersInGroup(GroupName(channelId)).SendAsync(eventName, update, cancellationToken);
    }

    private async Task<Guid> GetServerIdAsync(Guid channelId) =>
        await dbContext.Channels.AsNoTracking()
            .Where(channel => channel.Id == channelId)
            .Select(channel => (Guid?)channel.ServerId)
            .SingleOrDefaultAsync(Context.ConnectionAborted)
        ?? throw new HubException("Canal não encontrado.");

    private Guid GetUserId() => Guid.Parse(
        Context.User?.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? throw new HubException("Usuário não autenticado."));
}
