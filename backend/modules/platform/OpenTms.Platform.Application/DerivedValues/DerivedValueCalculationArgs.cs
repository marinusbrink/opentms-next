using System;
using Volo.Abp.BackgroundJobs;

namespace OpenTms.Platform.DerivedValues;

/// <summary>Ids only: Hangfire stores job arguments in plain text (design 49).</summary>
[BackgroundJobName("OpenTms.Platform.DerivedValueCalculation")]
public class DerivedValueCalculationArgs
{
    public Guid? TenantId { get; set; }

    public string TargetType { get; set; } = string.Empty;

    public Guid TargetId { get; set; }

    public long Version { get; set; }
}
