using System.ComponentModel.DataAnnotations;
using Concord.Api.Models;

namespace Concord.Api.DTOs.Channels;

public sealed class SaveChannelRequest : IValidatableObject
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [EnumDataType(typeof(ChannelType))]
    public ChannelType Type { get; init; }

    [Range(0, 10_000)]
    public int Position { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Name))
            yield return new ValidationResult("O nome do canal não pode ser vazio.", [nameof(Name)]);
        if (!Enum.IsDefined(Type))
            yield return new ValidationResult("O tipo do canal é inválido.", [nameof(Type)]);
    }
}
