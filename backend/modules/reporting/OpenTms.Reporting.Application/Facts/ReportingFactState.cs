using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using OpenTms.Orders.Published;
using OpenTms.PlanningExecution.Published;
using OpenTms.Reporting.Settings;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.SettingManagement;

namespace OpenTms.Reporting.Facts;

/// <summary>Where a tenant's backfill is: each grain's last requested id, whether it ran out, and when both had.</summary>
public sealed class ReportingBackfillPosition
{
    public Guid? OrdersAfter { get; set; }

    public bool OrdersDone { get; set; }

    public Guid? TripsAfter { get; set; }

    public bool TripsDone { get; set; }

    public DateTime? ExhaustedAt { get; set; }

    /// <summary>Which sweep this is, starting at 1; capped by <see cref="ReportingMetricsOptions.BackfillMaxSweeps"/>.</summary>
    public int Sweep { get; set; } = 1;
}

/// <summary>
/// How many records of each grain differ from their facts: the difference of two counts, so a surplus fact (a lost delete) counts too.
/// Counts only, never ids: a query cannot join across modules to say which ones.
/// </summary>
public sealed class ReportingFactsMissing
{
    public long Orders { get; set; }

    public long Trips { get; set; }

    [JsonIgnore]
    public bool Any => Orders > 0 || Trips > 0;
}

/// <summary>
/// The current tenant's fact tables and the settings that say how far they are (design 75 "Migration strategy"), for the two
/// workers, the dataset reset and the query. Every statement runs under the tenant filter, so a shared-tier tenant in the host
/// database clears only its own rows (constitution rule 1). The caller owns the unit of work.
/// </summary>
public class ReportingFactState : ITransientDependency
{
    private readonly ISettingManager _settings;
    private readonly IRepository<OrderFact, Guid> _orderFacts;
    private readonly IRepository<TripFact, Guid> _tripFacts;
    private readonly IOrderFactReader _orders;
    private readonly ITripFactReader _trips;

    public ReportingFactState(
        ISettingManager settings,
        IRepository<OrderFact, Guid> orderFacts,
        IRepository<TripFact, Guid> tripFacts,
        IOrderFactReader orders,
        ITripFactReader trips)
    {
        _settings = settings;
        _orderFacts = orderFacts;
        _tripFacts = tripFacts;
        _orders = orders;
        _trips = trips;
    }

    public async Task<DateTime?> GetBackfillCompletedAtAsync() => await GetDateAsync(ReportingSettings.BackfillCompletedAt);

    public Task SetBackfillCompletedAtAsync(DateTime at) => SetDateAsync(ReportingSettings.BackfillCompletedAt, at);

    public async Task<DateTime?> GetRepairLastRunAtAsync() => await GetDateAsync(ReportingSettings.RepairLastRunAt);

    public Task SetRepairLastRunAtAsync(DateTime at) => SetDateAsync(ReportingSettings.RepairLastRunAt, at);

    public async Task<ReportingBackfillPosition?> GetBackfillPositionAsync()
    {
        var text = await _settings.GetOrNullForCurrentTenantAsync(ReportingSettings.BackfillPosition, fallback: false);
        return string.IsNullOrEmpty(text) ? null : JsonSerializer.Deserialize<ReportingBackfillPosition>(text);
    }

    public Task SetBackfillPositionAsync(ReportingBackfillPosition position)
        => _settings.SetForCurrentTenantAsync(ReportingSettings.BackfillPosition, JsonSerializer.Serialize(position));

    public async Task<ReportingFactsMissing?> GetFactsMissingAsync()
    {
        var text = await _settings.GetOrNullForCurrentTenantAsync(ReportingSettings.FactsMissing, fallback: false);
        return string.IsNullOrEmpty(text) ? null : JsonSerializer.Deserialize<ReportingFactsMissing>(text);
    }

    public Task SetFactsMissingAsync(ReportingFactsMissing missing)
        => _settings.SetForCurrentTenantAsync(ReportingSettings.FactsMissing, JsonSerializer.Serialize(missing));

    /// <summary>Four counts, so only when a backfill runs out of sweeps and in the daily repair of a tenant that has a number on record.</summary>
    public async Task<ReportingFactsMissing> CountFactsMissingAsync(CancellationToken cancellationToken = default)
        => new()
        {
            Orders = Math.Abs(await _orders.GetCountAsync(cancellationToken) - await _orderFacts.LongCountAsync(cancellationToken)),
            Trips = Math.Abs(await _trips.GetCountAsync(cancellationToken) - await _tripFacts.LongCountAsync(cancellationToken)),
        };

    /// <summary>The daily repair's recount of a number on record: it moves with the facts and goes when none is left, which ends the warning. Returns what is left.</summary>
    public async Task<ReportingFactsMissing?> RecountFactsMissingAsync(CancellationToken cancellationToken = default)
    {
        if (await GetFactsMissingAsync() == null)
        {
            return null;
        }

        var missing = await CountFactsMissingAsync(cancellationToken);
        await _settings.SetForCurrentTenantAsync(ReportingSettings.FactsMissing, missing.Any ? JsonSerializer.Serialize(missing) : null);
        return missing;
    }

    /// <summary>Starts over at the next backfill pass, which clears the facts first.</summary>
    public async Task ClearBackfillAsync()
    {
        await _settings.SetForCurrentTenantAsync(ReportingSettings.BackfillCompletedAt, null);
        await _settings.SetForCurrentTenantAsync(ReportingSettings.BackfillPosition, null);
        await _settings.SetForCurrentTenantAsync(ReportingSettings.FactsMissing, null);
    }

    public async Task ClearFactsAsync()
    {
        await _orderFacts.DeleteDirectAsync(_ => true);
        await _tripFacts.DeleteDirectAsync(_ => true);
    }

    private async Task<DateTime?> GetDateAsync(string name)
    {
        var text = await _settings.GetOrNullForCurrentTenantAsync(name, fallback: false);
        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at : null;
    }

    private Task SetDateAsync(string name, DateTime at)
        => _settings.SetForCurrentTenantAsync(name, at.ToString("O", CultureInfo.InvariantCulture));
}
