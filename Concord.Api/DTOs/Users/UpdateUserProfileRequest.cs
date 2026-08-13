using System.ComponentModel.DataAnnotations;

namespace Concord.Api.DTOs.Users;

public sealed class UpdateUserProfileRequest
{
    [Required, StringLength(32, MinimumLength = 3)]
    public string Username { get; init; } = string.Empty;

    [StringLength(100)]
    public string? DisplayName { get; init; }

    [StringLength(500)]
    public string? Bio { get; init; }
}
