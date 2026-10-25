using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Orders;
using OpenTms.Orders.Events;
using OpenTms.Orders.TransportOrders;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Orders;

/// <summary>
/// TransportOrderTemplateAppService and the materialisation runner on SQLite (design 58 "Templates and recurrence";
/// orders.md scenarios 48–53, 61): CRUD with the unique name, "new from template" with dates and copies (idempotent on
/// template + date + copy index), "copy order" through a transient template, "template from order", the inactive
/// refusal and the hourly job's run for a tenant. Risk class: High.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class TransportOrderTemplateAppServiceTests : TransportOrderTestBase<OpenTmsEntityFrameworkCoreTestModule>
{
    private ITransportOrderTemplateAppService Templates => GetRequiredService<ITransportOrderTemplateAppService>();

    private TransportOrderTemplateCreateDto TemplateDto(Fixture f, string name = "Weekly Venlo", TemplateScheduleDto? schedule = null) => new()
    {
        Name = name,
        TransportOrderTypeId = f.OrderTypeId,
        CustomerRelationId = f.CustomerId,
        CustomerReference = "WEEKLY",
        FinancialTerms = new TransportOrderFinancialTermsInputDto { CurrencyCode = "EUR", VatPercentageId = f.VatId, PriceListIds = [f.PriceListId] },
        Lines =
        [
            new TemplateLineInputDto
            {
                TransportUnitId = f.TransportUnitId, Quantity = 3, WeightKg = 120,
                Stops =
                [
                    new() { WaypointTypeId = f.LoadTypeId, RelationId = f.DepotId, RelationAddressId = f.DepotAddressId, DayOffset = 0, Reference = "L", TimeSlotId = f.TimeSlotId },
                    new() { WaypointTypeId = f.UnloadTypeId, RelationId = f.ShopId, RelationAddressId = f.ShopAddressId, DayOffset = 2, Reference = "U" }
                ]
            }
        ],
        References = [new TransportOrderReferenceDto { Type = "PO", Value = "W-1" }],
        TripRequest = new TemplateTripRequestInputDto
        {
            TransportTripTypeId = Guid.NewGuid(), DepartureRelationId = f.DepotId, DepartureAddressId = f.DepotAddressId, ArrivalRelationId = f.ShopId, ArrivalAddressId = f.ShopAddressId,
            DepartureDayOffset = 0, DepartureTime = new TimeOnly(6, 0), CarrierRelationId = f.CarrierId
        },
        Schedule = schedule ?? new TemplateScheduleDto { Recurrence = TemplateRecurrence.Weekly, Interval = 1, DaysOfWeek = (RecurrenceDays)127, HorizonDays = 3, ValidFrom = new DateOnly(2026, 1, 1) }
    };

    [Fact]
    public async Task A_template_saves_without_number_or_legs_and_lists_with_its_schedule_scenario_48()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();

        var created = await Templates.CreateAsync(TemplateDto(f));

        created.Name.ShouldBe("Weekly Venlo");
        created.CustomerName.ShouldBe("Klant Foods B.V.");
        created.Lines.ShouldHaveSingleItem().Stops.Select(s => s.DayOffset).ShouldBe([0, 2]);
        created.Lines[0].Stops[0].RelationName.ShouldBe("DC Venlo");
        created.Lines[0].TransportUnitName.ShouldBe("Europallet");
        created.TripRequest.ShouldNotBeNull().DepartureTime.ShouldBe(new TimeOnly(6, 0));
        created.Schedule.Recurrence.ShouldBe(TemplateRecurrence.Weekly);
        created.UpcomingDates.ShouldBe([Today, Today.AddDays(1), Today.AddDays(2)], "every day of the week, horizon 3");
        created.IsActive.ShouldBeTrue();

        var row = (await Templates.GetListAsync(new TransportOrderTemplateListRequestDto())).Rows.ShouldHaveSingleItem();
        row.LineCount.ShouldBe(1);
        row.HasTripRequest.ShouldBeTrue();
        row.Recurrence.ShouldBe(TemplateRecurrence.Weekly);
        (await Templates.GetLookupAsync("venlo")).ShouldHaveSingleItem().Name.ShouldBe("Weekly Venlo");

        (await Should.ThrowAsync<BusinessException>(() => Templates.CreateAsync(TemplateDto(f, "weekly venlo")))).Code.ShouldBe(ReferenceDataErrorCodes.DuplicateKey);
        var bad = TemplateDto(f, "Bad schedule", new TemplateScheduleDto { Recurrence = TemplateRecurrence.Monthly, DayOfMonth = null });
        (await Should.ThrowAsync<BusinessException>(() => Templates.CreateAsync(bad))).Code.ShouldBe(OrdersErrorCodes.InvalidSchedule);
        var unknownUnit = TemplateDto(f, "Unknown unit");
        unknownUnit.Lines[0].TransportUnitId = Guid.NewGuid();
        (await Should.ThrowAsync<BusinessException>(() => Templates.CreateAsync(unknownUnit))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
    }

    [Fact]
    public async Task Update_replaces_the_payload_and_delete_removes_the_template()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        var created = await Templates.CreateAsync(TemplateDto(f));

        var update = TemplateDto(f, "Weekly Venlo v2");
        update.Lines[0].Quantity = 9;
        update.TripRequest = null;
        update.IsActive = false;
        var updated = await Templates.UpdateAsync(created.Id, new TransportOrderTemplateUpdateDto
        {
            Name = update.Name, TransportOrderTypeId = update.TransportOrderTypeId, CustomerRelationId = update.CustomerRelationId, CustomerReference = update.CustomerReference,
            FinancialTerms = update.FinancialTerms, Lines = update.Lines, References = update.References, TripRequest = null, Schedule = update.Schedule, IsActive = false,
            ConcurrencyStamp = created.ConcurrencyStamp
        });
        updated.Name.ShouldBe("Weekly Venlo v2");
        updated.Lines.Single().Quantity.ShouldBe(9m);
        updated.TripRequest.ShouldBeNull();
        updated.IsActive.ShouldBeFalse();
        updated.UpcomingDates.ShouldBeEmpty("inactive");
        (await Templates.GetListAsync(new TransportOrderTemplateListRequestDto())).TotalCount.ShouldBe(0);
        (await Templates.GetListAsync(new TransportOrderTemplateListRequestDto { IncludeInactive = true })).TotalCount.ShouldBe(1);

        await Templates.DeleteAsync(created.Id);
        await Should.ThrowAsync<EntityNotFoundException>(() => Templates.GetAsync(created.Id));
    }

    [Fact]
    public async Task Materialise_creates_confirmed_orders_per_date_and_copy_once_scenario_49_51()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        var template = await Templates.CreateAsync(TemplateDto(f));
        var monday = new DateOnly(2026, 11, 2);

        var result = await Templates.MaterialiseAsync(template.Id, new MaterialiseTemplateDto { Dates = [monday, monday.AddDays(1)], Copies = 2 });

        result.CreatedOrderIds.Count.ShouldBe(4);
        result.SkippedCount.ShouldBe(0);
        var orders = new List<TransportOrderDto>();
        foreach (var id in result.CreatedOrderIds)
        {
            orders.Add(await Orders.GetAsync(id));
        }

        orders.ShouldAllBe(o => o.Status == TransportOrderStatus.Open && o.Number != null && o.Source == OrderSource.Template && o.SourceTemplateId == template.Id);
        orders.Select(o => (o.OrderDate, o.CopyIndex)).ShouldBe([(monday, 0), (monday, 1), (monday.AddDays(1), 0), (monday.AddDays(1), 1)], ignoreOrder: true);
        var first = orders.Single(o => o.OrderDate == monday && o.CopyIndex == 0);
        first.Lines.Single().Stops.Select(s => s.Date).ShouldBe([monday, monday.AddDays(2)]);
        first.Lines.Single().Stops[0].TimeSlotId.ShouldBe(f.TimeSlotId);
        first.TripRequest.ShouldNotBeNull().PlannedDeparture.ShouldBe(new DateTimeOffset(2026, 11, 2, 5, 0, 0, TimeSpan.Zero), "06:00 Amsterdam winter time, stored in UTC");
        first.References.Single().Value.ShouldBe("W-1");
        first.FinancialTerms.PriceListIds.ShouldBe([f.PriceListId]);
        Events.Of<TransportOrderConfirmedEto>(first.Id).ShouldHaveSingleItem();

        /* Again, with one more copy: only the third copies are new. */
        var again = await Templates.MaterialiseAsync(template.Id, new MaterialiseTemplateDto { Dates = [monday, monday.AddDays(1)], Copies = 3 });
        again.CreatedOrderIds.Count.ShouldBe(2);
        again.SkippedCount.ShouldBe(4);
        (await Templates.GetAsync(template.Id)).LastMaterialisedDate.ShouldBe(monday.AddDays(1));
        (await Orders.GetListAsync(new TransportOrderListRequestDto { SourceTemplateId = template.Id })).TotalCount.ShouldBe(6);

        (await Should.ThrowAsync<BusinessException>(() => Templates.MaterialiseAsync(template.Id, new MaterialiseTemplateDto { Dates = [], Copies = 1 }))).Code.ShouldBe(OrdersErrorCodes.MaterialisationInputInvalid);
    }

    [Fact]
    public async Task An_inactive_template_creates_nothing()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        var dto = TemplateDto(f);
        dto.IsActive = false;
        var template = await Templates.CreateAsync(dto);

        (await Should.ThrowAsync<BusinessException>(() => Templates.MaterialiseAsync(template.Id, new MaterialiseTemplateDto { Dates = [Today] }))).Code.ShouldBe(OrdersErrorCodes.TemplateInactive);
    }

    [Fact]
    public async Task Copy_order_makes_confirmed_copies_without_template_and_template_from_order_stores_one_scenarios_50_53()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        var original = await Orders.CreateAsync(CreateDto(f, confirm: true));

        var copies = await Templates.CopyOrderAsync(original.Id, new MaterialiseTemplateDto { Dates = [Today.AddDays(7)], Copies = 2 });
        copies.CreatedOrderIds.Count.ShouldBe(2);
        var copy = await Orders.GetAsync(copies.CreatedOrderIds[1]);
        copy.Status.ShouldBe(TransportOrderStatus.Open);
        copy.Source.ShouldBe(OrderSource.Screen);
        copy.SourceTemplateId.ShouldBeNull();
        copy.CopyIndex.ShouldBe(0, "copies without a template carry no copy index");
        copy.OrderDate.ShouldBe(Today.AddDays(7));
        copy.Lines.Single().Stops.Select(s => s.Date).ShouldBe([Today.AddDays(7), Today.AddDays(8)], "the day span of the original");
        copy.Number.ShouldNotBe(original.Number);

        var template = await Templates.CreateFromOrderAsync(original.Id);
        template.Name.ShouldBe($"Template of order {original.Number}");
        template.SourceOrderId.ShouldBe(original.Id);
        template.Lines.Single().Stops.Select(s => s.DayOffset).ShouldBe([0, 1]);
        template.Schedule.Recurrence.ShouldBe(TemplateRecurrence.None);
        var second = await Templates.CreateFromOrderAsync(original.Id);
        second.Name.ShouldBe($"Template of order {original.Number} (2)");
    }

    [Fact]
    public async Task The_hourly_run_creates_the_due_orders_of_every_active_recurring_template_once()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        var daily = await Templates.CreateAsync(TemplateDto(f, "Daily"));
        var never = TemplateDto(f, "Sundays only", new TemplateScheduleDto { Recurrence = TemplateRecurrence.Weekly, DaysOfWeek = TemplateSchedule.ToFlag(Today.AddDays(5).DayOfWeek), HorizonDays = 3, ValidFrom = new DateOnly(2026, 1, 1) });
        await Templates.CreateAsync(never);
        var manual = TemplateDto(f, "Manual");
        manual.Schedule = new TemplateScheduleDto { Recurrence = TemplateRecurrence.None };
        await Templates.CreateAsync(manual);

        var runner = GetRequiredService<TemplateMaterialisationRunner>();
        (await runner.RunForCurrentTenantAsync()).ShouldBe(3, "the daily template over its horizon of three days");
        (await runner.RunForCurrentTenantAsync()).ShouldBe(0, "the second run finds everything in place");

        var created = await Orders.GetListAsync(new TransportOrderListRequestDto { SourceTemplateId = daily.Id });
        created.Rows.Select(r => r.OrderDate).ShouldBe([Today, Today.AddDays(1), Today.AddDays(2)], ignoreOrder: true);
        created.Rows.ShouldAllBe(r => r.Status == TransportOrderStatus.Open && r.Source == OrderSource.Template);
        (await Templates.GetAsync(daily.Id)).LastMaterialisedDate.ShouldBe(Today.AddDays(2));
    }

    [Fact]
    public async Task Another_tenant_sees_no_templates()
    {
        Guid templateId;
        using (InTenantA())
        {
            var f = await FixtureAsync();
            templateId = (await Templates.CreateAsync(TemplateDto(f))).Id;
        }

        using (InTenantB())
        {
            (await Templates.GetListAsync(new TransportOrderTemplateListRequestDto { IncludeInactive = true })).TotalCount.ShouldBe(0);
            await Should.ThrowAsync<EntityNotFoundException>(() => Templates.GetAsync(templateId));
            await Should.ThrowAsync<EntityNotFoundException>(() => Templates.MaterialiseAsync(templateId, new MaterialiseTemplateDto { Dates = [Today] }));
            (await GetRequiredService<TemplateMaterialisationRunner>().RunForCurrentTenantAsync()).ShouldBe(0);
        }
    }
}
