using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTms.Platform.Maintenance.Health;

/// <summary>The outcome of one registered health check.</summary>
public sealed record HealthCheckEntry(string Name, string Status, string? Description, TimeSpan Duration, IReadOnlyList<string> Tags, string? Error);

/// <summary>The outcome of all registered health checks of this instance.</summary>
public sealed record HealthReport(string Status, TimeSpan TotalDuration, IReadOnlyList<HealthCheckEntry> Entries);

/// <summary>
/// The port behind the Health tab. The host implements it over ASP.NET Core's
/// <c>HealthCheckService</c> (the same checks that answer <c>/health/ready</c>); the module itself
/// does not depend on the health-check packages.
/// </summary>
public interface IHealthReportProvider
{
    Task<HealthReport> GetReportAsync(CancellationToken cancellationToken = default);
}
