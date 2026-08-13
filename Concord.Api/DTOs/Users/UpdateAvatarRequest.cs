using System.ComponentModel.DataAnnotations;

namespace Concord.Api.DTOs.Users;

public sealed class UpdateAvatarRequest
{
    [Required, Url, StringLength(2048)]
    public string Avatar { get; init; } = string.Empty;
}
