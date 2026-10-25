using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Orders.Events;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.PostgreSql.TransportRun;
using OpenTms.TestDoubles;
using OpenTms.TransportRun;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace OpenTms.PostgreSql.PlanningExecution;

/// <summary>
/// Shared plumbing for the Planning &amp; Execution tests against real PostgreSQL (design 59): the seeded default trip
/// type and waypoint types of each tenant database, Master Data places created in the same transaction, real orders
/// created through the Orders service (their facts land in the outbox, which a test does not dispatch: the handler under
/// test is called directly), legs generated through the real handler, routing through <see cref="TestRoutingWorld"/>.
/// Every helper runs inside the caller's unit of work.
/// </summary>
public abstract class PlanningPostgreSqlTestBase : OpenTmsTestBase<TransportRunPostgreSqlTestModule>
{
    protected static readonly DateOnly Day = new(2026, 10, 26);

    protected ICurrentTenant CurrentTenant { get; }

    protected IUnitOfWorkManager UnitOfWorkManager { get; }

    protected static Guid TenantA => PostgreSqlTestDatabases.TenantAId;

    protected static Guid SharedTenant => PostgreSqlTestDatabases.SharedTenantId;

    protected TestRoutingWorld World => GetRequiredService<TestRoutingWorld>();

    protected TransportRunFixture Run => GetRequiredService<TransportRunFixture>();

    protected ITransportOrderAppService Orders => GetRequiredService<ITransportOrderAppService>();

    protected OrderLegsHandler Handler => GetRequiredService<OrderLegsHandler>();

    protected TripOperations Operations => GetRequiredService<TripOperations>();

    protected IRepository<Leg, Guid> LegRepository => GetRequiredService<IRepository<Leg, Guid>>();

    protected IRepository<TransportTrip, Guid> TripRepository => GetRequiredService<IRepository<TransportTrip, Guid>>();

    /// <summary>The Orders master data of one tenant (no foreign keys from Planning; design 56).</summary>
    protected sealed record OrdersMasterData(Guid Load, Guid Unload, Guid DepotIn, Guid DepotOut, Guid Pallet, Guid OrderTypeId);

    private readonly Dictionary<Guid, OrdersMasterData> _ordersMasterData = new();

    protected PlanningPostgreSqlTestBase()
    {
        CurrentTenant = GetRequiredService<ICurrentTenant>();
        UnitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();
    }

    protected static DateTimeOffset At(int hour, int minute = 0, int day = 26) => new(2026, 10, day, hour, minute, 0, TimeSpan.FromHours(2));

    protected async Task InRolledBackTransactionAsync(Guid? tenantId, Func<Task> action)
    {
        using (CurrentTenant.Change(tenantId))
        using (var unitOfWork = UnitOfWorkManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true))
        {
            try
            {
                await action();
            }
            finally
            {
                await unitOfWork.RollbackAsync();
                _ordersMasterData.Remove(tenantId ?? Guid.Empty);
            }
        }
    }

    protected Task FlushAsync() => UnitOfWorkManager.Current!.SaveChangesAsync();

    protected static async Task<Npgsql.PostgresException> ShouldViolateUniqueIndexAsync(Func<Task> insert, string indexName)
    {
        var ex = await Shouldly.Should.ThrowAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(insert);
        var postgres = Shouldly.ShouldBeTestExtensions.ShouldBeOfType<Npgsql.PostgresException>(ex.InnerException);
        Shouldly.ShouldBeStringTestExtensions.ShouldBe(postgres.SqlState, Npgsql.PostgresErrorCodes.UniqueViolation);
        Shouldly.ShouldBeStringTestExtensions.ShouldBe(postgres.ConstraintName, indexName);
        return postgres;
    }

    /* Orders master data of the current tenant */

    private OrdersMasterData Current => _ordersMasterData.TryGetValue(CurrentTenant.Id ?? Guid.Empty, out var md)
        ? md
        : throw new InvalidOperationException("Call PlaceAsync or OrdersMasterDataAsync first: the Orders master data of this tenant is not built yet.");

    protected Guid Load => Current.Load;

    protected Guid Unload => Current.Unload;

    protected Guid DepotIn => Current.DepotIn;

    protected Guid DepotOut => Current.DepotOut;

    protected Guid Pallet => Current.Pallet;

    protected Guid OrderTypeId => Current.OrderTypeId;

    /// <summary>The tenant's Orders master data: the seeded LOAD/UNLOAD and STD, plus the depot pair and a pallet unit (get or create).</summary>
    protected async Task<OrdersMasterData> OrdersMasterDataAsync()
    {
        var tenant = CurrentTenant.Id ?? Guid.Empty;
        if (_ordersMasterData.TryGetValue(tenant, out var existing))
        {
            return existing;
        }

        var load = await Run.WaypointTypeAsync("LOAD", "Load", "Laden", WaypointActivity.Load, 30);
        var unload = await Run.WaypointTypeAsync("UNLOAD", "Unload", "Lossen", WaypointActivity.Unload, 30);
        var depotOut = await Run.WaypointTypeAsync("DEPOTOUT", "Depot out", "Depot uit", WaypointActivity.Load, 15);
        var depotIn = await Run.WaypointTypeAsync("DEPOTIN", "Depot in", "Depot in", WaypointActivity.Unload, 15, successorId: depotOut);
        var pallet = await Run.TransportUnitAsync("Pallet");
        var orderType = await Run.OrderTypeAsync(TransportOrderRequiredFields.None, TransportRunFixture.NewSuffix(), load, unload);
        await FlushAsync();
        var md = new OrdersMasterData(load, unload, depotIn, depotOut, pallet, orderType);
        _ordersMasterData[tenant] = md;
        return md;
    }

    /// <summary>The default trip type the DbMigrator seeded in the current tenant.</summary>
    protected async Task<Guid> SeededTripTypeAsync()
        => (await GetRequiredService<IRepository<TransportTripType, Guid>>().GetListAsync()).OrderBy(t => t.IsDefault ? 0 : 1).First().Id;

    /// <summary>A relation with one geocoded address (codes carry a suffix: committed rows never clash).</summary>
    protected async Task<(Guid RelationId, Guid AddressId)> PlaceAsync(string code, string name, double latitude, double longitude, string city = "Amsterdam")
    {
        await OrdersMasterDataAsync();
        var place = await Run.PlaceAsync(code, name, OpenTms.MasterData.Relations.RelationRoles.Customer | OpenTms.MasterData.Relations.RelationRoles.Depot, latitude, longitude, city);
        await FlushAsync();
        return (place.RelationId, place.AddressId);
    }

    /* Orders (real rows) */

    protected static TransportOrderStopInputDto StopAt(Guid relationId, Guid addressId, Guid waypointTypeId, DateOnly? date = null,
        DateTimeOffset? windowFrom = null, DateTimeOffset? windowTill = null, string? reference = null)
        => TransportRunFixture.Stop(waypointTypeId, relationId, addressId, date ?? Day, reference, null,
            windowFrom == null ? null : TimeOnly.FromTimeSpan(windowFrom.Value.TimeOfDay), windowTill == null ? null : TimeOnly.FromTimeSpan(windowTill.Value.TimeOfDay), 30);

    protected TransportOrderLineInputDto LineOf(decimal quantity, double? weightKg, params TransportOrderStopInputDto[] stops) => new()
    {
        TransportUnitId = Pallet,
        Quantity = quantity,
        WeightKg = weightKg,
        LoadingMeters = (double)quantity * 0.4,
        Epq = (double)quantity,
        Stops = stops.ToList()
    };

    /// <summary>A Draft order of the customer with the given lines, dated <see cref="Day"/>, in the current tenant.</summary>
    protected async Task<TransportOrderDto> OrderAsync(Guid customerRelationId, params TransportOrderLineInputDto[] lines)
    {
        await OrdersMasterDataAsync();
        var order = await Orders.CreateAsync(new TransportOrderCreateDto
        {
            TransportOrderTypeId = OrderTypeId,
            CustomerRelationId = customerRelationId,
            OrderDate = Day,
            Lines = lines.ToList()
        });
        await FlushAsync();
        return order;
    }

    /// <summary>Confirms the order and lets Planning react to the fact (the outbox is not dispatched here: the handler runs directly).</summary>
    protected async Task<List<Leg>> ConfirmAsync(Guid orderId)
    {
        var order = await Orders.GetAsync(orderId);
        if (order.Status == TransportOrderStatus.Draft)
        {
            await Orders.ConfirmAsync(orderId);
            await FlushAsync();
        }

        await Handler.HandleEventAsync(new TransportOrderConfirmedEto { OrderId = orderId });
        await FlushAsync();
        return (await LegRepository.GetListAsync(l => l.OrderId == orderId)).OrderBy(l => l.OrderLineId).ThenBy(l => l.Position).ToList();
    }

    protected Task<TransportTrip> CreateTripAsync(Guid typeId, DateTimeOffset? departure = null, DateTimeOffset? arrival = null, StopCompositionMode mode = StopCompositionMode.MergeByLocationAndActivity)
        => Operations.CreateAsync(new TransportTripCreateDto
        {
            TransportTripTypeId = typeId,
            PlannedDeparture = departure ?? At(6),
            PlannedArrival = arrival ?? At(18),
            StopCompositionMode = mode
        }, null, null, null);
}
