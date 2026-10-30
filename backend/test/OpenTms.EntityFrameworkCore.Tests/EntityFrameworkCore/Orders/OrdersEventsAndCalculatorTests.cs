using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Financial.Events;
using OpenTms.Orders;
using OpenTms.Orders.Events;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution.Events;
using OpenTms.PlanningExecution.Published;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.DerivedValues;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Uow;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Orders;

/// <summary>
/// The Orders side of the other modules' facts and the order summary calculator (design 56 "Flow 2", design 58 "Events")
/// on SQLite: Planning's and Financial's events request a new summary version (idempotently, only for orders that exist),
/// TransportTripCreated records the trip once, and the calculator writes revenue, cost, margin, progress, CO₂, counts,
/// first/last stop and trip numbers from the published readers over Planning's real legs and trips and Financial's real
/// rate lines. The attribute definitions list is covered here too. Risk class: Critical (idempotent handlers), High (calculator).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class OrdersEventsAndCalculatorTests : TransportOrderTestBase<OpenTmsEntityFrameworkCoreTestModule>
{
    private IDistributedEventBus Bus => GetRequiredService<IDistributedEventBus>();

    [Fact]
    public async Task Plannings_and_Financials_events_request_the_summary_for_existing_orders_only()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        var order = await Orders.CreateAsync(CreateDto(f, confirm: true));
        var before = SummaryReasons(order.Id).Count;

        await Bus.PublishAsync(new LegAssignedEto { LegId = Guid.NewGuid(), TripId = Guid.NewGuid(), OrderId = order.Id });
        await Bus.PublishAsync(new LegUnassignedEto { LegId = Guid.NewGuid(), TripId = Guid.NewGuid(), OrderId = order.Id });
        await Bus.PublishAsync(new LegsGeneratedEto { OrderId = order.Id, LegIds = [Guid.NewGuid()] });
        await Bus.PublishAsync(new TransportTripStatusChangedEto { TripId = Guid.NewGuid(), Status = TransportTripStatus.InProgress, OrderIds = [order.Id, Guid.NewGuid()] });
        await Bus.PublishAsync(new TransportTripRecalculatedEto { TripId = Guid.NewGuid(), Version = 3, OrderIds = [order.Id] });
        await Bus.PublishAsync(new RateLinesChangedEto { OrderId = order.Id, Version = 2 });
        await Bus.PublishAsync(new RateLinesChangedEto { TripId = Guid.NewGuid(), Version = 2 });
        await Bus.PublishAsync(new LegAssignedEto { LegId = Guid.NewGuid(), TripId = Guid.NewGuid(), OrderId = Guid.NewGuid() });

        var reasons = SummaryReasons(order.Id).Skip(before).ToList();
        reasons.ShouldBe(["LegAssigned", "LegUnassigned", "LegsGenerated", "TripStatusChanged", "TripRecalculated", "RateLinesChanged"]);
        var summaries = Requests.All.Where(r => r.TargetType == OrdersConsts.OrderSummaryTargetType).ToList();
        summaries.Count(r => r.TargetId != order.Id && r.Reason == "LegAssigned").ShouldBe(0, "an unknown order requests nothing");
    }

    [Fact]
    public async Task TransportTripCreated_records_the_trip_on_the_request_once_scenario_54()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        var dto = CreateDto(f, confirm: true);
        dto.TripRequest = new TransportOrderTripRequestInputDto
        {
            TransportTripTypeId = f.TripTypeId, DepartureRelationId = f.DepotId, DepartureAddressId = f.DepotAddressId, ArrivalRelationId = f.ShopId, ArrivalAddressId = f.ShopAddressId,
            PlannedDeparture = TransportRun.TransportRunFixture.At(Today, 6)
        };

        /* "Tevens trip" (Q9): Planning creates the trip on the confirm and tells Orders, which records it on the request. */
        var created = await Orders.CreateAsync(dto);
        var trip = (await GetRequiredService<IRepository<TransportTrip, Guid>>().GetListAsync(t => t.CreatedFromOrderId == created.Id)).ShouldHaveSingleItem();
        /* The facts are delivered when the create's unit of work completes: the order is read again afterwards. */
        var order = await Orders.GetAsync(created.Id);
        order.TripRequest.ShouldNotBeNull().TripId.ShouldBe(trip.Id);
        (await Run.LegsOfAsync(order.Id)).ShouldAllBe(l => l.TripId == trip.Id, "the order's legs ride on the requested trip");
        SummaryReasons(order.Id).ShouldContain("TripCreated");
        var before = SummaryReasons(order.Id).Count;

        /* Redelivered, and a trip created for no order: nothing changes. */
        await Bus.PublishAsync(new TransportTripCreatedEto { TripId = trip.Id, CreatedFromOrderId = order.Id });
        await Bus.PublishAsync(new TransportTripCreatedEto { TripId = Guid.NewGuid(), CreatedFromOrderId = null });

        var reloaded = await Orders.GetAsync(order.Id);
        reloaded.TripRequest.ShouldNotBeNull().TripId.ShouldBe(trip.Id);
        SummaryReasons(order.Id).Count.ShouldBe(before, "the redelivery changed nothing");
        (await GetRequiredService<OpenTms.Orders.Published.ITransportOrderLookup>().GetTripRequestAsync(order.Id))!.TripId.ShouldBe(trip.Id);
    }

    [Fact]
    public async Task The_calculator_writes_the_stored_summary_from_the_rate_lines_the_legs_and_the_addresses()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        var order = await Orders.CreateAsync(CreateDto(f, confirm: true, lines: 2));
        var lineIds = order.Lines.OrderBy(l => l.Position).Select(l => l.Id!.Value).ToList();
        await Run.StoreRevenueAsync(order, 1250m, version: 4);
        var trip = await Run.PlanAsync(f.TripTypeId, [order.Id]);
        var legs = await Run.LegsOfAsync(order.Id);
        await Run.SetLegValuesAsync(legs.Single(l => l.OrderLineId == lineIds[0]).Id, distanceM: 120_000, co2Kg: 95.5m, allocatedCost: 400m);
        await Run.SetLegValuesAsync(legs.Single(l => l.OrderLineId == lineIds[1]).Id, distanceM: 50_000, co2Kg: 40m, allocatedCost: 200.25m);

        /* As the job runs it: one unit of work around the calculation (design 49), so the aggregate is read and written in one context. */
        await WithUnitOfWorkAsync(() => GetRequiredService<OrderSummaryCalculator>().CalculateAsync(new DerivedValueCalculationContext(OrdersConsts.OrderSummaryTargetType, order.Id, 1, default)));

        var summary = await Orders.GetSummaryAsync(order.Id);
        summary.RevenueAmount.ShouldBe(1250m);
        summary.CostOfRevenueAmount.ShouldBe(600.25m);
        summary.MarginAmount.ShouldBe(649.75m);
        summary.Co2Kg.ShouldBe(135.5m);
        summary.ExecutionProgress.ShouldBe(ExecutionProgress.Planned);
        summary.LineCount.ShouldBe(2);
        summary.StopCount.ShouldBe(4);
        (summary.FirstStopName, summary.FirstStopCity, summary.FirstStopDate).ShouldBe(("DC Venlo", "Venlo", Today));
        (summary.LastStopName, summary.LastStopCity, summary.LastStopDate).ShouldBe(("Supermarkt Utrecht", "Utrecht", Today.AddDays(1)));
        summary.TripNumbers.ShouldBe(trip.Number);
        summary.CalculatedAt.ShouldNotBeNull();

        var detail = await Orders.GetAsync(order.Id);
        detail.Lines.Single(l => l.Id == lineIds[0]).DistanceM.ShouldBe(120_000);
        detail.Lines.Single(l => l.Id == lineIds[1]).Co2Kg.ShouldBe(40m);
        var row = (await Orders.GetListAsync(new TransportOrderListRequestDto { WildcardSearch = "venlo" })).Rows.ShouldHaveSingleItem();
        row.MarginAmount.ShouldBe(649.75m);

        /* A missing order writes nothing and does not fail (the job logs and completes). */
        await WithUnitOfWorkAsync(() => GetRequiredService<OrderSummaryCalculator>().CalculateAsync(new DerivedValueCalculationContext(OrdersConsts.OrderSummaryTargetType, Guid.NewGuid(), 1, default)));
    }

    [Fact]
    public void The_progress_follows_the_legs_trips_and_arrivals()
    {
        OrderSummaryCalculator.ProgressOf([]).ShouldBe(ExecutionProgress.Unplanned);
        OrderSummaryCalculator.ProgressOf([Leg()]).ShouldBe(ExecutionProgress.Unplanned);
        OrderSummaryCalculator.ProgressOf([Leg(), Leg(TransportTripStatus.Planned)]).ShouldBe(ExecutionProgress.PartlyPlanned);
        OrderSummaryCalculator.ProgressOf([Leg(TransportTripStatus.Planned), Leg(TransportTripStatus.Planned)]).ShouldBe(ExecutionProgress.Planned);
        OrderSummaryCalculator.ProgressOf([Leg(TransportTripStatus.InProgress), Leg(TransportTripStatus.InProgress, arrived: true)]).ShouldBe(ExecutionProgress.InProgress);
        OrderSummaryCalculator.ProgressOf([Leg(TransportTripStatus.InProgress, arrived: true), Leg(TransportTripStatus.Completed)]).ShouldBe(ExecutionProgress.Delivered);
    }

    private static LegSnapshotDto Leg(TransportTripStatus? tripStatus = null, bool arrived = false) => new()
    {
        Id = Guid.NewGuid(), OrderId = Guid.Empty, OrderLineId = Guid.Empty, Position = 1,
        TripId = tripStatus == null ? null : Guid.NewGuid(), TripNumber = tripStatus == null ? null : "R-1", TripStatus = tripStatus,
        Origin = new LegEndDto { PlannedDate = new DateOnly(2026, 10, 20) },
        Destination = new LegEndDto { PlannedDate = new DateOnly(2026, 10, 20), ActualAt = arrived ? DateTimeOffset.UtcNow : null }
    };

    [Fact]
    public async Task Attribute_definitions_are_unique_per_target_and_key_and_feed_the_attribute_check_Q12()
    {
        using var _ = InTenantA();
        var service = GetRequiredService<IOrderAttributeDefinitionAppService>();

        var project = await service.CreateAsync(new OrderAttributeDefinitionCreateDto { Target = OrderAttributeTarget.Order, Key = " Project ", Names = Names("Project"), Position = 2 });
        project.Key.ShouldBe("Project");
        await service.CreateAsync(new OrderAttributeDefinitionCreateDto { Target = OrderAttributeTarget.Line, Key = "Project", Names = Names("Line project"), Position = 1 });
        (await Should.ThrowAsync<BusinessException>(() => service.CreateAsync(new OrderAttributeDefinitionCreateDto { Target = OrderAttributeTarget.Order, Key = "Project", Names = Names("Again") })))
            .Code.ShouldBe(ReferenceDataErrorCodes.DuplicateKey);
        (await Should.ThrowAsync<BusinessException>(() => service.CreateAsync(new OrderAttributeDefinitionCreateDto { Target = OrderAttributeTarget.Order, Key = "bad key!", Names = Names("Bad") })))
            .Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);

        (await service.GetListAsync(new OrderAttributeDefinitionListRequestDto { Target = OrderAttributeTarget.Order })).Rows.ShouldHaveSingleItem().Key.ShouldBe("Project");
        (await service.GetListAsync(new OrderAttributeDefinitionListRequestDto { WildcardSearch = "line" })).FilteredCount.ShouldBe(1);
        (await service.GetLookupAsync()).Count.ShouldBe(2);

        var updated = await service.UpdateAsync(project.Id, new OrderAttributeDefinitionUpdateDto { Target = OrderAttributeTarget.Order, Key = "Project", Names = Names("Project code"), Position = 1, IsArchived = true, ConcurrencyStamp = project.ConcurrencyStamp });
        updated.IsArchived.ShouldBeTrue();
        (await service.GetListAsync(new OrderAttributeDefinitionListRequestDto { Target = OrderAttributeTarget.Order })).TotalCount.ShouldBe(0, "archived rows are hidden");
        (await service.GetListAsync(new OrderAttributeDefinitionListRequestDto { Target = OrderAttributeTarget.Order, IncludeArchived = true })).TotalCount.ShouldBe(1);

        /* A line attribute with a defined key saves; an undefined one is refused. */
        var f = await FixtureAsync();
        var dto = CreateDto(f);
        dto.Lines[0].Attributes = new() { ["Project"] = "P-1" };
        var order = await Orders.CreateAsync(dto);
        order.Lines.Single().Attributes["Project"].ShouldBe("P-1");
        dto.Lines[0].Attributes = new() { ["Seal"] = "ZG-1" };
        (await Should.ThrowAsync<BusinessException>(() => Orders.CreateAsync(dto))).Code.ShouldBe(OrdersErrorCodes.InvalidAttributes);

        using (InTenantB())
        {
            (await service.GetListAsync(new OrderAttributeDefinitionListRequestDto { IncludeArchived = true })).TotalCount.ShouldBe(0);
        }
    }
}
