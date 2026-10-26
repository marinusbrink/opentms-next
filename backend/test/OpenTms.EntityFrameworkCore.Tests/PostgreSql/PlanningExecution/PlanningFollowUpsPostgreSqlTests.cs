using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using OpenTms.Orders.Events;
using OpenTms.PlanningExecution.EntityFrameworkCore;
using OpenTms.PlanningExecution.EntityFrameworkCore.Migrations;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.TransportTrips;
using Shouldly;
using Volo.Abp.Content;
using Volo.Abp.EntityFrameworkCore;
using Xunit;

namespace OpenTms.PostgreSql.PlanningExecution;

/// <summary>
/// The Transport follow-ups of Planning &amp; Execution against real PostgreSQL, both tenant tiers: the trip export and
/// the trip picker never show another tenant's trips (exports and lookups are the classic leaks); a planner's edit of a
/// leg (reference, time window) survives a regeneration from unchanged stops with the leg still on its trip, while a
/// changed stop still unassigns (design 59); and the PlanningLegSourceKeys migration (expand only, newest). Writes are
/// rolled back. Risk class: Critical (tenant isolation, leg generation), High (migration).
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class PlanningFollowUpsPostgreSqlTests : PlanningPostgreSqlTestBase
{
    private const string Migration = "20261026013555_PlanningLegSourceKeys";
    private const string PreviousMigration = "20261025192744_PlanningLegsAndTrips";

    private static List<string> Cells(IRemoteStreamContent content)
    {
        using var workbook = new XLWorkbook(content.GetStream());
        return workbook.Worksheet(1).CellsUsed().Select(c => c.GetFormattedString()).ToList();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_export_and_the_picker_never_show_another_tenants_trips(bool ownDatabase)
    {
        var tenantId = ownDatabase ? TenantA : SharedTenant;
        var otherId = ownDatabase ? SharedTenant : TenantA;
        var name = "Export " + Guid.NewGuid().ToString("N")[..8];
        await InRolledBackTransactionAsync(tenantId, async () =>
        {
            var trip = await Operations.CreateAsync(new TransportTripCreateDto { TransportTripTypeId = await SeededTripTypeAsync(), PlannedDeparture = At(6), Name = name }, null, null, null);
            await FlushAsync();
            var service = GetRequiredService<ITransportTripAppService>();
            var columns = new List<ExportColumnDto> { new() { Field = "number" }, new() { Field = "name" } };
            var term = name.ToLowerInvariant();

            Cells(await service.ExportAsync(new TransportTripExportRequestDto { AllRows = true, Columns = columns })).ShouldContain(name);
            Cells(await service.ExportAsync(new TransportTripExportRequestDto { WildcardSearch = term, Columns = columns })).ShouldBe(["Number", "Name", trip.Number, name], ignoreOrder: true);
            (await service.GetPickerAsync(new TransportTripPickerRequestDto { StartRow = 0, EndRow = 100, WildcardSearch = term })).Rows.ShouldHaveSingleItem().Id.ShouldBe(trip.Id);

            /* The same transaction: switched to the other tenant, neither the export nor the picker shows the trip. */
            using (CurrentTenant.Change(otherId))
            {
                Cells(await service.ExportAsync(new TransportTripExportRequestDto { AllRows = true, Columns = columns })).ShouldNotContain(name);
                Cells(await service.ExportAsync(new TransportTripExportRequestDto { WildcardSearch = term, Columns = columns })).ShouldBe(["Number", "Name"], ignoreOrder: true);
                (await service.GetPickerAsync(new TransportTripPickerRequestDto { StartRow = 0, EndRow = 100, WildcardSearch = term })).Rows.ShouldBeEmpty();
                (await service.GetPickerAsync(new TransportTripPickerRequestDto { StartRow = 0, EndRow = 100 })).Rows.ShouldNotContain(r => r.Id == trip.Id);
            }
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_planners_edit_survives_a_regeneration_from_unchanged_stops_and_a_changed_stop_still_unassigns(bool ownDatabase)
    {
        var tenantId = ownDatabase ? TenantA : SharedTenant;
        await InRolledBackTransactionAsync(tenantId, async () =>
        {
            var depot = await PlaceAsync("DEPOT", "Depot", 52.09, 5.12, "Utrecht");
            var customer = await PlaceAsync("CUST", "Customer", 52.37, 4.90);
            var other = await PlaceAsync("OTHER", "Other", 52.5, 4.8);
            var orderId = (await OrderAsync(customer.RelationId,
                LineOf(10, 1000, StopAt(depot.RelationId, depot.AddressId, Load), StopAt(customer.RelationId, customer.AddressId, Unload)))).Id;
            var leg = (await ConfirmAsync(orderId)).Single();
            var trip = await CreateTripAsync(await SeededTripTypeAsync());
            await Operations.AssignAsync(trip, [leg], allowOutsideWindow: true, afterStopId: null, moving: false);
            await FlushAsync();

            var legs = GetRequiredService<ILegAppService>();
            var dto = await legs.GetAsync(leg.Id);
            var edited = await legs.UpdateAsync(leg.Id, new LegUpdateDto
            {
                OriginReference = "DOCK-7",
                OriginWindow = new LegTimeWindowDto { From = At(9), Till = At(9, 30) },
                ConcurrencyStamp = dto.ConcurrencyStamp
            });
            await FlushAsync();
            (edited.Origin.WindowFrom, edited.Origin.WindowTill).ShouldBe((At(9), At(9, 30)));

            /* Delivered again: the order's stops did not change, the leg keeps the planner's values and its trip. */
            await Handler.HandleEventAsync(new TransportOrderStopsChangedEto { OrderId = orderId });
            await FlushAsync();
            var kept = await LegRepository.GetAsync(leg.Id);
            (kept.TripId, kept.OriginReference, kept.OriginWindowFrom).ShouldBe((trip.Id, "DOCK-7", At(9)));
            kept.OriginSourceKey.ShouldNotBeNull();

            /* The clerk moves the destination: Planning takes the leg off the trip (design 59). */
            var order = await Orders.GetAsync(orderId);
            var line = order.Lines.Single();
            var input = new OpenTms.Orders.TransportOrders.TransportOrderLineInputDto
            {
                Id = line.Id, TransportUnitId = line.TransportUnitId, Quantity = line.Quantity, WeightKg = line.WeightKg,
                Stops = line.Stops.Select(s => new OpenTms.Orders.TransportOrders.TransportOrderStopInputDto
                {
                    Id = s.Id, WaypointTypeId = s.WaypointTypeId, RelationId = s.RelationId, RelationAddressId = s.RelationAddressId, Date = s.Date,
                    WindowFrom = s.WindowFrom, WindowTill = s.WindowTill, PlannedDurationMin = s.PlannedDurationMin
                }).ToList()
            };
            input.Stops![1].RelationId = other.RelationId;
            input.Stops[1].RelationAddressId = other.AddressId;
            await Orders.UpdateLineAsync(orderId, line.Id!.Value, input);
            await FlushAsync();
            await Handler.HandleEventAsync(new TransportOrderStopsChangedEto { OrderId = orderId });
            await FlushAsync();

            var moved = await LegRepository.GetAsync(leg.Id);
            moved.TripId.ShouldBeNull();
            moved.DestinationRelationId.ShouldBe(other.RelationId);
            moved.OriginReference.ShouldBe("DOCK-7", "the origin did not change at the order");
            moved.TenantId.ShouldBe(tenantId);
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("tenant-a")]
    public async Task The_migration_is_applied_last_and_nothing_is_pending(string? tenant)
    {
        await InRolledBackTransactionAsync(tenant == null ? null : TenantA, async () =>
        {
            var context = await GetRequiredService<IDbContextProvider<PlanningExecutionDbContext>>().GetDbContextAsync();
            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
            applied.Last().ShouldBe(Migration, "the newest Planning migration");
            applied.IndexOf(Migration).ShouldBe(applied.IndexOf(PreviousMigration) + 1);
            (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
            context.Database.HasPendingModelChanges().ShouldBeFalse();
        });
    }

    [Fact]
    public void The_migration_is_expand_only_and_its_Down_drops_exactly_the_two_columns()
    {
        var migration = new PlanningLegSourceKeys();
        migration.UpOperations.ShouldAllBe(o => o is AddColumnOperation);
        migration.UpOperations.OfType<AddColumnOperation>().Select(c => (c.Schema, c.Table, c.Name, c.IsNullable, c.ColumnType))
            .ShouldBe([("planning", "Legs", "OriginSourceKey", true, "uuid"), ("planning", "Legs", "DestinationSourceKey", true, "uuid")], ignoreOrder: true);
        migration.DownOperations.ShouldAllBe(o => o is DropColumnOperation);
        migration.DownOperations.OfType<DropColumnOperation>().Select(c => c.Name).ShouldBe(["OriginSourceKey", "DestinationSourceKey"], ignoreOrder: true);
    }
}
