using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace OpenTms.Platform.Administration;

public record AdministrationUserCreateDto
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

    [MaxLength(16)]
    public string? PhoneNumber { get; init; }

    /// <summary>Required unless <see cref="SetRandomPassword"/> is true.</summary>
    public string? Password { get; init; }

    /// <summary>Generates a password that meets the policy; the user then resets it through "forgot password" or an administrator.</summary>
    public bool SetRandomPassword { get; init; }

    public bool ShouldChangePasswordOnNextLogin { get; init; }

    /// <summary>Whether failed sign-ins lock the account for a while (ABP's lockout mechanism).</summary>
    public bool LockoutEnabled { get; init; } = true;

    public List<string> RoleNames { get; init; } = new();

    /// <summary>The organization units the user is a member of after the call (the whole set, not a delta).</summary>
    public List<Guid> OrganizationUnitIds { get; init; } = new();
}
