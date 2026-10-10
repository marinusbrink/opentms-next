using System;
using System.ComponentModel.DataAnnotations;

namespace OpenTms.Platform.Administration;

public record AdministrationOrganizationUnitCreateDto
{
    /// <summary>ABP's OrganizationUnitConsts.MaxDisplayNameLength (128).</summary>
    [Required]
    [MaxLength(128)]
    public string DisplayName { get; init; } = default!;

    /// <summary>The parent unit; null creates a root unit.</summary>
    public Guid? ParentId { get; init; }
}
