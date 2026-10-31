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

    /// <summary>The test dataset of the last "Reset to dataset" (<c>TestDatasets</c>); null when never reset.</summary>
    public string? DatasetName { get; init; }

    /// <summary>One of <c>TestDatasetResetState</c>; null when never reset.</summary>
    public string? DatasetResetState { get; init; }

    public DateTime? DatasetResetAt { get; init; }
}

public record TenantDto : TenantRowDto
{
    /// <summary>The tenant's connection string with the password masked.</summary>
    public string? ConnectionString { get; init; }

    public string? ProvisioningError { get; init; }

    public DateTime? InvitationSentAt { get; init; }

    /// <summary>Exception type and message of a failed "Reset to dataset".</summary>
    public string? DatasetResetError { get; init; }
}

/// <summary>"Reset to dataset": which test dataset (<c>TestDatasets.Average</c> or <c>TestDatasets.Large</c>).</summary>
public record TenantResetToDatasetDto
{
    [Required]
    public string Dataset { get; init; } = default!;

    /// <summary>The tenant's name typed again, as a guard against resetting the wrong tenant.</summary>
    [Required]
    public string ConfirmationName { get; init; } = default!;
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
