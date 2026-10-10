using System;
using System.ComponentModel.DataAnnotations;

namespace OpenTms.Platform.Administration;

public record AdministrationOrganizationUnitUpdateDto
{
    /// <summary>ABP's OrganizationUnitConsts.MaxDisplayNameLength (128).</summary>
    [Required]
    [MaxLength(128)]
    public string DisplayName { get; init; } = default!;

    /// <summary>The parent after the update; null makes it a root unit. A change moves the unit with its whole subtree.</summary>
    public Guid? ParentId { get; init; }
}
