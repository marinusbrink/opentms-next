using System;
using System.ComponentModel.DataAnnotations;

namespace OpenTms.Platform.Tenants;

/// <summary>One row of the Tenants list. Nothing here carries a credential.</summary>
public record TenantRowDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = default!;
    public Guid? EditionId { get; init; }
    public string? EditionName { get; init; }

    /// <summary>Null for a tenant on the shared tier (no database of its own).</summary>
    public string? DatabaseName { get; init; }

    public string? ServerName { get; init; }

    /// <summary>One of <see cref="TenantProvisioningState"/>; null for a tenant created before this app.</summary>
    public string? ProvisioningState { get; init; }

    public DateTime CreationTime { get; init; }
}

public record TenantDto : TenantRowDto
{
    /// <summary>The tenant's connection string with the password masked.</summary>
    public string? ConnectionString { get; init; }

    public string? ProvisioningError { get; init; }

    public DateTime? InvitationSentAt { get; init; }
}

public class TenantCreateDto
{
    [Required]
    [StringLength(TenantAdministrationConsts.MaxNameLength)]
    public string Name { get; set; } = default!;

    [Required]
    [EmailAddress]
    [StringLength(TenantAdministrationConsts.MaxEmailAddressLength)]
    public string AdminEmailAddress { get; set; } = default!;

    public Guid? EditionId { get; set; }
}

public class TenantUpdateDto
{
    [Required]
    [StringLength(TenantAdministrationConsts.MaxNameLength)]
    public string Name { get; set; } = default!;

    public Guid? EditionId { get; set; }
}

/// <summary>What a new tenant's connection string will look like; the id part of the name is shown as x's.</summary>
public record TenantConnectionPreviewDto
{
    public string ConnectionString { get; init; } = default!;
    public string DatabaseName { get; init; } = default!;
    public string ServerName { get; init; } = default!;
}

public static class TenantAdministrationConsts
{
    /// <summary>ABP's <c>TenantConsts.MaxNameLength</c>.</summary>
    public const int MaxNameLength = 64;

    /// <summary>ABP's <c>IdentityUserConsts.MaxEmailLength</c>.</summary>
    public const int MaxEmailAddressLength = 256;
}
