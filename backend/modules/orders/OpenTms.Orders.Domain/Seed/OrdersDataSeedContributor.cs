using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTms.Orders.TransportOrders;
using OpenTms.Platform.Localization;
using OpenTms.Platform.NumberSequences;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace OpenTms.Orders.Seed;

/// <summary>
/// Seeds the Orders master data per tenant database (design 51, Migration strategy), run by the DbMigrator and the
/// provisioning job through ABP's data seeder: the waypoint types first, then the time slots, then the default
/// transport order type with its default stops. Idempotent on the code: an existing or soft-deleted row is never
/// touched. Skips the host (host-level master data is parked, assessment §4). Logs tenant id and counts only.
/// </summary>
public class OrdersDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IRepository<WaypointType, Guid> _waypointTypes;
    private readonly IRepository<TimeSlot, Guid> _timeSlots;
    private readonly IRepository<TransportOrderType, Guid> _orderTypes;
    private readonly ICurrentTenant _currentTenant;
    private readonly IDataFilter _dataFilter;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ILogger<OrdersDataSeedContributor> _logger;

    public OrdersDataSeedContributor(
        IRepository<WaypointType, Guid> waypointTypes,
        IRepository<TimeSlot, Guid> timeSlots,
        IRepository<TransportOrderType, Guid> orderTypes,
        ICurrentTenant currentTenant,
        IDataFilter dataFilter,
        IGuidGenerator guidGenerator,
        ILogger<OrdersDataSeedContributor> logger)
    {
        _waypointTypes = waypointTypes;
        _timeSlots = timeSlots;
        _orderTypes = orderTypes;
        _currentTenant = currentTenant;
        _dataFilter = dataFilter;
        _guidGenerator = guidGenerator;
        _logger = logger;
    }

    private static readonly (string Code, string En, string Nl, WaypointActivity Activity, int? Minutes, string? Successor)[] WaypointTypeRows =
    [
        ("LOAD", "Load", "Laden", WaypointActivity.Load, 30, "UNLOAD"),
        ("UNLOAD", "Unload", "Lossen", WaypointActivity.Unload, 30, null),
        ("VIA", "Via", "Via", WaypointActivity.Via, null, null),
        ("COUPLE", "Couple trailer", "Aankoppelen", WaypointActivity.CoupleTrailer, null, null),
        ("UNCOUPLE", "Uncouple trailer", "Afkoppelen", WaypointActivity.UncoupleTrailer, null, null),
        ("CUSTOMS", "Customs", "Douane", WaypointActivity.Customs, null, null),
    ];

    private static readonly (string Code, string En, string Nl, TimeOnly Start, TimeOnly End)[] TimeSlotRows =
    [
        ("MORNING", "Morning", "Ochtend", new TimeOnly(8, 0), new TimeOnly(12, 0)),
        ("AFTERNOON", "Afternoon", "Middag", new TimeOnly(12, 0), new TimeOnly(17, 0)),
        ("DAY", "Day", "Dag", new TimeOnly(8, 0), new TimeOnly(17, 0)),
        ("EVENING", "Evening", "Avond", new TimeOnly(17, 0), new TimeOnly(22, 0)),
        ("NIGHT", "Night", "Nacht", new TimeOnly(22, 0), new TimeOnly(6, 0)),
    ];

    private static LocalizedName Name(string en, string nl)
        => LocalizedName.Create(new Dictionary<string, string?> { ["en"] = en, ["nl"] = nl });

    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        if (context.TenantId == null)
        {
            return;
        }

        using (_currentTenant.Change(context.TenantId))
        {
            var waypointTypes = await SeedWaypointTypesAsync(context.TenantId.Value);
            var timeSlots = await SeedTimeSlotsAsync(context.TenantId.Value);
            var orderTypes = await SeedOrderTypeAsync(context.TenantId.Value);
            if (waypointTypes + timeSlots + orderTypes > 0)
            {
                _logger.LogInformation(
                    "Orders seed for tenant {TenantId}: {WaypointTypes} waypoint types, {TimeSlots} time slots, {OrderTypes} transport order types inserted.",
                    context.TenantId, waypointTypes, timeSlots, orderTypes);
            }
        }
    }

    private async Task<int> SeedWaypointTypesAsync(Guid tenantId)
    {
        List<WaypointType> existing;
        using (_dataFilter.Disable<ISoftDelete>())
        {
            existing = await _waypointTypes.GetListAsync();
        }

        /* Successors refer to live rows only, seeded now or kept from before. */
        var live = existing.Where(t => !t.IsDeleted).ToDictionary(t => t.Code);
        var inserted = new List<WaypointType>();
        foreach (var row in WaypointTypeRows.Where(r => existing.All(t => t.Code != r.Code)))
        {
            var type = new WaypointType(_guidGenerator.Create(), tenantId, row.Code, Name(row.En, row.Nl), row.Activity);
            type.SetDefaultDuration(row.Minutes);
            inserted.Add(type);
            live[row.Code] = type;
        }

        foreach (var type in inserted)
        {
            var successor = WaypointTypeRows.Single(r => r.Code == type.Code).Successor;
            if (successor != null && live.TryGetValue(successor, out var next))
            {
                type.SetSuccessor(next.Id);
            }
        }

        /* Successors first, so each insert finds the row it points at. */
        foreach (var type in inserted.OrderBy(t => t.SuccessiveWaypointTypeId == null ? 0 : 1))
        {
            await _waypointTypes.InsertAsync(type, autoSave: true);
        }

        return inserted.Count;
    }

    private async Task<int> SeedTimeSlotsAsync(Guid tenantId)
    {
        List<TimeSlot> existing;
        using (_dataFilter.Disable<ISoftDelete>())
        {
            existing = await _timeSlots.GetListAsync();
        }

        var count = 0;
        foreach (var row in TimeSlotRows.Where(r => existing.All(t => t.Code != r.Code)))
        {
            await _timeSlots.InsertAsync(new TimeSlot(_guidGenerator.Create(), tenantId, row.Code, Name(row.En, row.Nl), row.Start, row.End));
            count++;
        }

        return count;
    }

    private async Task<int> SeedOrderTypeAsync(Guid tenantId)
    {
        List<TransportOrderType> existing;
        using (_dataFilter.Disable<ISoftDelete>())
        {
            existing = await _orderTypes.GetListAsync();
        }

        if (existing.Any(t => t.Code == "STD"))
        {
            return 0;
        }

        var standard = new TransportOrderType(
            _guidGenerator.Create(),
            tenantId,
            "STD",
            Name("Standard", "Standaard"),
            NumberSequenceFormat.Create("TO", includeYear: true, includeMonth: false, separator: "-", digits: 5));
        if (!existing.Any(t => t.IsDefault && !t.IsDeleted))
        {
            standard.SetDefault(true);
        }

        var stops = (await _waypointTypes.GetListAsync(t => t.Code == "LOAD" || t.Code == "UNLOAD"))
            .OrderBy(t => t.Code == "LOAD" ? 0 : 1)
            .Select(t => t.Id)
            .ToList();
        standard.SetDefaultWaypoints(stops);

        await _orderTypes.InsertAsync(standard);
        return 1;
    }
}
