using System;
using System.ComponentModel.DataAnnotations;

namespace OpenTms.Platform.Editions;

public record EditionRowDto
{
    public Guid Id { get; init; }
    public string DisplayName { get; init; } = default!;
    public int TenantCount { get; init; }
    public DateTime CreationTime { get; init; }
}

public class EditionCreateUpdateDto
{
    [Required]
    [StringLength(EditionConsts.MaxDisplayNameLength)]
    public string DisplayName { get; set; } = default!;
}

public record EditionLookupDto
{
    public Guid Id { get; init; }
    public string DisplayName { get; init; } = default!;
}

/// <summary>The 409 body when an edition still has tenants.</summary>
public record EditionDeleteCheckDto
{
    public string DisplayName { get; init; } = default!;
    public int TenantCount { get; init; }
}
