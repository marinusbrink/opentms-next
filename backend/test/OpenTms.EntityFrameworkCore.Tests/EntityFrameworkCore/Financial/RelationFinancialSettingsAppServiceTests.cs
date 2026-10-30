using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Financial;
using OpenTms.Financial.Invoicing;
using OpenTms.Financial.Tariffs;
using OpenTms.MasterData.Relations;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Financial;

/// <summary>
/// RelationFinancialSettingsAppService on SQLite (design 57): created on first edit, updated with the stamp, lists per
/// side checked against the list's side, defaults with validity, the list searched by relation name through the Master
/// Data lookup, tenant isolation. Risk class: Critical (tenant isolation), High (the upsert rules).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class RelationFinancialSettingsAppServiceTests : FinancialAppServiceTestBase
{
    private readonly IRelationFinancialSettingsAppService _service;
    private readonly IPriceListAppService _priceLists;
    private readonly IRelationAppService _relations;

    public RelationFinancialSettingsAppServiceTests()
    {
        _service = GetRequiredService<IRelationFinancialSettingsAppService>();
        _priceLists = GetRequiredService<IPriceListAppService>();
        _relations = GetRequiredService<IRelationAppService>();
    }

    private async Task<Guid> RelationAsync(string code, string name)
        => (await _relations.CreateAsync(new RelationCreateDto { Code = code, Name = name, Roles = RelationRoles.Customer | RelationRoles.Carrier })).Id;

    private async Task<Guid> ListAsync(string code, RateSide side)
        => (await _priceLists.CreateAsync(new PriceListCreateDto { Code = code, Name = code, Side = side, CurrencyCode = "EUR", ValidFrom = new DateOnly(2026, 1, 1) })).Id;

    private async Task<Guid> SurchargeAsync(string code)
        => (await GetRequiredService<IDiscountOrSurchargeAppService>().CreateAsync(new DiscountOrSurchargeCreateDto
        {
            Code = code,
            Name = code,
            ValueKind = DiscountOrSurchargeValueKind.Percentage,
            Values = [new DiscountOrSurchargeValueInputDto { ValidFrom = new DateOnly(2026, 1, 1), Value = 5m }]
        })).Id;

    [Fact]
    public async Task Upsert_creates_the_settings_on_the_first_edit_and_updates_them_with_the_stamp()
    {
        using var _ = InTenantA();
        await CurrencyAsync("EUR");
        var acme = await RelationAsync("ACME", "Acme Logistics");
        var revenue = await ListAsync("REV", RateSide.Revenue);
        var revenue2 = await ListAsync("REV2", RateSide.Revenue);
        var cost = await ListAsync("COST", RateSide.Cost);
        var fuel = await SurchargeAsync("FUEL");

        (await _service.GetByRelationAsync(acme)).ShouldBeNull();

        var created = await _service.UpsertByRelationAsync(acme, new RelationFinancialSettingsUpsertDto
        {
            RevenuePriceListIds = [revenue2, revenue],
            CostPriceListIds = [cost],
            RevenueMultipleMatches = MultipleMatches.HighestWins,
            DiscountsOrSurcharges = [new RelationDiscountOrSurchargeInputDto { DiscountOrSurchargeId = fuel, ValidFrom = new DateOnly(2026, 1, 1), ValidTill = new DateOnly(2026, 12, 31) }],
            PaymentTermDays = 30,
            ChargeFixedPriceByDefault = true,
            SendFreightDocumentsWithInvoice = true,
            CurrencyCode = "eur"
        });

        created.RelationId.ShouldBe(acme);
        created.RelationName.ShouldBe("Acme Logistics");
        created.RevenuePriceLists.Select(p => (p.Code, p.Position)).ShouldBe([("REV2", 1), ("REV", 2)]);
        created.CostPriceLists.Select(p => p.Code).ShouldBe(["COST"]);
        created.RevenueMultipleMatches.ShouldBe(MultipleMatches.HighestWins);
        created.CostMultipleMatches.ShouldBe(MultipleMatches.All);
        created.DiscountsOrSurcharges.ShouldHaveSingleItem().Name.ShouldBe("FUEL");
        created.PaymentTermDays.ShouldBe(30);
        created.CurrencyCode.ShouldBe("EUR");
        created.ChargeFixedPriceByDefault.ShouldBeTrue();

        var updated = await _service.UpsertByRelationAsync(acme, new RelationFinancialSettingsUpsertDto
        {
            RevenuePriceListIds = [revenue],
            PaymentTermDays = 14,
            ConcurrencyStamp = created.ConcurrencyStamp
        });
        updated.Id.ShouldBe(created.Id, "the same row");
        updated.RevenuePriceLists.Select(p => p.Code).ShouldBe(["REV"]);
        updated.CostPriceLists.ShouldBeEmpty();
        updated.DiscountsOrSurcharges.ShouldBeEmpty();
        updated.PaymentTermDays.ShouldBe(14);
        updated.CurrencyCode.ShouldBeNull();

        (await _service.GetByRelationAsync(acme))!.RevenuePriceLists.Single().PriceListId.ShouldBe(revenue);

        await Should.ThrowAsync<AbpDbConcurrencyException>(() => _service.UpsertByRelationAsync(acme, new RelationFinancialSettingsUpsertDto { ConcurrencyStamp = Guid.NewGuid().ToString("N") }));
        /* Also when the edit's references are checked (queries) between taking the stamp and saving. */
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => _service.UpsertByRelationAsync(acme, new RelationFinancialSettingsUpsertDto { RevenuePriceListIds = [revenue2], CurrencyCode = "EUR", ConcurrencyStamp = Guid.NewGuid().ToString("N") }));
    }

    [Fact]
    public async Task The_references_are_checked_the_list_side_included()
    {
        using var _ = InTenantA();
        await CurrencyAsync("EUR");
        var acme = await RelationAsync("ACME", "Acme");
        var cost = await ListAsync("COST", RateSide.Cost);

        (await Should.ThrowAsync<BusinessException>(() => _service.UpsertByRelationAsync(Guid.NewGuid(), new RelationFinancialSettingsUpsertDto()))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        (await Should.ThrowAsync<BusinessException>(() => _service.UpsertByRelationAsync(acme, new RelationFinancialSettingsUpsertDto { RevenuePriceListIds = [cost] }))).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
        (await Should.ThrowAsync<BusinessException>(() => _service.UpsertByRelationAsync(acme, new RelationFinancialSettingsUpsertDto { CostPriceListIds = [Guid.NewGuid()] }))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        (await Should.ThrowAsync<BusinessException>(() => _service.UpsertByRelationAsync(acme, new RelationFinancialSettingsUpsertDto { DiscountsOrSurcharges = [new RelationDiscountOrSurchargeInputDto { DiscountOrSurchargeId = Guid.NewGuid(), ValidFrom = Today }] }))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        (await Should.ThrowAsync<BusinessException>(() => _service.UpsertByRelationAsync(acme, new RelationFinancialSettingsUpsertDto { VatPercentageId = Guid.NewGuid() }))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        (await Should.ThrowAsync<BusinessException>(() => _service.UpsertByRelationAsync(acme, new RelationFinancialSettingsUpsertDto { CurrencyCode = "GBP" }))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        (await Should.ThrowAsync<BusinessException>(() => _service.UpsertByRelationAsync(acme, new RelationFinancialSettingsUpsertDto { CostPriceListIds = [cost, cost] }))).Code.ShouldBe(ReferenceDataErrorCodes.DuplicateKey);

        (await _service.GetByRelationAsync(acme)).ShouldBeNull("nothing was created by the refused edits");
    }

    [Fact]
    public async Task The_list_names_the_relations_counts_the_lists_and_searches_by_relation_name()
    {
        using var _ = InTenantA();
        await CurrencyAsync("EUR");
        var acme = await RelationAsync("ACME", "Acme Logistics");
        var zebra = await RelationAsync("ZEBRA", "Zebra Transport");
        var revenue = await ListAsync("REV", RateSide.Revenue);
        var cost = await ListAsync("COST", RateSide.Cost);
        await _service.UpsertByRelationAsync(zebra, new RelationFinancialSettingsUpsertDto { CostPriceListIds = [cost], PaymentTermDays = 60 });
        await _service.UpsertByRelationAsync(acme, new RelationFinancialSettingsUpsertDto { RevenuePriceListIds = [revenue], CurrencyCode = "EUR" });

        var all = await _service.GetListAsync(new ReferenceDataListRequestDto { StartRow = 0, EndRow = 100 });
        all.TotalCount.ShouldBe(2);
        all.Rows.Select(r => r.RelationName).ShouldBe(["Acme Logistics", "Zebra Transport"]);
        all.Rows[0].RevenuePriceListCount.ShouldBe(1);
        all.Rows[1].CostPriceListCount.ShouldBe(1);
        all.Rows[1].PaymentTermDays.ShouldBe(60);

        var searched = await _service.GetListAsync(new ReferenceDataListRequestDto { StartRow = 0, EndRow = 100, WildcardSearch = "zeb" });
        searched.Rows.Select(r => r.RelationId).ShouldBe([zebra]);
        searched.FilteredCount.ShouldBe(1);
        searched.TotalCount.ShouldBe(2);

        (await _service.GetListAsync(new ReferenceDataListRequestDto { StartRow = 0, EndRow = 100, SortBy = "paymenttermdays", SortDir = "desc" })).Rows.Select(r => r.RelationId).ShouldBe([zebra, acme]);
        (await Should.ThrowAsync<BusinessException>(() => _service.GetListAsync(new ReferenceDataListRequestDto { StartRow = 0, EndRow = 100, SortBy = "nope" }))).Code.ShouldBe(ReferenceDataErrorCodes.UnknownSortColumn);
    }

    [Fact]
    public async Task The_list_sorts_and_pages_by_relation_name_across_pages_in_the_query()
    {
        /* Review M2: the sort covers every row, not only the page (rows are created out of name order). */
        using var _ = InTenantA();
        var ids = new System.Collections.Generic.Dictionary<string, Guid>();
        var term = 30;
        foreach (var name in new[] { "Delta Freight", "Alpha Cargo", "Echo Haulage", "Charlie Express", "Bravo Lines" })
        {
            ids[name] = await RelationAsync(name.Split(' ')[0].ToUpperInvariant(), name);
            await _service.UpsertByRelationAsync(ids[name], new RelationFinancialSettingsUpsertDto { PaymentTermDays = term });
            term -= 5;
        }

        async Task<string[]> PageAsync(int start, int end, string? sortBy = null, string? sortDir = null, string? search = null)
            => (await _service.GetListAsync(new ReferenceDataListRequestDto { StartRow = start, EndRow = end, SortBy = sortBy, SortDir = sortDir, WildcardSearch = search }))
                .Rows.Select(r => r.RelationName).ToArray();

        (await PageAsync(0, 2)).ShouldBe(["Alpha Cargo", "Bravo Lines"]);
        (await PageAsync(2, 4)).ShouldBe(["Charlie Express", "Delta Freight"]);
        (await PageAsync(4, 6)).ShouldBe(["Echo Haulage"]);
        (await PageAsync(0, 2, "relationName", "desc")).ShouldBe(["Echo Haulage", "Delta Freight"]);
        (await PageAsync(2, 4, "relationName", "desc")).ShouldBe(["Charlie Express", "Bravo Lines"]);

        /* Payment terms 30, 25, 20, 15, 10 in creation order: Delta, Alpha, Echo, Charlie, Bravo. */
        (await PageAsync(0, 2, "paymentTermDays")).ShouldBe(["Bravo Lines", "Charlie Express"]);
        (await PageAsync(2, 4, "paymentTermDays")).ShouldBe(["Echo Haulage", "Alpha Cargo"]);
        (await PageAsync(4, 6, "paymentTermDays")).ShouldBe(["Delta Freight"]);
    }

    [Fact]
    public async Task The_search_filters_every_row_in_the_query_and_pages_the_matches()
    {
        using var _ = InTenantA();
        foreach (var name in new[] { "Northern Haulage", "Haulage South", "Acme", "West Haulage" })
        {
            await _service.UpsertByRelationAsync(await RelationAsync(name.Replace(" ", string.Empty).ToUpperInvariant(), name), new RelationFinancialSettingsUpsertDto());
        }

        var first = await _service.GetListAsync(new ReferenceDataListRequestDto { StartRow = 0, EndRow = 2, WildcardSearch = "HAULAGE" });
        first.TotalCount.ShouldBe(4);
        first.FilteredCount.ShouldBe(3, "case-insensitive, anywhere in the name");
        first.Rows.Select(r => r.RelationName).ShouldBe(["Haulage South", "Northern Haulage"]);

        var second = await _service.GetListAsync(new ReferenceDataListRequestDto { StartRow = 2, EndRow = 4, WildcardSearch = "haulage" });
        second.Rows.Select(r => r.RelationName).ShouldBe(["West Haulage"]);
        second.FilteredCount.ShouldBe(3);

        (await _service.GetListAsync(new ReferenceDataListRequestDto { StartRow = 0, EndRow = 10, WildcardSearch = "nothing like it" })).Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_renamed_relation_shows_its_live_name_and_sorts_under_it_once_its_settings_are_saved_again()
    {
        using var _ = InTenantA();
        var first = await RelationAsync("FIRST", "Aardvark Transport");
        var second = await RelationAsync("SECOND", "Mole Logistics");
        await _service.UpsertByRelationAsync(first, new RelationFinancialSettingsUpsertDto());
        await _service.UpsertByRelationAsync(second, new RelationFinancialSettingsUpsertDto());

        /* A rename in Master Data (straight on its aggregate: the relation editor's DTO is not the subject here). */
        var relations = GetRequiredService<Volo.Abp.Domain.Repositories.IRepository<OpenTms.MasterData.Relations.Relation, Guid>>();
        await WithUnitOfWorkAsync(async () =>
        {
            var relation = await relations.GetAsync(first);
            relation.SetName("Zebra Transport", relation.FullName);
            await relations.UpdateAsync(relation, autoSave: true);
        });

        var stale = await _service.GetListAsync(new ReferenceDataListRequestDto { StartRow = 0, EndRow = 10 });
        stale.Rows.Select(r => r.RelationName).ShouldBe(["Zebra Transport", "Mole Logistics"], "the live name is shown; the order follows the stored copy until the next save");

        await _service.UpsertByRelationAsync(first, new RelationFinancialSettingsUpsertDto());
        (await _service.GetListAsync(new ReferenceDataListRequestDto { StartRow = 0, EndRow = 10 })).Rows.Select(r => r.RelationName).ShouldBe(["Mole Logistics", "Zebra Transport"]);
    }

    [Fact]
    public async Task Another_tenant_sees_nothing()
    {
        Guid acme;
        using (InTenantA())
        {
            await CurrencyAsync("EUR");
            acme = await RelationAsync("ACME", "Acme");
            await _service.UpsertByRelationAsync(acme, new RelationFinancialSettingsUpsertDto { PaymentTermDays = 30 });
        }

        using (InTenantB())
        {
            (await _service.GetListAsync(new ReferenceDataListRequestDto { StartRow = 0, EndRow = 100 })).Rows.ShouldBeEmpty();
            (await _service.GetByRelationAsync(acme)).ShouldBeNull();
            (await Should.ThrowAsync<BusinessException>(() => _service.UpsertByRelationAsync(acme, new RelationFinancialSettingsUpsertDto()))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound, "the relation is tenant A's");
        }
    }
}
