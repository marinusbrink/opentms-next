using System;
using Volo.Abp.BackgroundJobs;

namespace OpenTms.Platform.Tenants;

/// <summary>Only the tenant id: Hangfire stores job arguments in plain text, so no address, password or connection string.</summary>
[BackgroundJobName("Platform.TenantProvisioning")]
public class TenantProvisioningArgs
{
    public Guid TenantId { get; set; }
}

[BackgroundJobName("Platform.TenantDatabaseDrop")]
public class TenantDatabaseDropArgs
{
    public Guid TenantId { get; set; }

    public string Host { get; set; } = default!;

    public int Port { get; set; }

    public string DatabaseName { get; set; } = default!;

    public string? RoleName { get; set; }
}

[BackgroundJobName("Platform.TenantInvitation")]
public class TenantInvitationArgs
{
    public Guid TenantId { get; set; }

    /// <summary>One per "Resend invitation": a redelivery of the same job carries the same id and sends nothing.</summary>
    public Guid RequestId { get; set; }
}
