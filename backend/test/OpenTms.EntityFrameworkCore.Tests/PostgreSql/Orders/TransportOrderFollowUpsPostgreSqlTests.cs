using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using OpenTms.Orders;
using OpenTms.Orders.EntityFrameworkCore;
using OpenTms.Orders.EntityFrameworkCore.Migrations;
using OpenTms.Orders.Events;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution.Events;
using Shouldly;
using Volo.Abp.Content;
using Volo.Abp.EntityFrameworkCore;
using Xunit;

namespace OpenTms.PostgreSql.Orders;

/// <summary>
/// The Transport follow-ups of the Orders module against real PostgreSQL, both tenant tiers: the list export never
/// carries another tenant's orders (UI-GRID-013; exports are a classic leak), the trip-request warning Planning reports
/// lands on the raising tenant's order only and once (design 59 build note 13), the trip request's freight items keep
/// "every line", a selection and an emptied selection apart in jsonb (design 61 point 7), and the OrdersTripRequestWarning
/// and OrdersTripRequestLines migrations (expand only; the latter newest). Writes are rolled back. Risk class: Critical (tenant isolation), High (migration).
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class TransportOrderFollowUpsPostgreSqlTests : TransportOrdersPostgreSqlTestBase
{
    private const string Migration = "20261026014344_OrdersTripRequestWarning";
    private const string PreviousMigration = "20261025200600_OrdersTransportOrders";
    private const string LinesMigration = "20261026223019_OrdersTripRequestLines";

    private static List<string> Cells(IRemoteStreamContent content)
    {
        using var workbook = new XLWorkbook(content.GetStream());
        return workbook.Worksheet(1).CellsUsed().Select(c => c.GetFormattedString()).ToList();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_export_never_carries_another_tenants_orders(bool ownDatabase)
    {
        var tenantId = ownDatabase ? TenantA : SharedTenant;
        var otherId = ownDatabase ? SharedTenant : TenantA;
        var reference = "EXPORT-" + Guid.NewGuid().ToString("N")[..8];
        await InRolledBackTransactionAsync(tenantId, async () =>
        {
            var f = await FixtureAsync();
            await Orders.CreateAsync(CreateDto(f, confirm: true, customerReference: reference));
            await FlushAsync();

            var columns = new List<ExportColumnDto> { new() { Field = "customerReference" }, new() { Field = "number" } };
            Cells(await Orders.ExportAsync(new TransportOrderExportRequestDto { AllRows = true, Columns = columns })).ShouldContain(reference);
            Cells(await Orders.ExportAsync(new TransportOrderExportRequestDto { WildcardSearch = reference.ToLowerInvariant(), Columns = columns })).ShouldContain(reference);

            /* The same transaction: switched to the other tenant, neither all rows nor a search finds the order. */
            using (CurrentTenant.Change(otherId))
            {
                Cells(await Orders.ExportAsync(new TransportOrderExportRequestDto { AllRows = true, Columns = columns })).ShouldNotContain(reference);
                Cells(await Orders.ExportAsync(new TransportOrderExportRequestDto { WildcardSearch = reference.ToLowerInvariant(), Columns = columns }))
                    .ShouldBe(["Customer reference", "Number"], ignoreOrder: true);
            }
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_trip_request_warning_lands_on_the_raising_tenants_order_once_and_never_on_another_tenants(bool ownDatabase)
    {
        var tenantId = ownDatabase ? TenantA : SharedTenant;
        var otherId = ownDatabase ? SharedTenant : TenantA;
        await InRolledBackTransactionAsync(tenantId, async () =>
        {
            var f = await FixtureAsync();
            var create = CreateDto(f);
            create.TripRequest = new TransportOrderTripRequestInputDto
            {
                TransportTripTypeId = Guid.NewGuid(), DepartureRelationId = f.DepotId, DepartureAddressId = f.DepotAddressId,
                ArrivalRelationId = f.ShopId, ArrivalAddressId = f.ShopAddressId, PlannedDeparture = DateTimeOffset.UtcNow
            };
            var order = await Orders.CreateAsync(create);
            await FlushAsync();
            var handlers = GetRequiredService<OrdersEventHandlers>();
            var eto = new TransportTripRequestNotHonouredEto { OrderId = order.Id, Field = "transportTripTypeId", Reason = "ReferenceNotFound" };

            /* The other tenant receives a fact with the same order id: it has no such order and writes nothing. */
            using (CurrentTenant.Change(otherId))
            {
                await handlers.HandleEventAsync(eto);
                await FlushAsync();
            }

            (await Orders.GetAsync(order.Id)).TripRequest.ShouldNotBeNull().WarningCode.ShouldBeNull();

            await handlers.HandleEventAsync(eto);
            await FlushAsync();
            var first = (await Orders.GetAsync(order.Id)).TripRequest.ShouldNotBeNull();
            (first.WarningCode, first.WarningField).ShouldBe((OrdersWarningCodes.TripRequestNotHonoured, "transportTripTypeId"));

            /* At least once: delivered again, the stored warning (and its time) is unchanged. */
            await handlers.HandleEventAsync(eto);
            await FlushAsync();
            (await Orders.GetAsync(order.Id)).TripRequest.ShouldNotBeNull().WarningAt.ShouldBe(first.WarningAt);
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("tenant-a")]
    public async Task The_migration_is_applied_in_order_and_adds_three_nullable_columns(string? tenant)
    {
        await InRolledBackTransactionAsync(tenant == null ? null : TenantA, async () =>
        {
            var context = await GetRequiredService<IDbContextProvider<OrdersDbContext>>().GetDbContextAsync();
            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
            applied.IndexOf(Migration).ShouldBe(applied.IndexOf(PreviousMigration) + 1);
            applied.IndexOf(LinesMigration).ShouldBe(applied.IndexOf(Migration) + 1, "OrdersTripRequestLines follows it");
            (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
            context.Database.HasPendingModelChanges().ShouldBeFalse();

            await using var command = new NpgsqlCommand(
                "SELECT column_name, data_type, COALESCE(character_maximum_length, 0), is_nullable FROM information_schema.columns WHERE table_schema = 'orders' AND table_name = 'TransportOrders' AND column_name LIKE 'TripRequest_Warning%'",
                (NpgsqlConnection)context.Database.GetDbConnection());
            command.Transaction = (NpgsqlTransaction?)context.Database.CurrentTransaction?.GetDbTransaction();
            await using var reader = await command.ExecuteReaderAsync();
            var columns = new List<(string, string, int, string)>();
            while (await reader.ReadAsync())
            {
                columns.Add((reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3)));
            }

            columns.ShouldBe(
            [
                ("TripRequest_WarningAt", "timestamp with time zone", 0, "YES"),
                ("TripRequest_WarningCode", "character varying", 64, "YES"),
                ("TripRequest_WarningField", "character varying", 64, "YES")
            ], ignoreOrder: true);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_trip_requests_freight_items_keep_every_line_a_selection_and_an_emptied_selection_apart(bool ownDatabase)
    {
        var tenantId = ownDatabase ? TenantA : SharedTenant;
        var otherId = ownDatabase ? SharedTenant : TenantA;
        await InRolledBackTransactionAsync(tenantId, async () =>
        {
            var f = await FixtureAsync();
            var create = CreateDto(f);
            create.Lines = [Run.Line(f), Run.Line(f, quantity: 3)];
            var order = await Orders.CreateAsync(create);
            await FlushAsync();
            var lines = order.Lines.OrderBy(l => l.Position).Select(l => l.Id!.Value).ToList();
            TransportOrderTripRequestInputDto Request(List<Guid>? lineIds) => new()
            {
                TransportTripTypeId = f.TripTypeId, DepartureRelationId = f.DepotId, DepartureAddressId = f.DepotAddressId,
                ArrivalRelationId = f.ShopId, ArrivalAddressId = f.ShopAddressId, PlannedDeparture = DateTimeOffset.UtcNow, LineIds = lineIds
            };

            var every = await Orders.UpdateTripRequestAsync(order.Id, new TransportOrderTripRequestUpdateDto { TripRequest = Request(null), ConcurrencyStamp = order.ConcurrencyStamp });
            await FlushAsync();
            (await StoredLineIdsAsync(order.Id)).ShouldBeNull("every line is SQL NULL");

            var selected = await Orders.UpdateTripRequestAsync(order.Id, new TransportOrderTripRequestUpdateDto { TripRequest = Request([lines[1]]), ConcurrencyStamp = every.ConcurrencyStamp });
            await FlushAsync();
            (await StoredLineIdsAsync(order.Id)).ShouldBe($"[\"{lines[1]}\"]");
            selected.TripRequest.ShouldNotBeNull().LineIds.ShouldBe([lines[1]]);

            await Orders.DeleteLineAsync(order.Id, lines[1]);
            await FlushAsync();
            (await StoredLineIdsAsync(order.Id)).ShouldBe("[]", "the emptied selection is an empty list, not NULL");
            var lookup = GetRequiredService<OpenTms.Orders.Published.ITransportOrderLookup>();
            (await lookup.GetTripRequestAsync(order.Id)).ShouldNotBeNull().LineIds.ShouldNotBeNull().ShouldBeEmpty();

            using (CurrentTenant.Change(otherId))
            {
                (await lookup.GetTripRequestAsync(order.Id)).ShouldBeNull("another tenant never reads the request");
            }
        });
    }

    /// <summary>The raw jsonb text of the order's trip request selection (null when SQL NULL), read in the test's transaction.</summary>
    private async Task<string?> StoredLineIdsAsync(Guid orderId)
    {
        var context = await GetRequiredService<IDbContextProvider<OrdersDbContext>>().GetDbContextAsync();
        await using var command = new NpgsqlCommand("SELECT \"TripRequest_LineIds\"::text FROM orders.\"TransportOrders\" WHERE \"Id\" = @id", (NpgsqlConnection)context.Database.GetDbConnection());
        command.Transaction = (NpgsqlTransaction?)context.Database.CurrentTransaction?.GetDbTransaction();
        command.Parameters.AddWithValue("id", orderId);
        var value = await command.ExecuteScalarAsync();
        return value is DBNull or null ? null : (string)value;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("tenant-a")]
    public async Task The_lines_migration_is_applied_last_and_adds_one_nullable_jsonb_column_per_trip_request(string? tenant)
    {
        await InRolledBackTransactionAsync(tenant == null ? null : TenantA, async () =>
        {
            var context = await GetRequiredService<IDbContextProvider<OrdersDbContext>>().GetDbContextAsync();
            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
            applied.Last().ShouldBe(LinesMigration, "the newest Orders migration");
            (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
            context.Database.HasPendingModelChanges().ShouldBeFalse();

            await using var command = new NpgsqlCommand(
                "SELECT table_name, data_type, is_nullable FROM information_schema.columns WHERE table_schema = 'orders' AND column_name = 'TripRequest_LineIds'",
                (NpgsqlConnection)context.Database.GetDbConnection());
            command.Transaction = (NpgsqlTransaction?)context.Database.CurrentTransaction?.GetDbTransaction();
            await using var reader = await command.ExecuteReaderAsync();
            var columns = new List<(string, string, string)>();
            while (await reader.ReadAsync())
            {
                columns.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }

            columns.ShouldBe([("TransportOrders", "jsonb", "YES"), ("TransportOrderTemplates", "jsonb", "YES")], ignoreOrder: true);
        });
    }

    [Fact]
    public void The_lines_migration_is_expand_only_and_its_Down_drops_exactly_its_columns()
    {
        var migration = new OrdersTripRequestLines();
        migration.UpOperations.ShouldAllBe(o => o is AddColumnOperation);
        migration.UpOperations.OfType<AddColumnOperation>().ShouldAllBe(c => c.Schema == "orders" && c.Name == "TripRequest_LineIds" && c.IsNullable && c.ColumnType == "jsonb");
        migration.UpOperations.OfType<AddColumnOperation>().Select(c => c.Table).ShouldBe(["TransportOrders", "TransportOrderTemplates"], ignoreOrder: true);
        migration.DownOperations.ShouldAllBe(o => o is DropColumnOperation);
        migration.DownOperations.OfType<DropColumnOperation>().Select(c => (c.Table, c.Name)).ShouldBe([("TransportOrders", "TripRequest_LineIds"), ("TransportOrderTemplates", "TripRequest_LineIds")], ignoreOrder: true);
    }

    [Fact]
    public void The_migration_is_expand_only_and_its_Down_drops_exactly_its_columns()
    {
        var migration = new OrdersTripRequestWarning();
        migration.UpOperations.ShouldAllBe(o => o is AddColumnOperation);
        migration.UpOperations.OfType<AddColumnOperation>().ShouldAllBe(c => c.Schema == "orders" && c.Table == "TransportOrders" && c.IsNullable);
        migration.UpOperations.OfType<AddColumnOperation>().Select(c => c.Name).ShouldBe(["TripRequest_WarningAt", "TripRequest_WarningCode", "TripRequest_WarningField"], ignoreOrder: true);
        migration.DownOperations.ShouldAllBe(o => o is DropColumnOperation);
        migration.DownOperations.OfType<DropColumnOperation>().Select(c => c.Name).ShouldBe(["TripRequest_WarningAt", "TripRequest_WarningCode", "TripRequest_WarningField"], ignoreOrder: true);
    }
}
