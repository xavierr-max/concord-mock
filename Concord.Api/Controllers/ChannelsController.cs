using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Concord.Api.DTOs.Channels;
using Concord.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Concord.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/channels")]
public sealed class ChannelsController(IChannelService channelService) : ControllerBase
{
    [HttpPost("/api/servers/{serverId:guid}/channels")]
    [ProducesResponseType<ChannelResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ChannelResponse>> Create(
        Guid serverId, SaveChannelRequest request, CancellationToken cancellationToken)
    {
        var result = await channelService.CreateAsync(serverId, GetUserId(), request, cancellationToken);
        return result.Status switch
        {
            ChannelOperationStatus.Success => Created($"/api/channels/{result.Value!.Id}", result.Value),
            ChannelOperationStatus.NotFound => NotFound(),
            _ => StatusCode(StatusCodes.Status403Forbidden)
        };
    }

    [HttpGet("/api/servers/{serverId:guid}/channels")]
    [ProducesResponseType<IReadOnlyCollection<ChannelResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyCollection<ChannelResponse>>> List(
        Guid serverId, CancellationToken cancellationToken)
    {
        var result = await channelService.ListAsync(serverId, GetUserId(), cancellationToken);
        return result.Status switch
        {
            ChannelOperationStatus.Success => Ok(result.Value),
            ChannelOperationStatus.NotFound => NotFound(),
            _ => StatusCode(StatusCodes.Status403Forbidden)
        };
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<ChannelResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ChannelResponse>> Update(
        Guid id, SaveChannelRequest request, CancellationToken cancellationToken)
    {
        var result = await channelService.UpdateAsync(id, GetUserId(), request, cancellationToken);
        return result.Status switch
        {
            ChannelOperationStatus.Success => Ok(result.Value),
            ChannelOperationStatus.NotFound => NotFound(),
            _ => StatusCode(StatusCodes.Status403Forbidden)
        };
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var status = await channelService.DeleteAsync(id, GetUserId(), cancellationToken);
        return status switch
        {
            ChannelOperationStatus.Success => NoContent(),
            ChannelOperationStatus.NotFound => NotFound(),
            _ => StatusCode(StatusCodes.Status403Forbidden)
        };
    }

    [HttpPost("{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAsRead(Guid id, CancellationToken cancellationToken)
    {
        var status = await channelService.MarkAsReadAsync(id, GetUserId(), cancellationToken);
        return ToActionResult(status);
    }

    [HttpGet("{id:guid}/unread-count")]
    [ProducesResponseType<UnreadCountResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UnreadCountResponse>> GetUnreadCount(
        Guid id, CancellationToken cancellationToken)
    {
        var result = await channelService.GetUnreadCountAsync(id, GetUserId(), cancellationToken);
        return result.Status switch
        {
            ChannelOperationStatus.Success => Ok(result.Value),
            ChannelOperationStatus.NotFound => NotFound(),
            _ => StatusCode(StatusCodes.Status403Forbidden)
        };
    }

    [HttpGet("{id:guid}/unread-mention-count")]
    [ProducesResponseType<UnreadMentionCountResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UnreadMentionCountResponse>> GetUnreadMentionCount(
        Guid id, CancellationToken cancellationToken)
    {
        var result = await channelService.GetUnreadMentionCountAsync(id, GetUserId(), cancellationToken);
        return result.Status switch
        {
            ChannelOperationStatus.Success => Ok(result.Value),
            ChannelOperationStatus.NotFound => NotFound(),
            _ => StatusCode(StatusCodes.Status403Forbidden)
        };
    }

    private IActionResult ToActionResult(ChannelOperationStatus status) => status switch
    {
        ChannelOperationStatus.Success => NoContent(),
        ChannelOperationStatus.NotFound => NotFound(),
        _ => StatusCode(StatusCodes.Status403Forbidden)
    };

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}
