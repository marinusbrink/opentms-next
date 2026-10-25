using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Orders;
using OpenTms.Orders.ConsignmentNotes;
using OpenTms.Orders.Published;
using OpenTms.Orders.Seed;
using OpenTms.Orders.TransportOrders;
using OpenTms.Platform.Events.Outbox;
using OpenTms.Platform.Localization;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Guids;
using Volo.Abp.Uow;
using Xunit;

namespace OpenTms.PostgreSql.Orders;

/// <summary>
/// The transport order against real PostgreSQL (design 58, test risk analysis): the seeded consignment note profile of
/// both tenants and the idempotent seed, tenant isolation of orders, templates, profiles, documents and the two published
/// readers, the unique indexes as the truth, the gapless numbering on Confirm (a rolled-back confirm consumes nothing) and
/// the period lock, and the outbox: a rolled-back confirm leaves no event, a committed one has its facts in the tenant's
/// own outbox and is dispatched through the real dispatcher to Planning and Financial. Risk class: Critical.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class TransportOrdersPostgreSqlTests : TransportOrdersPostgreSqlTestBase
{
    private readonly IRepository<TransportOrder, Guid> _orders;
    private readonly IRepository<TransportOrderTemplate, Guid> _templates;
    private readonly IRepository<ConsignmentNoteProfile, Guid> _profiles;
    private readonly IRepository<ConsignmentNoteFixedDescription, Guid> _fixedDescriptions;
    private readonly IRepository<ConsignmentNoteDocument, Guid> _documents;
    private readonly IRepository<OrderAttributeDefinition, Guid> _definitions;
    private readonly OrdersDataSeedContributor _seed;
    private readonly IGuidGenerator _guids;

    public TransportOrdersPostgreSqlTests()
    {
        _orders = GetRequiredService<IRepository<TransportOrder, Guid>>();
        _templates = GetRequiredService<IRepository<TransportOrderTemplate, Guid>>();
        _profiles = GetRequiredService<IRepository<ConsignmentNoteProfile, Guid>>();
        _fixedDescriptions = GetRequiredService<IRepository<ConsignmentNoteFixedDescription, Guid>>();
        _documents = GetRequiredService<IRepository<ConsignmentNoteDocument, Guid>>();
        _definitions = GetRequiredService<IRepository<OrderAttributeDefinition, Guid>>();
        _seed = GetRequiredService<OrdersDataSeedContributor>();
        _guids = GetRequiredService<IGuidGenerator>();
    }

    private static LocalizedName Name(string en) => LocalizedName.Create(new Dictionary<string, string?> { ["en"] = en });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Every_tenant_has_the_default_consignment_note_profile_and_seeding_again_changes_nothing(bool ownDatabase)
    {
        var tenantId = ownDatabase ? TenantA : SharedTenant;
        await InRolledBackTransactionAsync(tenantId, async () =>
        {
            var standard = (await _profiles.GetListAsync()).ShouldHaveSingleItem();
            standard.Name.ShouldBe("Standaard");
            standard.IsDefault.ShouldBeTrue();
            standard.TenantId.ShouldBe(tenantId);
            (standard.Copies, standard.ShowBorders, standard.ShowQrCode, standard.GroupByLeg).ShouldBe((4, true, true, false));
            (standard.Box1Sender, standard.Box2Consignee, standard.Box3DeliveryPlace, standard.Box4TakingOverPlace).ShouldBe((CmrPartySource.Customer, CmrPartySource.DestinationAddress, CmrPartySource.DestinationAddress, CmrPartySource.OriginAddress));
            (standard.Box16Carrier, standard.Box17SuccessiveCarrier, standard.Box21EstablishedBy).ShouldBe((CmrPartySource.Carrier, CmrPartySource.Empty, CmrPartySource.OwnCompany));
            standard.Box6Columns.ShouldBe(ConsignmentNoteProfile.OpenTmsDefaultColumns);

            await _seed.SeedAsync(new DataSeedContext(tenantId));
            await FlushAsync();
            (await _profiles.GetCountAsync()).ShouldBe(1);
        });
    }

    [Fact]
    public async Task A_tenant_never_sees_another_tenants_orders_templates_profiles_documents_or_readers()
    {
        Guid orderId = Guid.Empty, templateId = Guid.Empty, documentId = Guid.Empty;
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var f = await FixtureAsync();
            var order = await Orders.CreateAsync(CreateDto(f, confirm: true));
            orderId = order.Id;
            templateId = (await GetRequiredService<ITransportOrderTemplateAppService>().CreateFromOrderAsync(order.Id)).Id;
            var profile = (await _profiles.GetListAsync()).Single();
            var document = new ConsignmentNoteDocument(_guids.Create(), TenantA, "CMR-TEST-000001", order.Id, order.Lines.Select(l => l.Id!.Value), null, profile.Id, Guid.NewGuid(),
                DateTime.UtcNow, null, Guid.NewGuid().ToString("N"), "application/pdf", "cmr.pdf", 3, new string('a', 64), "{}");
            await _documents.InsertAsync(document, autoSave: true);
            documentId = document.Id;

            (await Orders.GetListAsync(new TransportOrderListRequestDto())).Rows.Select(r => r.Id).ShouldContain(orderId);
            (await GetRequiredService<IOrderPricingReader>().GetSnapshotAsync(orderId)).ShouldNotBeNull();
            (await GetRequiredService<ITransportOrderLookup>().GetAsync([orderId])).ShouldHaveSingleItem();

            /* The children carry the tenant too. */
            (await GetRequiredService<IRepository<TransportOrderLine, Guid>>().GetListAsync(l => l.OrderId == orderId)).ShouldAllBe(l => l.TenantId == TenantA);
            (await GetRequiredService<IRepository<TransportOrderStop, Guid>>().GetListAsync()).Where(s => order.Lines.Any(l => l.Id == s.LineId)).ShouldAllBe(s => s.TenantId == TenantA);
        });

        await InRolledBackTransactionAsync(SharedTenant, async () =>
        {
            (await Orders.GetListAsync(new TransportOrderListRequestDto())).TotalCount.ShouldBe(0);
            await Should.ThrowAsync<EntityNotFoundException>(() => Orders.GetAsync(orderId));
            await Should.ThrowAsync<EntityNotFoundException>(() => GetRequiredService<ITransportOrderTemplateAppService>().GetAsync(templateId));
            await Should.ThrowAsync<EntityNotFoundException>(() => GetRequiredService<IConsignmentNoteAppService>().GetDocumentAsync(documentId));
            (await GetRequiredService<IOrderPricingReader>().GetSnapshotAsync(orderId)).ShouldBeNull();
            (await GetRequiredService<ITransportOrderLookup>().GetAsync([orderId])).ShouldBeEmpty();
            (await _profiles.GetListAsync()).ShouldAllBe(p => p.TenantId == SharedTenant);
            (await _orders.GetCountAsync()).ShouldBe(0);
        });
    }

    [Fact]
    public async Task Numbers_are_consecutive_a_refused_or_rolled_back_confirm_consumes_none_and_the_period_is_locked()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var f = await FixtureAsync();
            var first = await Orders.CreateAsync(CreateDto(f, confirm: true));
            var type = await GetRequiredService<IRepository<TransportOrderType, Guid>>().GetAsync(f.OrderTypeId);
            var start = long.Parse(first.Number![^5..]);

            /* A refused confirm (no stops) draws nothing. */
            var incomplete = CreateDto(f);
            incomplete.Lines[0].Stops!.Clear();
            var draft = await Orders.CreateAsync(incomplete);
            (await Should.ThrowAsync<BusinessException>(() => Orders.ConfirmAsync(draft.Id))).Code.ShouldBe(OrdersErrorCodes.LineNeedsTwoStops);

            var second = await Orders.CreateAsync(CreateDto(f, confirm: true));
            second.Number.ShouldBe(type.Numbering.Format(start + 1, Today));

            /* The period lock (orders.md 8): the year of a yearly numbered type. */
            var dto = new TransportOrderUpdateDto
            {
                TransportOrderTypeId = second.TransportOrderTypeId, CustomerRelationId = second.CustomerRelationId, OrderDate = new DateOnly(Today.Year + 1, 1, 1),
                CustomerReference = second.CustomerReference, ConcurrencyStamp = second.ConcurrencyStamp
            };
            (await Should.ThrowAsync<BusinessException>(() => Orders.UpdateAsync(second.Id, dto))).Code.ShouldBe(OrdersErrorCodes.OrderDatePeriodLocked);
        });

        /* Rolled back: the counter is where it was. The next confirm in a fresh transaction gets the same first number. */
        string? firstNumber = null;
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var f = await FixtureAsync();
            firstNumber = (await Orders.CreateAsync(CreateDto(f, confirm: true))).Number;
        });
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var f = await FixtureAsync();
            (await Orders.CreateAsync(CreateDto(f, confirm: true))).Number.ShouldBe(firstNumber, "gapless: the rolled-back confirm returned its number");
        });
    }

    [Fact]
    public async Task The_unique_indexes_are_the_truth()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var f = await FixtureAsync();
            var order = await Orders.CreateAsync(CreateDto(f, confirm: true));
            var header = new OrderHeaderInput(f.OrderTypeId, f.CustomerId, null, "Klant", Today, null, null, null, null);

            await ShouldViolateUniqueIndexAsync(async () =>
            {
                var twin = new TransportOrder(_guids.Create(), TenantA, header, TransportOrderFinancialTerms.Default(), OrderSource.Api, _guids.Create);
                twin.SetLines([new LineInput(null, new FreightInput(f.TransportUnitId, null, null, null, null, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, PaymentInstruction.NotSet, null, null, null, null, null, null), [], [],
                    [new StopInput(null, f.LoadTypeId, f.DepotId, f.DepotAddressId, null, Today, null, null, null, null, null, null), new StopInput(null, f.UnloadTypeId, f.ShopId, f.ShopAddressId, null, Today, null, null, null, null, null, null)], null)], _guids.Create);
                twin.Confirm(order.Number!, TransportOrderRequiredFields.None, _ => null);
                await _orders.InsertAsync(twin, autoSave: true);
            }, "IX_TransportOrders_TenantId_Number");
        });

        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var f = await FixtureAsync();
            var header = new OrderHeaderInput(f.OrderTypeId, f.CustomerId, null, "Klant", Today, null, null, null, null);
            var templateId = Guid.NewGuid();
            var first = new TransportOrder(_guids.Create(), TenantA, header, TransportOrderFinancialTerms.Default(), OrderSource.Template, _guids.Create);
            first.SetSourceTemplate(templateId, 0);
            await _orders.InsertAsync(first, autoSave: true);

            /* Two Drafts without number and two plain orders of one date are fine: the indexes filter them out. */
            await _orders.InsertAsync(new TransportOrder(_guids.Create(), TenantA, header, TransportOrderFinancialTerms.Default(), OrderSource.Screen, _guids.Create), autoSave: true);
            await _orders.InsertAsync(new TransportOrder(_guids.Create(), TenantA, header, TransportOrderFinancialTerms.Default(), OrderSource.Screen, _guids.Create), autoSave: true);

            /* The violating insert last: PostgreSQL aborts the transaction on it. */
            await ShouldViolateUniqueIndexAsync(async () =>
            {
                var twin = new TransportOrder(_guids.Create(), TenantA, header, TransportOrderFinancialTerms.Default(), OrderSource.Template, _guids.Create);
                twin.SetSourceTemplate(templateId, 0);
                await _orders.InsertAsync(twin, autoSave: true);
            }, "IX_TransportOrders_TenantId_Template_Date_Copy");
        });

        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var f = await FixtureAsync();
            var header = new OrderHeaderInput(f.OrderTypeId, f.CustomerId, null, "Klant", Today, null, null, null, null);
            await _templates.InsertAsync(new TransportOrderTemplate(_guids.Create(), TenantA, "Weekly", header, TransportOrderFinancialTerms.Default(), TemplateSchedule.None(), _guids.Create), autoSave: true);
            await ShouldViolateUniqueIndexAsync(
                () => _templates.InsertAsync(new TransportOrderTemplate(_guids.Create(), TenantA, " weekly ", header, TransportOrderFinancialTerms.Default(), TemplateSchedule.None(), _guids.Create), autoSave: true),
                "IX_TransportOrderTemplates_TenantId_NormalizedName");
        });

        await InRolledBackTransactionAsync(TenantA, () => ShouldViolateUniqueIndexAsync(
            () => _profiles.InsertAsync(new ConsignmentNoteProfile(_guids.Create(), TenantA, "standaard"), autoSave: true), "IX_ConsignmentNoteProfiles_TenantId_NormalizedName"));
        await InRolledBackTransactionAsync(TenantA, () => ShouldViolateUniqueIndexAsync(async () =>
        {
            var second = new ConsignmentNoteProfile(_guids.Create(), TenantA, "Second default");
            second.SetDefault(true);
            await _profiles.InsertAsync(second, autoSave: true);
        }, "IX_ConsignmentNoteProfiles_TenantId_Default"));
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            await _fixedDescriptions.InsertAsync(new ConsignmentNoteFixedDescription(_guids.Create(), TenantA, "Breekbaar", "Voorzichtig"), autoSave: true);
            await ShouldViolateUniqueIndexAsync(
                () => _fixedDescriptions.InsertAsync(new ConsignmentNoteFixedDescription(_guids.Create(), TenantA, "BREEKBAAR", "Anders"), autoSave: true),
                "IX_ConsignmentNoteFixedDescriptions_TenantId_NormalizedName");
        });
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            await _definitions.InsertAsync(new OrderAttributeDefinition(_guids.Create(), TenantA, OrderAttributeTarget.Order, "Project", Name("Project"), 1), autoSave: true);
            await _definitions.InsertAsync(new OrderAttributeDefinition(_guids.Create(), TenantA, OrderAttributeTarget.Line, "Project", Name("Project"), 1), autoSave: true);
            await ShouldViolateUniqueIndexAsync(
                () => _definitions.InsertAsync(new OrderAttributeDefinition(_guids.Create(), TenantA, OrderAttributeTarget.Order, "Project", Name("Again"), 2), autoSave: true),
                "IX_OrderAttributeDefinitions_TenantId_Target_Key");
        });
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var profile = (await _profiles.GetListAsync()).Single();
            ConsignmentNoteDocument Document(string number) => new(_guids.Create(), TenantA, number, Guid.NewGuid(), [Guid.NewGuid()], null, profile.Id, Guid.NewGuid(),
                DateTime.UtcNow, null, Guid.NewGuid().ToString("N"), "application/pdf", "cmr.pdf", 3, new string('b', 64), "{}");
            await _documents.InsertAsync(Document("CMR-UNIQUE-1"), autoSave: true);
            await ShouldViolateUniqueIndexAsync(() => _documents.InsertAsync(Document("CMR-UNIQUE-1"), autoSave: true), "IX_ConsignmentNoteDocuments_TenantId_Number");
        });
    }

    [Fact]
    public async Task A_rolled_back_confirm_leaves_no_event_and_a_committed_one_has_its_facts_in_the_tenants_outbox()
    {
        Guid rolledBack = Guid.Empty;
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var f = await FixtureAsync();
            rolledBack = (await Orders.CreateAsync(CreateDto(f, confirm: true))).Id;
            await FlushAsync();
        });
        (await PostgreSqlTestDatabases.CountOutboxRowsAsync(PostgreSqlTestDatabases.TenantADatabase, rolledBack.ToString())).ShouldBe(0);

        Guid committed;
        using (CurrentTenant.Change(TenantA))
        using (var unitOfWork = UnitOfWorkManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true))
        {
            var f = await FixtureAsync(uniqueNames: true);
            committed = (await Orders.CreateAsync(CreateDto(f, confirm: true))).Id;
            await unitOfWork.CompleteAsync();
        }

        try
        {
            var rows = await PostgreSqlTestDatabases.CountOutboxRowsAsync(PostgreSqlTestDatabases.TenantADatabase, committed.ToString());
            rows.ShouldBeGreaterThanOrEqualTo(3, "Confirmed, LinesChanged and StopsChanged (plus the derived-value request)");
            (await PostgreSqlTestDatabases.CountOutboxRowsAsync(PostgreSqlTestDatabases.HostDatabase, committed.ToString())).ShouldBe(0, "the tenant's own database, not the host's");
        }
        finally
        {
            /* Drain the outbox through the real dispatcher (its unit of work per event is what the handlers of Planning and
             * Financial run in), until the facts the handlers raise in turn are gone too, so later tests find it empty. */
            var dispatcher = GetRequiredService<IOutboxDispatcher>();
            while (await dispatcher.DispatchAsync(TenantA) > 0)
            {
            }

            (await PostgreSqlTestDatabases.CountOutboxRowsAsync(PostgreSqlTestDatabases.TenantADatabase, "")).ShouldBe(0, "every fact of the committed order was dispatched");
            GetRequiredService<OpenTms.TestDoubles.OrdersEventRecorder>().Of<OpenTms.Orders.Events.TransportOrderConfirmedEto>(committed).ShouldHaveSingleItem().ShouldNotBeNull();

            using (CurrentTenant.Change(TenantA))
            using (var cleanup = UnitOfWorkManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true))
            {
                /* Planning reacted to the dispatched facts: its legs of the order go with it. */
                (await GetRequiredService<IRepository<OpenTms.PlanningExecution.Legs.Leg, Guid>>().GetListAsync(l => l.OrderId == committed)).ShouldNotBeEmpty("Planning generated the legs from the dispatched confirm");
                await GetRequiredService<IRepository<OpenTms.PlanningExecution.Legs.Leg, Guid>>().HardDeleteAsync(l => l.OrderId == committed);
                var order = await _orders.FindAsync(committed, includeDetails: true);
                if (order != null)
                {
                    await _orders.HardDeleteAsync(order);
                }

                await cleanup.CompleteAsync();
            }
        }
    }
}
