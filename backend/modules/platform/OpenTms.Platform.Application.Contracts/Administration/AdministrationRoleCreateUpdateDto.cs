using System.ComponentModel.DataAnnotations;

namespace OpenTms.Platform.Administration;

public record AdministrationRoleCreateUpdateDto
{
    [Required]
    [MaxLength(256)]
    public string Name { get; init; } = default!;

    public bool IsDefault { get; init; }

    public bool IsPublic { get; init; }
}
