using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using OpenTms.Orders;
using OpenTms.Orders.Export;
using OpenTms.Orders.TransportOrders;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Content;
using Volo.Abp.SettingManagement;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Orders;

/// <summary>
/// The order list's export on SQLite (UI-GRID-013, design 61 "Orders list … Export"): the requested columns in their
/// order with the client's headers (else the server's, in the UI culture), the list's filters and sort applied, all rows
/// on request, the unknown-column and row-cap refusals, and another tenant's orders never in the file. The file is read
/// back with an independent reader (ClosedXML). Risk class: High (list data); the tenant negative also runs on PostgreSQL.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class TransportOrderExportTests : TransportOrderTestBase<OpenTmsEntityFrameworkCoreTestModule>
{
    private static List<List<string>> Read(IRemoteStreamContent content, out XLWorkbook workbook)
    {
        content.ContentType.ShouldBe(XlsxTableWriter.ContentType);
        content.FileName.ShouldNotBeNull().ShouldEndWith(".xlsx");
        workbook = new XLWorkbook(content.GetStream());
        var sheet = workbook.Worksheet(1);
        var range = sheet.RangeUsed();
        if (range == null)
        {
            return [];
        }

        return range.Rows().Select(r => r.Cells(1, range.ColumnCount()).Select(c => c.GetFormattedString()).ToList()).ToList();
    }

    private static List<List<string>> Read(IRemoteStreamContent content)
    {
        var rows = Read(content, out var workbook);
        workbook.Dispose();
        return rows;
    }

    private async Task<List<TransportOrderDto>> ThreeOrdersAsync(OpenTms.TransportRun.TransportRunWorld f)
    {
        var a = await Orders.CreateAsync(CreateDto(f, confirm: true, customerReference: "REF-A"));
        var b = await Orders.CreateAsync(CreateDto(f, confirm: true, customerReference: "REF-B"));
        var draft = await Orders.CreateAsync(CreateDto(f, confirm: false, customerReference: "REF-DRAFT"));
        return [a, b, draft];
    }

    [Fact]
    public async Task Export_writes_the_requested_columns_with_the_client_headers_and_the_filtered_rows_in_list_order()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        var orders = await ThreeOrdersAsync(f);

        var file = await Orders.ExportAsync(new TransportOrderExportRequestDto
        {
            Status = [TransportOrderStatus.Open],
            SortBy = "customerReference",
            SortDir = "desc",
            Columns =
            [
                new ExportColumnDto { Field = "customerReference", Header = "Reference (PO)" },
                new ExportColumnDto { Field = "number" },
                new ExportColumnDto { Field = "status", Header = "State" },
                new ExportColumnDto { Field = "orderDate", Header = "Date" },
                new ExportColumnDto { Field = "customerName", Header = "Customer" }
            ]
        });

        var rows = Read(file, out var workbook);
        using (workbook)
        {
            rows[0].ShouldBe(["Reference (PO)", "Number", "State", "Date", "Customer"], "the client's headers in its order; a missing one localized by the server");
            rows.Count.ShouldBe(3, "the header and the two Open orders: the Draft is filtered out");
            rows[1][0].ShouldBe("REF-B", "sorted by the customer reference, descending");
            rows[2][0].ShouldBe("REF-A");
            rows[1][1].ShouldBe(orders[1].Number);
            rows[1][2].ShouldBe("Open", "the status localized as the grid shows it");
            rows[1][4].ShouldBe(f.CustomerName);

            var sheet = workbook.Worksheet(1);
            sheet.Cell(2, 4).DataType.ShouldBe(XLDataType.DateTime, "dates are real Excel dates");
            DateOnly.FromDateTime(sheet.Cell(2, 4).GetDateTime()).ShouldBe(Today);
            sheet.Cell(1, 1).Style.Font.Bold.ShouldBeTrue();
            sheet.AutoFilter.IsEnabled.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task All_rows_ignores_the_filters_and_the_search_and_no_columns_means_every_column_with_server_headers()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        await ThreeOrdersAsync(f);

        var rows = Read(await Orders.ExportAsync(new TransportOrderExportRequestDto
        {
            Status = [TransportOrderStatus.Draft],
            WildcardSearch = "matches-nothing",
            AllRows = true
        }));

        rows.Count.ShouldBe(4, "every order of the tenant");
        rows[0].Count.ShouldBe(TransportOrderExportColumns.All.Count);
        rows[0][0].ShouldBe("Number");
        rows.Skip(1).Select(r => r[TransportOrderExportColumns.All.ToList().FindIndex(c => c.Key == "customerReference")])
            .ShouldBe(["REF-A", "REF-B", "REF-DRAFT"], ignoreOrder: true);

        /* Without AllRows the same request finds nothing: only the header row is written. */
        Read(await Orders.ExportAsync(new TransportOrderExportRequestDto { WildcardSearch = "matches-nothing", Columns = [new ExportColumnDto { Field = "number" }] }))
            .ShouldHaveSingleItem().ShouldBe(["Number"]);
    }

    [Fact]
    public async Task The_server_headers_follow_the_ui_culture()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        await Orders.CreateAsync(CreateDto(f, confirm: true));

        using (UiCulture("nl"))
        {
            var rows = Read(await Orders.ExportAsync(new TransportOrderExportRequestDto { Columns = [new ExportColumnDto { Field = "number" }, new ExportColumnDto { Field = "customerName" }] }));
            rows[0].ShouldBe(["Nummer", "Klant"]);
        }
    }

    [Fact]
    public async Task An_unknown_column_and_more_rows_than_the_cap_are_refused()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        await ThreeOrdersAsync(f);

        var unknown = await Should.ThrowAsync<BusinessException>(() => Orders.ExportAsync(new TransportOrderExportRequestDto { Columns = [new ExportColumnDto { Field = "colour" }] }));
        unknown.Code.ShouldBe(OrdersErrorCodes.ExportUnknownColumn);
        unknown.Data["field"].ShouldBe("colour");
        (await Should.ThrowAsync<BusinessException>(() => Orders.ExportAsync(new TransportOrderExportRequestDto { SortBy = "colour" }))).Code
            .ShouldBe(OpenTms.Platform.ReferenceData.ReferenceDataErrorCodes.UnknownSortColumn);

        await GetRequiredService<ISettingManager>().SetForTenantAsync(TenantA, OrdersSettings.ExportMaxRows, "2");
        var tooMany = await Should.ThrowAsync<BusinessException>(() => Orders.ExportAsync(new TransportOrderExportRequestDto()));
        tooMany.Code.ShouldBe(OrdersErrorCodes.ExportTooManyRows);
        tooMany.Data["max"].ShouldBe(2);
        tooMany.Data["count"].ShouldBe(3L);

        /* Filtered to two rows, the same tenant exports again. */
        Read(await Orders.ExportAsync(new TransportOrderExportRequestDto { Status = [TransportOrderStatus.Open] })).Count.ShouldBe(3);
    }

    [Fact]
    public async Task Another_tenants_orders_are_never_in_the_file()
    {
        using (InTenantB())
        {
            var fb = await FixtureAsync();
            await Orders.CreateAsync(CreateDto(fb, confirm: true, customerReference: "TENANT-B"));
        }

        using var _ = InTenantA();
        var f = await FixtureAsync();
        var mine = await Orders.CreateAsync(CreateDto(f, confirm: true, customerReference: "TENANT-A"));

        var rows = Read(await Orders.ExportAsync(new TransportOrderExportRequestDto
        {
            AllRows = true,
            Columns = [new ExportColumnDto { Field = "number" }, new ExportColumnDto { Field = "customerReference" }]
        }));

        rows.Skip(1).ShouldHaveSingleItem().ShouldBe([mine.Number!, "TENANT-A"]);
        rows.SelectMany(r => r).ShouldNotContain("TENANT-B");
    }
}
