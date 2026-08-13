using System.ComponentModel.DataAnnotations;

namespace Concord.Api.DTOs.Invites;

public sealed class CreateServerInviteRequest : IValidatableObject
{
    public DateTimeOffset ExpiresAt { get; init; }

    [Range(1, int.MaxValue)]
    public int? MaxUses { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ExpiresAt <= DateTimeOffset.UtcNow)
            yield return new ValidationResult("ExpiresAt deve ser uma data futura.", [nameof(ExpiresAt)]);
    }
}
