using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Concord.Api.Data;
using Concord.Api.DTOs.Voice;
using Concord.Api.Models;
using Concord.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Concord.Api.Hubs;

[Authorize]
public sealed class VoiceHub(
    ConcordDbContext dbContext,
    IServerAuthorizationService authorizationService,
    IVoiceSessionService voiceSessionService,
    ILogger<VoiceHub> logger) : Hub
{
    public async Task<VoiceParticipantResponse> JoinVoiceChannel(Guid channelId)
    {
        await ValidateVoiceChannelAsync(channelId);
        var result = voiceSessionService.Join(channelId, GetUserId(), Context.ConnectionId);

        if (result.PreviousParticipant is not null)
        {
            if (result.UserLeftPreviousChannel)
                await Clients.Group(GroupName(result.PreviousParticipant.ChannelId)).SendAsync(
                    VoiceHubEvents.VoiceUserLeft, result.PreviousParticipant, Context.ConnectionAborted);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId,
                GroupName(result.PreviousParticipant.ChannelId), Context.ConnectionAborted);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(channelId), Context.ConnectionAborted);
        foreach (var participant in voiceSessionService.GetParticipants(channelId)
                     .Where(participant => participant.UserId != result.Participant.UserId))
            await Clients.Caller.SendAsync(
                VoiceHubEvents.VoiceUserUpdated, participant, Context.ConnectionAborted);
        if (result.UserJoined)
            await Clients.Group(GroupName(channelId)).SendAsync(
                VoiceHubEvents.VoiceUserJoined, result.Participant, Context.ConnectionAborted);
        return result.Participant;
    }

    public async Task LeaveVoiceChannel()
    {
        var result = voiceSessionService.Leave(Context.ConnectionId);
        if (result is null) return;
        if (result.UserLeft)
            await Clients.Group(GroupName(result.Participant.ChannelId)).SendAsync(
                VoiceHubEvents.VoiceUserLeft, result.Participant, Context.ConnectionAborted);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId,
            GroupName(result.Participant.ChannelId), Context.ConnectionAborted);
    }

    public Task SetMute(bool muted) => UpdateParticipantAsync(
        () => voiceSessionService.SetMute(Context.ConnectionId, muted));

    public Task SetDeafened(bool deafened) => UpdateParticipantAsync(
        () => voiceSessionService.SetDeafened(Context.ConnectionId, deafened));

    public Task SendOffer(Guid targetUserId, string sdp) => SendSignalAsync(
        targetUserId, sdp, VoiceHubEvents.VoiceOfferReceived,
        (senderUserId, channelId, payload) => new VoiceOfferResponse(senderUserId, channelId, payload));

    public Task SendAnswer(Guid targetUserId, string sdp) => SendSignalAsync(
        targetUserId, sdp, VoiceHubEvents.VoiceAnswerReceived,
        (senderUserId, channelId, payload) => new VoiceAnswerResponse(senderUserId, channelId, payload));

    public Task SendIceCandidate(Guid targetUserId, string candidate) => SendSignalAsync(
        targetUserId, candidate, VoiceHubEvents.VoiceIceCandidateReceived,
        (senderUserId, channelId, payload) =>
            new VoiceIceCandidateResponse(senderUserId, channelId, payload));

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var result = voiceSessionService.Leave(Context.ConnectionId);
        if (result?.UserLeft == true)
            await Clients.Group(GroupName(result.Participant.ChannelId)).SendAsync(
                VoiceHubEvents.VoiceUserLeft, result.Participant, CancellationToken.None);
        await base.OnDisconnectedAsync(exception);
    }

    internal static string GroupName(Guid channelId) => $"voice:{channelId:N}";

    private async Task UpdateParticipantAsync(Func<VoiceParticipantResponse?> update)
    {
        var channelId = voiceSessionService.GetChannelId(Context.ConnectionId)
            ?? throw new HubException("Entre em um canal de voz antes de atualizar seu estado.");
        await ValidateVoiceChannelAsync(channelId);
        var participant = update()
            ?? throw new HubException("Participação de voz não encontrada.");
        await Clients.Group(GroupName(channelId)).SendAsync(
            VoiceHubEvents.VoiceUserUpdated, participant, Context.ConnectionAborted);
    }

    private async Task SendSignalAsync<TSignal>(
        Guid targetUserId,
        string payload,
        string eventName,
        Func<Guid, Guid, string, TSignal> createSignal)
    {
        if (string.IsNullOrWhiteSpace(payload))
            throw new HubException("O payload de signaling é obrigatório.");

        var senderUserId = GetUserId();
        var route = voiceSessionService.GetSignalRoute(
            Context.ConnectionId, senderUserId, targetUserId);
        if (route is null)
        {
            logger.LogDebug(
                "Voice signaling {SignalType} rejected from user {SenderUserId} to user {TargetUserId}",
                eventName, senderUserId, targetUserId);
            throw new HubException("Remetente e destinatário devem estar na mesma sala de voz.");
        }

        logger.LogDebug(
            "Forwarding voice signaling {SignalType} in channel {ChannelId} from user {SenderUserId} to user {TargetUserId}",
            eventName, route.ChannelId, senderUserId, targetUserId);
        await Clients.Clients(route.TargetConnectionIds).SendAsync(
            eventName, createSignal(senderUserId, route.ChannelId, payload), Context.ConnectionAborted);
    }

    private async Task ValidateVoiceChannelAsync(Guid channelId)
    {
        var channel = await dbContext.Channels.AsNoTracking()
            .Where(item => item.Id == channelId)
            .Select(item => new { item.ServerId, item.Type })
            .SingleOrDefaultAsync(Context.ConnectionAborted)
            ?? throw new HubException("Canal não encontrado.");
        if (channel.Type != ChannelType.Voice)
            throw new HubException("O canal informado não é um canal de voz.");
        if (!await authorizationService.HasPermissionAsync(
                channel.ServerId, GetUserId(), ServerPermission.JoinVoiceChannels, Context.ConnectionAborted))
            throw new HubException("Você não possui acesso a este canal de voz.");
    }

    private Guid GetUserId() => Guid.Parse(
        Context.User?.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? throw new HubException("Usuário não autenticado."));
}
