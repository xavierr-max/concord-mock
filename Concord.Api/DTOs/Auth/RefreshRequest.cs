using System.ComponentModel.DataAnnotations;

namespace Concord.Api.DTOs.Auth;

public sealed class RefreshRequest
{
    [Required]
    public string RefreshToken { get; init; } = string.Empty;
}
