using System.ComponentModel.DataAnnotations;

namespace Concord.Api.DTOs.Auth;

public sealed class RegisterRequest
{
    [Required, StringLength(32, MinimumLength = 3)]
    public string Username { get; init; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; init; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 8)]
    public string Password { get; init; } = string.Empty;

    [Url, StringLength(2048)]
    public string? Avatar { get; init; }
}
