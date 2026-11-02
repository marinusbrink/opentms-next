using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using OpenTms.Platform.Maintenance;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;

namespace OpenTms.HealthChecks;

/// <summary>
/// Every tenant with its own database can be reached: one connect and <c>SELECT 1</c> per database, at most
/// <see cref="Parallelism"/> at a time, each within <see cref="Timeout"/>, on an unpooled connection. Raw SQL by necessity
/// (a connection test before any DbContext), documented in docs/designs/maintenance-view.md, "Components". Shared-tier tenants live in the host database
/// (the database check). An unreachable tenant database is Unhealthy for that tenant only: the check is not part of
/// /health/ready, so it never takes the API out of rotation.
/// </summary>
public class TenantDatabasesHealthCheck : IHealthCheck, ITransientDependency
{
    public const string Name = "tenant-databases";

    public const int Parallelism = 4;

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>At most this many tenant names in the result; the count covers the rest.</summary>
    public const int MaxNames = 10;

    private static readonly string[] Settings = ["Tenant connection strings (Tenants app)"];

    private readonly ITenantStore _tenantStore;

    public TenantDatabasesHealthCheck(ITenantStore tenantStore)
    {
        _tenantStore = tenantStore;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var tenants = (await _tenantStore.GetListAsync(includeDetails: true))
            .Where(t => !string.IsNullOrWhiteSpace(t.ConnectionStrings?.Default))
            .ToList();

        using var gate = new SemaphoreSlim(Parallelism);
        var results = await Task.WhenAll(tenants.Select(async tenant =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return (tenant.Name, Error: await ProbeAsync(tenant.ConnectionStrings!.Default!, cancellationToken));
            }
            finally
            {
                gate.Release();
            }
        }));

        var failed = results.Where(r => r.Error != null).OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var extra = new Dictionary<string, object>
        {
            ["total"] = tenants.Count,
            ["count"] = failed.Count,
            ["tenants"] = string.Join(", ", failed.Take(MaxNames).Select(r => r.Name))
        };

        if (failed.Count == 0)
        {
            return HealthCheckResult.Healthy(
                $"All {tenants.Count} tenant databases answered.",
                HealthCheckFacts.Data(HealthCheckFacts.Reasons.Ok, Settings, extra: extra));
        }

        return HealthCheckResult.Unhealthy(
            $"{failed.Count} of {tenants.Count} tenant databases did not answer: {extra["tenants"]}. First error: {failed[0].Error}",
            data: HealthCheckFacts.Data(HealthCheckFacts.Reasons.Unreachable, Settings, extra: extra));
    }

    /// <summary>Null when the database answered, otherwise the reason (Npgsql's messages carry no password).</summary>
    private static async Task<string?> ProbeAsync(string connectionString, CancellationToken cancellationToken)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString)
            {
                Timeout = (int)Timeout.TotalSeconds,
                CommandTimeout = (int)Timeout.TotalSeconds,
                /* No pool of its own: a pooled probe would keep one idle connection per tenant database open. */
                Pooling = false
            };
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            return null;
        }
        catch (Exception e) when (e is NpgsqlException or TimeoutException or ArgumentException or InvalidOperationException)
        {
            return e.Message;
        }
    }
}
