using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace OpenTms.Platform.Administration.Logbook;

/// <summary>The explicit filters of the functional logbook (UI-GRID-005); all optional, combined with AND.</summary>
public record FunctionalLogbookFilterDto : IValidatableObject
{
    /// <summary>Lower bound (inclusive) of the change time, UTC.</summary>
    public DateTime? From { get; init; }

    /// <summary>Upper bound (exclusive) of the change time, UTC.</summary>
    public DateTime? To { get; init; }

    /// <summary>One of <see cref="LogbookChangeTypes"/>.</summary>
    [MaxLength(16)]
    public string? ChangeType { get; init; }

    /// <summary>Case-insensitive "contains" on the entity's full type name.</summary>
    [MaxLength(256)]
    public string? EntityTypeFullName { get; init; }

    /// <summary>Exact entity id.</summary>
    [MaxLength(128)]
    public string? EntityId { get; init; }

    /// <summary>Case-insensitive "contains" on the name of the user who made the change.</summary>
    [MaxLength(256)]
    public string? UserName { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrEmpty(ChangeType) && !LogbookChangeTypes.IsValid(ChangeType))
            yield return new ValidationResult(
                $"ChangeType must be one of {string.Join(", ", LogbookChangeTypes.All)}.",
                [nameof(ChangeType)]);
    }
}
