using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace OpenTms.Platform.Administration;

public record AdministrationUserUpdateDto
{
    [Required]
    [MaxLength(256)]
    public string UserName { get; init; } = default!;

    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string Email { get; init; } = default!;

    [MaxLength(64)]
    public string? Name { get; init; }

    [MaxLength(64)]
    public string? Surname { get; init; }

    public bool IsActive { get; init; }

    public List<string> RoleNames { get; init; } = new();
}
