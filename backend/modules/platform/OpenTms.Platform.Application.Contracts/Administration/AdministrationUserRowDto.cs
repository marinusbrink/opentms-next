using System;
using System.Collections.Generic;

namespace OpenTms.Platform.Administration;

public record AdministrationUserRowDto
{
    public Guid Id { get; init; }
    public string UserName { get; init; } = default!;
    public string Email { get; init; } = default!;
    public string? Name { get; init; }
    public string? Surname { get; init; }
    public string? PhoneNumber { get; init; }
    public bool IsActive { get; init; }
    public bool ShouldChangePasswordOnNextLogin { get; init; }
    public bool LockoutEnabled { get; init; }
    /// <summary>True while a lockout (after failed sign-ins) is in force; Unlock ends it early.</summary>
    public bool IsLockedOut { get; init; }
    public bool HasProfilePicture { get; init; }
    public List<string> RoleNames { get; init; } = new();
    public List<Guid> OrganizationUnitIds { get; init; } = new();
    public DateTime CreationTime { get; init; }
}
