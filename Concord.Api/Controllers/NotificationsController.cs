using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Concord.Api.DTOs.Notifications;
using Concord.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Concord.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/notifications")]
public sealed class NotificationsController(INotificationService notificationService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedNotificationsResponse>> List(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 30,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize is < 1 or > 100) return BadRequest();
        return Ok(await notificationService.ListAsync(GetUserId(), page, pageSize, cancellationToken));
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<UnreadNotificationsResponse>> UnreadCount(CancellationToken cancellationToken) =>
        Ok(new UnreadNotificationsResponse(
            await notificationService.GetUnreadCountAsync(GetUserId(), cancellationToken)));

    [HttpPost("{id:guid}/read")]
    public async Task<ActionResult<NotificationResponse>> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        var notification = await notificationService.MarkReadAsync(id, GetUserId(), cancellationToken);
        return notification is null ? NotFound() : Ok(notification);
    }

    [HttpPost("read-all")]
    public async Task<ActionResult<UnreadNotificationsResponse>> MarkAllRead(CancellationToken cancellationToken)
    {
        await notificationService.MarkAllReadAsync(GetUserId(), cancellationToken);
        return Ok(new UnreadNotificationsResponse(0));
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}
