using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Concord.Api.DTOs.Messages;
using Concord.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Concord.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/messages")]
public sealed class MessagesController(IMessageService messageService) : ControllerBase
{
    [HttpPost("/api/channels/{channelId:guid}/messages")]
    [ProducesResponseType<MessageResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MessageResponse>> Create(
        Guid channelId, SaveMessageRequest request, CancellationToken cancellationToken)
    {
        var result = await messageService.CreateAsync(channelId, GetUserId(), request, cancellationToken);
        return result.Status switch
        {
            MessageOperationStatus.Success => Created($"/api/messages/{result.Value!.Id}", result.Value),
            MessageOperationStatus.NotFound => NotFound(),
            MessageOperationStatus.InvalidChannel => BadRequest("Mensagens só podem ser enviadas em canais de texto."),
            MessageOperationStatus.InvalidContent => BadRequest("Conteúdo da mensagem inválido."),
            _ => StatusCode(StatusCodes.Status403Forbidden)
        };
    }

    [HttpGet("/api/channels/{channelId:guid}/messages")]
    [ProducesResponseType<PagedMessagesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedMessagesResponse>> List(
        Guid channelId,
        [FromQuery] MessageHistoryQuery query,
        CancellationToken cancellationToken)
    {
        var result = await messageService.ListAsync(
            channelId, GetUserId(), query.Page, query.PageSize, cancellationToken);
        return result.Status switch
        {
            MessageOperationStatus.Success => Ok(result.Value),
            MessageOperationStatus.NotFound => NotFound(),
            _ => StatusCode(StatusCodes.Status403Forbidden)
        };
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<MessageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MessageResponse>> Update(
        Guid id, SaveMessageRequest request, CancellationToken cancellationToken)
    {
        var result = await messageService.UpdateAsync(id, GetUserId(), request, cancellationToken);
        return result.Status switch
        {
            MessageOperationStatus.Success => Ok(result.Value),
            MessageOperationStatus.NotFound => NotFound(),
            MessageOperationStatus.InvalidContent => BadRequest("Conteúdo da mensagem inválido."),
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
        var status = await messageService.DeleteAsync(id, GetUserId(), cancellationToken);
        return status switch
        {
            MessageOperationStatus.Success => NoContent(),
            MessageOperationStatus.NotFound => NotFound(),
            _ => StatusCode(StatusCodes.Status403Forbidden)
        };
    }

    [HttpPost("{id:guid}/attachments")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<MessageAttachmentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MessageAttachmentResponse>> AddAttachment(
        Guid id, [FromForm] IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null) return BadRequest("Envie um arquivo no campo 'file'.");
        var result = await messageService.AddAttachmentAsync(id, GetUserId(), file, cancellationToken);
        return result.Status switch
        {
            MessageOperationStatus.Success => Created($"/api/messages/{id}/attachments/{result.Value!.Id}", result.Value),
            MessageOperationStatus.NotFound => NotFound(),
            MessageOperationStatus.Forbidden => StatusCode(StatusCodes.Status403Forbidden),
            MessageOperationStatus.FileTooLarge => BadRequest("O arquivo excede o tamanho máximo permitido."),
            MessageOperationStatus.InvalidContentType => BadRequest("Content-Type não permitido."),
            MessageOperationStatus.InvalidExtension => BadRequest("Extensão incompatível ou não permitida."),
            MessageOperationStatus.InvalidFileName => BadRequest("Nome de arquivo inválido."),
            _ => BadRequest("O arquivo está vazio.")
        };
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}
