using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Concord.Api.DTOs.Invites;
using Concord.Api.DTOs.Servers;
using Concord.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Concord.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/invites")]
public sealed class InvitesController(IServerInviteService inviteService) : ControllerBase
{
    [HttpPost("/api/servers/{serverId:guid}/invites")]
    [ProducesResponseType<ServerInviteResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServerInviteResponse>> Create(
        Guid serverId, CreateServerInviteRequest request, CancellationToken cancellationToken)
    {
        var result = await inviteService.CreateAsync(serverId, GetUserId(), request, cancellationToken);
        return result.Status switch
        {
            InviteOperationStatus.Success => CreatedAtAction(nameof(Get), new { code = result.Value!.Code }, result.Value),
            InviteOperationStatus.NotFound => NotFound(),
            _ => StatusCode(StatusCodes.Status403Forbidden)
        };
    }

    [HttpGet("{code}")]
    [ProducesResponseType<ServerInviteResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public async Task<ActionResult<ServerInviteResponse>> Get(string code, CancellationToken cancellationToken) =>
        ToInviteActionResult(await inviteService.GetAsync(code, cancellationToken));

    [HttpPost("{code}/accept")]
    [ProducesResponseType<ServerMemberResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public async Task<ActionResult<ServerMemberResponse>> Accept(string code, CancellationToken cancellationToken)
    {
        var result = await inviteService.AcceptAsync(code, GetUserId(), cancellationToken);
        return result.Status switch
        {
            InviteOperationStatus.Success => Ok(result.Value),
            InviteOperationStatus.NotFound => NotFound(),
            InviteOperationStatus.Expired => StatusCode(StatusCodes.Status410Gone),
            InviteOperationStatus.LimitReached or InviteOperationStatus.AlreadyMember => Conflict(),
            _ => StatusCode(StatusCodes.Status403Forbidden)
        };
    }

    [HttpDelete("{code}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string code, CancellationToken cancellationToken)
    {
        var status = await inviteService.DeleteAsync(code, GetUserId(), cancellationToken);
        return status switch
        {
            InviteOperationStatus.Success => NoContent(),
            InviteOperationStatus.NotFound => NotFound(),
            _ => StatusCode(StatusCodes.Status403Forbidden)
        };
    }

    private ActionResult<ServerInviteResponse> ToInviteActionResult(
        InviteOperationResult<ServerInviteResponse> result) => result.Status switch
    {
        InviteOperationStatus.Success => Ok(result.Value),
        InviteOperationStatus.NotFound => NotFound(),
        InviteOperationStatus.Expired => StatusCode(StatusCodes.Status410Gone),
        InviteOperationStatus.LimitReached => Conflict(),
        _ => StatusCode(StatusCodes.Status403Forbidden)
    };

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}
