using System;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
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

    public ReportingFactState(ISettingManager settings, IRepository<OrderFact, Guid> orderFacts, IRepository<TripFact, Guid> tripFacts)
    {
        _settings = settings;
        _orderFacts = orderFacts;
        _tripFacts = tripFacts;
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

    /// <summary>Starts over at the next backfill pass, which clears the facts first.</summary>
    public async Task ClearBackfillAsync()
    {
        await _settings.SetForCurrentTenantAsync(ReportingSettings.BackfillCompletedAt, null);
        await _settings.SetForCurrentTenantAsync(ReportingSettings.BackfillPosition, null);
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
