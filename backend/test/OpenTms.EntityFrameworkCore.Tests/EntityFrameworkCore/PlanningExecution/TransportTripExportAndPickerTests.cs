using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using OpenTms.MasterData.Relations;
using OpenTms.PlanningExecution;
using OpenTms.PlanningExecution.Export;
using OpenTms.PlanningExecution.Settings;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Content;
using Volo.Abp.SettingManagement;
using Xunit;

namespace OpenTms.EntityFrameworkCore.PlanningExecution;

/// <summary>
/// The trip list's export (UI-GRID-013, design 61 "Trips list … Export") and the trip picker of "Outsource origin"
/// (design 61) on SQLite: columns in the client's order and headers, the list's filters and sort, all rows, values as
/// the grid shows them (times in the tenant's zone, km, percentages, joined lists), the refusals, and another tenant's
/// trips never in the file nor in the picker. Risk class: High; the tenant negatives also run on PostgreSQL.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class TransportTripExportAndPickerTests : PlanningAppServiceTestBase
{
    private static List<List<string>> Read(IRemoteStreamContent content, out XLWorkbook workbook)
    {
        content.ContentType.ShouldBe(XlsxTableWriter.ContentType);
        content.FileName.ShouldNotBeNull().ShouldStartWith("transport-trips-");
        workbook = new XLWorkbook(content.GetStream());
        var range = workbook.Worksheet(1).RangeUsed()!;
        return range.Rows().Select(r => r.Cells(1, range.ColumnCount()).Select(c => c.GetFormattedString()).ToList()).ToList();
    }

    private static List<List<string>> Read(IRemoteStreamContent content)
    {
        var rows = Read(content, out var workbook);
        workbook.Dispose();
        return rows;
    }

    private async Task<(TransportTripDto Alpha, TransportTripDto Beta, TransportTripDto Gamma, Guid CarrierId)> TripsAsync()
    {
        var type = await TripTypeAsync();
        var depot = await PlaceAsync("DEPOT", "Depot Utrecht", 52.09, 5.12, RelationRoles.Depot | RelationRoles.OwnCompany, "Utrecht");
        var customer = await PlaceAsync("CUSTA", "Customer A", 52.37, 4.90, RelationRoles.Customer, "Amsterdam");
        var carrier = await PlaceAsync("CARR", "Carrier BV", 52, 5, RelationRoles.Carrier);
        var orderId = await ConfirmedOrderAsync(customer.RelationId, LineOf(10, 1000,
            StopAt(depot, Load, Day, At(8), At(10)), StopAt(customer, Unload, Day, At(12), At(14))));

        var alpha = await CreateTripAsync(type, At(6), At(18), name: "Alpha");
        await Trips.AssignLegsAsync(alpha.Id, new AssignLegsDto { LegIds = await LegIdsOfOrderAsync(orderId), AllowOutsideWindow = true });
        var beta = await CreateTripAsync(type, At(6, 0, 27), name: "Beta");
        await Trips.SetCarrierAsync(beta.Id, new TripCarrierDto { CarrierRelationId = carrier.RelationId });
        var gamma = await CreateTripAsync(type, At(6, 0, 28), name: "Gamma");
        await Trips.CancelAsync(gamma.Id);
        return (await Trips.GetAsync(alpha.Id), await Trips.GetAsync(beta.Id), await Trips.GetAsync(gamma.Id), carrier.RelationId);
    }

    [Fact]
    public async Task Export_writes_the_requested_columns_with_the_client_headers_the_filtered_rows_in_list_order_and_grid_values()
    {
        using var _ = InTenantA();
        var (alpha, beta, _, _) = await TripsAsync();

        var file = await Trips.ExportAsync(new TransportTripExportRequestDto
        {
            View = "open",
            SortBy = "name",
            SortDir = "desc",
            Columns =
            [
                new ExportColumnDto { Field = "name", Header = "Trip name" },
                new ExportColumnDto { Field = "number" },
                new ExportColumnDto { Field = "status" },
                new ExportColumnDto { Field = "carrierName", Header = "Carrier" },
                new ExportColumnDto { Field = "plannedDeparture", Header = "Departure" },
                new ExportColumnDto { Field = "route", Header = "Route" },
                new ExportColumnDto { Field = "orders", Header = "Orders" },
                new ExportColumnDto { Field = "plannedDistanceM" }
            ]
        });

        var rows = Read(file, out var workbook);
        using (workbook)
        {
            rows[0].ShouldBe(["Trip name", "Number", "Status", "Carrier", "Departure", "Route", "Orders", "Distance (km)"]);
            rows.Count.ShouldBe(3, "the header and the two open trips: the cancelled one is filtered out");
            rows[1][0].ShouldBe("Beta", "sorted by name, descending");
            rows[2][0].ShouldBe("Alpha");
            rows[1][1].ShouldBe(beta.Number);
            rows[1][2].ShouldBe("Planned");
            rows[1][3].ShouldBe("Carrier BV");
            rows[2][5].ShouldBe("Utrecht → Amsterdam");
            rows[2][6].ShouldNotBeNullOrEmpty();

            var sheet = workbook.Worksheet(1);
            sheet.Cell(3, 5).DataType.ShouldBe(XLDataType.DateTime);
            var zone = GetRequiredService<PlanningExecutionClock>().Zone;
            sheet.Cell(3, 5).GetDateTime().ShouldBe(TimeZoneInfo.ConvertTime(alpha.PlannedDeparture, zone).DateTime, "the departure in the tenant's zone");
        }
    }

    [Fact]
    public async Task All_rows_ignores_the_filters_but_not_the_archived_state_and_no_columns_means_every_column()
    {
        using var _ = InTenantA();
        await TripsAsync();

        var rows = Read(await Trips.ExportAsync(new TransportTripExportRequestDto { View = "today", WildcardSearch = "matches-nothing", AllRows = true }));
        rows.Count.ShouldBe(4, "all three trips");
        rows[0].Count.ShouldBe(TransportTripExportColumns.All.Count);
        rows[0][0].ShouldBe("Number");
        rows.Skip(1).Select(r => r[TransportTripExportColumns.All.ToList().FindIndex(c => c.Key == "name")]).ShouldBe(["Alpha", "Beta", "Gamma"], ignoreOrder: true);

        Read(await Trips.ExportAsync(new TransportTripExportRequestDto { AllRows = true, IsArchived = true, Columns = [new ExportColumnDto { Field = "number" }] }))
            .ShouldHaveSingleItem("no archived trips: only the header");
    }

    [Fact]
    public async Task An_unknown_column_and_more_rows_than_the_cap_are_refused()
    {
        using var _ = InTenantA();
        await TripsAsync();

        var unknown = await Should.ThrowAsync<BusinessException>(() => Trips.ExportAsync(new TransportTripExportRequestDto { Columns = [new ExportColumnDto { Field = "colour" }] }));
        unknown.Code.ShouldBe(PlanningExecutionErrorCodes.ExportUnknownColumn);
        unknown.Data["field"].ShouldBe("colour");

        await GetRequiredService<ISettingManager>().SetForTenantAsync(TenantA, PlanningExecutionSettings.ExportMaxRows, "2");
        var tooMany = await Should.ThrowAsync<BusinessException>(() => Trips.ExportAsync(new TransportTripExportRequestDto()));
        tooMany.Code.ShouldBe(PlanningExecutionErrorCodes.ExportTooManyRows);
        tooMany.Data["max"].ShouldBe(2);
        tooMany.Data["count"].ShouldBe(3L);
        Read(await Trips.ExportAsync(new TransportTripExportRequestDto { View = "open" })).Count.ShouldBe(3);
    }

    [Fact]
    public async Task The_picker_offers_planned_and_in_progress_trips_by_default_filters_by_date_and_search_and_leaves_one_out()
    {
        using var _ = InTenantA();
        var (alpha, beta, gamma, carrierId) = await TripsAsync();
        await Trips.StartAsync(alpha.Id, new TripTimestampDto { At = At(6, 5) });

        var all = await Trips.GetPickerAsync(new TransportTripPickerRequestDto());
        all.Rows.Select(r => r.Name).ShouldBe(["Alpha", "Beta"], "Planned and InProgress, by date; the cancelled trip is not offered");
        all.TotalCount.ShouldBe(2);
        var betaRow = all.Rows.Single(r => r.Id == beta.Id);
        (betaRow.Number, betaRow.TripDate, betaRow.Status, betaRow.CarrierRelationId, betaRow.CarrierName).ShouldBe((beta.Number, beta.TripDate, TransportTripStatus.Planned, carrierId, "Carrier BV"));
        all.Rows.Single(r => r.Id == alpha.Id).Status.ShouldBe(TransportTripStatus.InProgress);

        (await Trips.GetPickerAsync(new TransportTripPickerRequestDto { ExcludeTripId = alpha.Id })).Rows.ShouldHaveSingleItem().Id.ShouldBe(beta.Id);
        (await Trips.GetPickerAsync(new TransportTripPickerRequestDto { DateFrom = Day.AddDays(1) })).Rows.ShouldHaveSingleItem().Id.ShouldBe(beta.Id);
        (await Trips.GetPickerAsync(new TransportTripPickerRequestDto { DateTo = Day })).Rows.ShouldHaveSingleItem().Id.ShouldBe(alpha.Id);
        var searched = await Trips.GetPickerAsync(new TransportTripPickerRequestDto { WildcardSearch = beta.Number.ToLowerInvariant() });
        searched.Rows.ShouldHaveSingleItem().Id.ShouldBe(beta.Id);
        searched.FilteredCount.ShouldBe(1);
        (await Trips.GetPickerAsync(new TransportTripPickerRequestDto { WildcardSearch = "Carrier" })).Rows.ShouldHaveSingleItem().Id.ShouldBe(beta.Id);
        (await Trips.GetPickerAsync(new TransportTripPickerRequestDto { Status = [TransportTripStatus.Cancelled] })).Rows.ShouldHaveSingleItem().Id.ShouldBe(gamma.Id);
        (await Trips.GetPickerAsync(new TransportTripPickerRequestDto { StartRow = 1, EndRow = 2 })).Rows.ShouldHaveSingleItem().Id.ShouldBe(beta.Id, "paged");
        (await Should.ThrowAsync<BusinessException>(() => Trips.GetPickerAsync(new TransportTripPickerRequestDto { SortBy = "nope" }))).Code.ShouldBe(ReferenceDataErrorCodes.UnknownSortColumn);
    }

    [Fact]
    public async Task Another_tenants_trips_are_never_in_the_export_or_the_picker()
    {
        using (InTenantB())
        {
            var otherType = await TripTypeAsync();
            await CreateTripAsync(otherType, At(6), name: "Tenant B trip");
        }

        using var _ = InTenantA();
        var type = await TripTypeAsync();
        var mine = await CreateTripAsync(type, At(6), name: "Tenant A trip");

        var rows = Read(await Trips.ExportAsync(new TransportTripExportRequestDto { AllRows = true, Columns = [new ExportColumnDto { Field = "name" }] }));
        rows.Skip(1).ShouldHaveSingleItem().ShouldBe(["Tenant A trip"]);
        (await Trips.GetPickerAsync(new TransportTripPickerRequestDto())).Rows.ShouldHaveSingleItem().Id.ShouldBe(mine.Id);
        (await Trips.GetPickerAsync(new TransportTripPickerRequestDto { WildcardSearch = "tenant b" })).Rows.ShouldBeEmpty();
    }
}
