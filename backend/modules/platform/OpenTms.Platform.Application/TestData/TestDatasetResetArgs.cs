using System;
using Volo.Abp.BackgroundJobs;

namespace OpenTms.Platform.TestData;

[BackgroundJobName("Platform.TestDatasetReset")]
public class TestDatasetResetArgs
{
    public Guid TenantId { get; set; }

    /// <summary>A <see cref="TestDatasets"/> name.</summary>
    public string Dataset { get; set; } = default!;

    /// <summary>One per "Reset to dataset": a redelivery of a finished run does nothing.</summary>
    public Guid RequestId { get; set; }
}
