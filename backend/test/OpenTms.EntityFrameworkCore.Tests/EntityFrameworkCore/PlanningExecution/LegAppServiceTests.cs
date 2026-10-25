using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData.Relations;
using OpenTms.PlanningExecution;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace OpenTms.EntityFrameworkCore.PlanningExecution;

/// <summary>
/// LegAppService on SQLite (design 59 "LegAppService"; trips.md 59): the unplanned list with its filters, search and
/// sorts, the legs of an order or trip, and the editable fields with the window rule. Risk class: High.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class LegAppServiceTests : PlanningAppServiceTestBase
{
    [Fact]
    public async Task The_unplanned_list_filters_by_date_and_customer_searches_names_and_references_and_sorts()
    {
        using var _ = InTenantA();
        var a = await PlaceAsync("A", "Alpha", 52.0, 5.0);
        var b = await PlaceAsync("B", "Beta", 52.1, 5.1);
        var c = await PlaceAsync("C", "Gamma", 52.2, 5.2);
        var early = await OrderAsync(a.RelationId, LineOf(1, 100, StopAt(a, Load, Day, reference: "REF-EARLY"), StopAt(b, Unload, Day)));
        var late = await OrderAsync(c.RelationId, LineOf(2, 200, StopAt(c, Load, Day.AddDays(3)), StopAt(b, Unload, Day.AddDays(3))));
        await ConfirmOrderAsync(early);
        await ConfirmOrderAsync(late);

        var all = await Legs.GetListAsync(LegList());
        all.TotalCount.ShouldBe(2);
        all.Rows.Select(r => r.Origin.Name).ShouldBe(["Alpha", "Gamma"], "by origin date");
        all.Rows[0].CustomerName.ShouldBe("Alpha", "the customer's name through Orders' published lookup");
        all.Rows[0].OrderNumber.ShouldBe(early.Number ?? (await Orders.GetAsync(early.Id)).Number);

        (await Legs.GetListAsync(LegList(dateFrom: Day.AddDays(1)))).Rows.ShouldHaveSingleItem().Origin.Name.ShouldBe("Gamma");
        (await Legs.GetListAsync(LegList(dateTo: Day))).Rows.ShouldHaveSingleItem().Origin.Name.ShouldBe("Alpha");
        (await Legs.GetListAsync(LegList(customerRelationId: c.RelationId))).Rows.ShouldHaveSingleItem().Origin.Name.ShouldBe("Gamma");
        (await Legs.GetListAsync(LegList(search: "ref-early"))).Rows.ShouldHaveSingleItem();
        (await Legs.GetListAsync(LegList(search: "beta"))).Rows.Count.ShouldBe(2);
        var filtered = await Legs.GetListAsync(LegList(search: "gamma"));
        filtered.TotalCount.ShouldBe(2);
        filtered.FilteredCount.ShouldBe(1);
        var byQuantityDesc = LegList(sortBy: "quantity");
        byQuantityDesc.SortDir = "desc";
        (await Legs.GetListAsync(byQuantityDesc)).Rows.Select(r => r.Quantity).ShouldBe([2m, 1m]);
        (await Legs.GetListAsync(LegList(sortBy: "origin"))).Rows.Select(r => r.Origin.Name).ShouldBe(["Alpha", "Gamma"]);
        (await Should.ThrowAsync<BusinessException>(() => Legs.GetListAsync(LegList(sortBy: "colour")))).Code.ShouldBe(ReferenceDataErrorCodes.UnknownSortColumn);
    }

    [Fact]
    public async Task Update_changes_references_remarks_and_planned_dates_within_the_window_and_requests_the_trip_when_planned()
    {
        using var _ = InTenantA();
        var type = await TripTypeAsync();
        var a = await PlaceAsync("A", "Alpha", 52.0, 5.0);
        var b = await PlaceAsync("B", "Beta", 52.1, 5.1);
        /* The origin's window runs from 22:00 into the next morning (a window across midnight ends the next day). */
        var order = await OrderAsync(a.RelationId, LineOf(1, 100,
            StopAt(a, Load, Day, At(22), At(10, 0, 27)),
            StopAt(b, Unload, Day, At(12), At(14))));
        var orderId = await ConfirmOrderAsync(order);
        var leg = (await Legs.GetListAsync(LegList(orderId: orderId))).Rows.Single();

        var updated = await Legs.UpdateAsync(leg.Id, new LegUpdateDto
        {
            OriginReference = "PO-1",
            DestinationReference = "DN-1",
            Remarks = "Call before arrival",
            OriginPlannedDate = Day.AddDays(1),
            ConcurrencyStamp = leg.ConcurrencyStamp
        });
        updated.Origin.Reference.ShouldBe("PO-1");
        updated.Destination.Reference.ShouldBe("DN-1");
        updated.Remarks.ShouldBe("Call before arrival");
        updated.Origin.PlannedDate.ShouldBe(Day.AddDays(1));
        Requests.All.ShouldNotContain(r => r.TargetType == PlanningExecutionApplicationModule.TripTargetType, "an unplanned leg requests no trip");

        (await Should.ThrowAsync<BusinessException>(() => Legs.UpdateAsync(leg.Id, new LegUpdateDto { DestinationPlannedDate = Day.AddDays(2), ConcurrencyStamp = updated.ConcurrencyStamp })))
            .Code.ShouldBe(PlanningExecutionErrorCodes.LegDateOutsideStopWindow);

        var trip = await CreateTripAsync(type, At(6), At(18, 0, 27));
        await Trips.AssignLegsAsync(trip.Id, new AssignLegsDto { LegIds = [leg.Id] });
        var planned = await Legs.GetAsync(leg.Id);
        var before = Requests.For(PlanningExecutionApplicationModule.TripTargetType, trip.Id).Count;
        await Legs.UpdateAsync(leg.Id, new LegUpdateDto { OriginPlannedDate = Day, ConcurrencyStamp = planned.ConcurrencyStamp });
        Requests.For(PlanningExecutionApplicationModule.TripTargetType, trip.Id).Count.ShouldBe(before + 1);
        (await Legs.GetAsync(leg.Id)).TripNumber.ShouldBe(trip.Number);

        var stale = await Should.ThrowAsync<Volo.Abp.Data.AbpDbConcurrencyException>(() => Legs.UpdateAsync(leg.Id, new LegUpdateDto { Remarks = "x", ConcurrencyStamp = leg.ConcurrencyStamp }));
        stale.ShouldNotBeNull();
    }
}
