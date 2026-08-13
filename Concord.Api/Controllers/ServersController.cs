using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Concord.Api.DTOs.Servers;
using Concord.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Concord.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/servers")]
public sealed class ServersController(IServerService serverService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<ServerResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ServerResponse>> Create(
        CreateServerRequest request, CancellationToken cancellationToken)
    {
        var server = await serverService.CreateAsync(GetUserId(), request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = server.Id }, server);
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyCollection<ServerResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyCollection<ServerResponse>>> List(CancellationToken cancellationToken) =>
        Ok(await serverService.ListAsync(GetUserId(), cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ServerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServerResponse>> GetById(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await serverService.GetAsync(id, GetUserId(), cancellationToken));

    [HttpPut("{id:guid}")]
    [ProducesResponseType<ServerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServerResponse>> Update(
        Guid id, UpdateServerRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await serverService.UpdateAsync(id, GetUserId(), request, cancellationToken));

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await serverService.DeleteAsync(id, GetUserId(), cancellationToken));

    [HttpPost("{id:guid}/members")]
    [ProducesResponseType<ServerMemberResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ServerMemberResponse>> Join(Guid id, CancellationToken cancellationToken)
    {
        var result = await serverService.JoinAsync(id, GetUserId(), cancellationToken);
        return result.Status switch
        {
            ServerOperationStatus.Success => StatusCode(StatusCodes.Status201Created, result.Value),
            ServerOperationStatus.NotFound => NotFound(),
            ServerOperationStatus.Conflict => Conflict(),
            _ => StatusCode(StatusCodes.Status403Forbidden)
        };
    }

    [HttpDelete("{id:guid}/members/{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveMember(
        Guid id, Guid userId, CancellationToken cancellationToken) =>
        ToActionResult(await serverService.RemoveMemberAsync(id, GetUserId(), userId, cancellationToken));

    private ActionResult<ServerResponse> ToActionResult(ServerOperationResult<ServerResponse> result) =>
        result.Status switch
        {
            ServerOperationStatus.Success => Ok(result.Value),
            ServerOperationStatus.NotFound => NotFound(),
            _ => StatusCode(StatusCodes.Status403Forbidden)
        };

    private IActionResult ToActionResult(ServerOperationStatus status) => status switch
    {
        ServerOperationStatus.Success => NoContent(),
        ServerOperationStatus.NotFound => NotFound(),
        ServerOperationStatus.Conflict => Conflict(),
        _ => StatusCode(StatusCodes.Status403Forbidden)
    };

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}
