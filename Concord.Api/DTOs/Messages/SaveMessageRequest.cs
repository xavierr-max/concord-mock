using System.ComponentModel.DataAnnotations;

namespace Concord.Api.DTOs.Messages;

public sealed class SaveMessageRequest : IValidatableObject
{
    [Required, StringLength(2000, MinimumLength = 1)]
    public string Content { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Content))
            yield return new ValidationResult("A mensagem não pode ser vazia.", [nameof(Content)]);
    }
}
