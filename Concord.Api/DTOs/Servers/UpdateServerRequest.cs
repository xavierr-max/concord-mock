using System.ComponentModel.DataAnnotations;

namespace Concord.Api.DTOs.Servers;

public sealed class UpdateServerRequest
{
    [Required, StringLength(100, MinimumLength = 2)]
    public string Name { get; init; } = string.Empty;

    [Url, StringLength(2048)]
    public string? Icon { get; init; }
}
