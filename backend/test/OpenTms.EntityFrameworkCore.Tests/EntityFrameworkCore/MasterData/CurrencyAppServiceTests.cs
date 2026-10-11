using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData;
using OpenTms.MasterData.Reference;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Validation;
using Xunit;

namespace OpenTms.EntityFrameworkCore.MasterData;

/// <summary>
/// CurrencyAppService on SQLite (design 41). Risk class: Critical for tenant isolation, uniqueness
/// and the single default; High for the CRUD, concurrency, in-use and bulk-delete rules.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class CurrencyAppServiceTests : MasterDataAppServiceTestBase
{
    private readonly ICurrencyAppService _service;
    private readonly ICountryAppService _countries;

    public CurrencyAppServiceTests()
    {
        _service = GetRequiredService<ICurrencyAppService>();
        _countries = GetRequiredService<ICountryAppService>();
    }

    private Task<CurrencyDto> CreateAsync(string code, string en, string? nl = null, bool active = true, string? symbol = null)
        => _service.CreateAsync(new CurrencyCreateDto { IsoCode = code, Names = Names(en, nl), IsActive = active, Symbol = symbol, IsoNumeric = 1, MinorUnits = 2 });

    private static CurrencyUpdateDto UpdateFrom(CurrencyDto dto) => new()
    {
        IsoCode = dto.IsoCode,
        IsoNumeric = dto.IsoNumeric,
        MinorUnits = dto.MinorUnits,
        Symbol = dto.Symbol,
        Names = Copy(dto.Names),
        Description = dto.Description,
        IsActive = dto.IsActive,
        ConcurrencyStamp = dto.ConcurrencyStamp
    };

    // ── Create / get / list ───────────────────────────────────────────────────

    [Fact]
    public async Task Create_normalises_the_code_resolves_the_name_and_get_returns_the_detail()
    {
        using var _ = InTenantA();
        using var culture = UiCulture("nl-NL");

        var created = await _service.CreateAsync(new CurrencyCreateDto
        {
            IsoCode = "eur",
            IsoNumeric = 978,
            MinorUnits = 2,
            Symbol = " € ",
            Names = Names("Euro (en)", "Euro (nl)"),
            Description = "  The euro  ",
            IsActive = true
        });

        created.Id.ShouldNotBe(Guid.Empty);
        created.IsoCode.ShouldBe("EUR");
        created.Symbol.ShouldBe("€");
        created.Name.ShouldBe("Euro (nl)");
        created.Names.ShouldBe(new LocalizedNameDto { ["en"] = "Euro (en)", ["nl"] = "Euro (nl)" });
        created.Description.ShouldBe("The euro");
        created.IsActive.ShouldBeTrue();
        created.IsDefault.ShouldBeFalse();
        created.ConcurrencyStamp.ShouldNotBeNullOrEmpty();
        created.CreationTime.ShouldBeGreaterThan(DateTime.MinValue);

        var fetched = await _service.GetAsync(created.Id);
        fetched.IsoCode.ShouldBe("EUR");
        fetched.Names.ShouldBe(created.Names);
        fetched.ConcurrencyStamp.ShouldBe(created.ConcurrencyStamp);

        var page = await _service.GetListAsync(List<CurrencyListRequestDto>());
        page.TotalCount.ShouldBe(1);
        page.FilteredCount.ShouldBe(1);
        var row = page.Rows.ShouldHaveSingleItem();
        row.Id.ShouldBe(created.Id);
        row.Name.ShouldBe("Euro (nl)");
        row.IsoNumeric.ShouldBe(978);
        row.MinorUnits.ShouldBe(2);
    }

    [Fact]
    public async Task Get_of_an_unknown_id_is_not_found()
    {
        using var _ = InTenantA();

        await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task The_create_dto_is_validated_by_annotation_before_the_service_runs()
    {
        using var _ = InTenantA();

        await Should.ThrowAsync<AbpValidationException>(() => CreateAsync("EURO", "Euro"));
        await Should.ThrowAsync<AbpValidationException>(() => _service.CreateAsync(new CurrencyCreateDto { IsoCode = "EUR", MinorUnits = 9, Names = Names("Euro") }));
        await Should.ThrowAsync<AbpValidationException>(() => _service.GetListAsync(List<CurrencyListRequestDto>(start: 10, end: 10)));
        await Should.ThrowAsync<AbpValidationException>(() => _service.GetListAsync(List<CurrencyListRequestDto>(sortDir: "sideways")));
    }

    [Fact]
    public async Task A_name_without_the_default_culture_text_is_refused()
    {
        using var _ = InTenantA();

        var ex = await Should.ThrowAsync<BusinessException>(() => _service.CreateAsync(new CurrencyCreateDto { IsoCode = "EUR", Names = new LocalizedNameDto { ["nl"] = "Euro" } }));
        ex.Code.ShouldBe(MasterDataErrorCodes.NameRequired);

        ex = await Should.ThrowAsync<BusinessException>(() => _service.CreateAsync(new CurrencyCreateDto { IsoCode = "EUR", Names = new LocalizedNameDto { ["en"] = "   " } }));
        ex.Code.ShouldBe(MasterDataErrorCodes.NameRequired);
    }

    [Fact]
    public async Task List_pages_counts_and_filters_on_activity()
    {
        using var _ = InTenantA();
        await CreateAsync("AAA", "Alpha");
        await CreateAsync("BBB", "Bravo");
        await CreateAsync("CCC", "Charlie", active: false);
        await CreateAsync("DDD", "Delta");

        var all = await _service.GetListAsync(List<CurrencyListRequestDto>(sortBy: "isoCode"));
        all.TotalCount.ShouldBe(4);
        all.Rows.Select(r => r.IsoCode).ShouldBe(["AAA", "BBB", "CCC", "DDD"]);

        var second = await _service.GetListAsync(List<CurrencyListRequestDto>(start: 1, end: 3, sortBy: "isoCode"));
        second.TotalCount.ShouldBe(4);
        second.FilteredCount.ShouldBe(4);
        second.Rows.Select(r => r.IsoCode).ShouldBe(["BBB", "CCC"]);

        var active = await _service.GetListAsync(new CurrencyListRequestDto { IsActive = true, SortBy = "isoCode" });
        active.TotalCount.ShouldBe(3);
        active.Rows.ShouldAllBe(r => r.IsActive);

        var inactive = await _service.GetListAsync(new CurrencyListRequestDto { IsActive = false });
        inactive.Rows.ShouldHaveSingleItem().IsoCode.ShouldBe("CCC");

        var searched = await _service.GetListAsync(new CurrencyListRequestDto { IsActive = true, WildcardSearch = "bb" });
        searched.TotalCount.ShouldBe(3);
        searched.FilteredCount.ShouldBe(1);
        searched.Rows.ShouldHaveSingleItem().IsoCode.ShouldBe("BBB");
    }

    [Fact]
    public async Task Search_covers_the_code_the_ui_language_name_and_the_default_culture_name()
    {
        using var _ = InTenantA();
        await CreateAsync("EUR", "Euro", "Euro");
        await CreateAsync("GBP", "Pound sterling", "Brits pond");
        await CreateAsync("USD", "US dollar", "Amerikaanse dollar");

        using (UiCulture("nl"))
        {
            (await _service.GetListAsync(List<CurrencyListRequestDto>(search: "pond"))).Rows.ShouldHaveSingleItem().IsoCode.ShouldBe("GBP");
            (await _service.GetListAsync(List<CurrencyListRequestDto>(search: "sterling"))).Rows.ShouldHaveSingleItem().IsoCode.ShouldBe("GBP");
            (await _service.GetListAsync(List<CurrencyListRequestDto>(search: "DOLLAR"))).Rows.Count.ShouldBe(1);
            (await _service.GetListAsync(List<CurrencyListRequestDto>(search: " gb "))).Rows.ShouldHaveSingleItem().IsoCode.ShouldBe("GBP");
        }

        using (UiCulture("en"))
        {
            (await _service.GetListAsync(List<CurrencyListRequestDto>(search: "pond"))).Rows.ShouldBeEmpty();
            (await _service.GetListAsync(List<CurrencyListRequestDto>(search: "dollar"))).Rows.ShouldHaveSingleItem().IsoCode.ShouldBe("USD");
        }
    }

    [Fact]
    public async Task Default_sort_is_the_resolved_name_in_the_ui_language()
    {
        using var _ = InTenantA();
        await CreateAsync("AAA", "Alpha", "Zebra");
        await CreateAsync("BBB", "Mike", "Mike");
        await CreateAsync("CCC", "Zulu", "Appel");
        await CreateAsync("DDD", "Bravo", "Bravo");

        using (UiCulture("en-GB"))
        {
            var rows = (await _service.GetListAsync(List<CurrencyListRequestDto>())).Rows;
            rows.Select(r => r.Name).ShouldBe(["Alpha", "Bravo", "Mike", "Zulu"]);
            rows.Select(r => r.IsoCode).ShouldBe(["AAA", "DDD", "BBB", "CCC"]);
        }

        using (UiCulture("nl-NL"))
        {
            var rows = (await _service.GetListAsync(List<CurrencyListRequestDto>())).Rows;
            rows.Select(r => r.Name).ShouldBe(["Appel", "Bravo", "Mike", "Zebra"]);
            rows.Select(r => r.IsoCode).ShouldBe(["CCC", "DDD", "BBB", "AAA"]);

            var descending = (await _service.GetListAsync(List<CurrencyListRequestDto>(sortBy: "name", sortDir: "desc"))).Rows;
            descending.Select(r => r.IsoCode).ShouldBe(["AAA", "BBB", "DDD", "CCC"]);
        }
    }

    /// <summary>
    /// Design 41, "LocalizedName": a list sorts on Text(name, uiCulture) ?? Text(name, DefaultCulture).
    /// A row without a text in the UI language must therefore sort on its English text, not as NULL.
    /// </summary>
    [Fact]
    public async Task Default_sort_falls_back_to_the_default_culture_text_for_a_row_without_a_text_in_the_ui_language()
    {
        using var _ = InTenantA();
        await CreateAsync("AAA", "Alpha", "Zebra");
        await CreateAsync("CCC", "Zulu", "Appel");
        await CreateAsync("DDD", "Bravo");

        using var culture = UiCulture("nl-NL");
        var rows = (await _service.GetListAsync(List<CurrencyListRequestDto>())).Rows;

        rows.Select(r => r.Name).ShouldBe(["Appel", "Bravo", "Zebra"]);
        rows.Select(r => r.IsoCode).ShouldBe(["CCC", "DDD", "AAA"]);
    }

    [Fact]
    public async Task Sort_accepts_every_row_column_and_rejects_anything_else()
    {
        using var _ = InTenantA();
        await CreateAsync("AAA", "Alpha", symbol: "b");
        await CreateAsync("BBB", "Bravo", symbol: "a", active: false);

        foreach (var column in new[] { "name", "isoCode", "isoNumeric", "minorUnits", "symbol", "isActive", "isDefault", "description", "IsoCode" })
        {
            (await _service.GetListAsync(List<CurrencyListRequestDto>(sortBy: column))).Rows.Count.ShouldBe(2, column);
        }

        (await _service.GetListAsync(List<CurrencyListRequestDto>(sortBy: "symbol"))).Rows.Select(r => r.IsoCode).ShouldBe(["BBB", "AAA"]);
        (await _service.GetListAsync(List<CurrencyListRequestDto>(sortBy: "isActive", sortDir: "desc"))).Rows.First().IsoCode.ShouldBe("AAA");

        var ex = await Should.ThrowAsync<BusinessException>(() => _service.GetListAsync(List<CurrencyListRequestDto>(sortBy: "tenantId")));
        ex.Code.ShouldBe(MasterDataErrorCodes.UnknownSortColumn);
        ex.Data["sortBy"].ShouldBe("tenantId");
    }

    // ── Lookup ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Lookup_returns_active_currencies_only_ordered_by_name_bounded_and_searchable()
    {
        using var _ = InTenantA();
        using var culture = UiCulture("en");
        await CreateAsync("EUR", "Euro");
        await CreateAsync("USD", "US dollar");
        await CreateAsync("XAU", "Gold", active: false);
        await CreateAsync("GBP", "Pound sterling");

        var all = await _service.GetLookupAsync();
        all.Select(l => l.Code).ShouldBe(["EUR", "GBP", "USD"]);
        all.Select(l => l.Name).ShouldBe(["Euro", "Pound sterling", "US dollar"]);

        (await _service.GetLookupAsync(take: 2)).Select(l => l.Code).ShouldBe(["EUR", "GBP"]);
        (await _service.GetLookupAsync(take: 0)).Count.ShouldBe(1);
        (await _service.GetLookupAsync(take: 10_000)).Count.ShouldBe(3);
        (await _service.GetLookupAsync("dollar")).ShouldHaveSingleItem().Code.ShouldBe("USD");
        (await _service.GetLookupAsync("gold")).ShouldBeEmpty();
    }

    // ── Update and concurrency ────────────────────────────────────────────────

    [Fact]
    public async Task Update_with_the_returned_stamp_succeeds_and_a_stale_stamp_is_a_concurrency_conflict()
    {
        using var _ = InTenantA();
        var created = await CreateAsync("eur", "Euro");

        var update = UpdateFrom(created);
        update.Names["nl"] = "Euro (nl)";
        update.Symbol = "€";
        update.IsoCode = "eur";
        var updated = await _service.UpdateAsync(created.Id, update);

        updated.Symbol.ShouldBe("€");
        updated.Names["nl"].ShouldBe("Euro (nl)");
        updated.IsoCode.ShouldBe("EUR");
        updated.ConcurrencyStamp.ShouldNotBe(created.ConcurrencyStamp);
        updated.LastModificationTime.ShouldNotBeNull();

        var stale = UpdateFrom(created);
        stale.Symbol = "E";
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => _service.UpdateAsync(created.Id, stale));

        (await _service.GetAsync(created.Id)).Symbol.ShouldBe("€");
    }

    [Fact]
    public async Task Update_can_change_the_code_but_not_to_another_currency_code()
    {
        using var _ = InTenantA();
        var eur = await CreateAsync("EUR", "Euro");
        await CreateAsync("USD", "US dollar");

        var renamed = UpdateFrom(eur);
        renamed.IsoCode = "eua";
        (await _service.UpdateAsync(eur.Id, renamed)).IsoCode.ShouldBe("EUA");

        var clash = UpdateFrom(await _service.GetAsync(eur.Id));
        clash.IsoCode = "usd";
        var ex = await Should.ThrowAsync<BusinessException>(() => _service.UpdateAsync(eur.Id, clash));
        ex.Code.ShouldBe(MasterDataErrorCodes.DuplicateKey);
        ex.Data["field"].ShouldBe("isoCode");
        ex.Data["value"].ShouldBe("USD");
    }

    [Fact]
    public async Task Update_of_an_unknown_id_is_not_found()
    {
        using var _ = InTenantA();

        await Should.ThrowAsync<EntityNotFoundException>(() => _service.UpdateAsync(Guid.NewGuid(), new CurrencyUpdateDto { IsoCode = "EUR", Names = Names("Euro"), ConcurrencyStamp = "x" }));
    }

    // ── Uniqueness ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Duplicate_code_is_refused_case_insensitively_and_a_soft_deleted_row_frees_its_code()
    {
        using var _ = InTenantA();
        var first = await CreateAsync("EUR", "Euro");

        var ex = await Should.ThrowAsync<BusinessException>(() => CreateAsync("eur", "Euro again"));
        ex.Code.ShouldBe(MasterDataErrorCodes.DuplicateKey);
        ex.Data["field"].ShouldBe("isoCode");
        ex.Data["value"].ShouldBe("EUR");

        await _service.DeleteAsync(first.Id);
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(first.Id));

        var second = await CreateAsync("EUR", "Euro");
        second.Id.ShouldNotBe(first.Id);
        (await _service.GetListAsync(List<CurrencyListRequestDto>())).Rows.ShouldHaveSingleItem().Id.ShouldBe(second.Id);
    }

    // ── Default ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetDefault_swaps_the_default_so_exactly_one_currency_is_the_default()
    {
        using var _ = InTenantA();
        var eur = await CreateAsync("EUR", "Euro");
        var usd = await CreateAsync("USD", "US dollar");
        await CreateAsync("GBP", "Pound");

        await _service.SetDefaultAsync(eur.Id);
        (await _service.GetAsync(eur.Id)).IsDefault.ShouldBeTrue();
        (await _service.GetListAsync(List<CurrencyListRequestDto>())).Rows.Count(r => r.IsDefault).ShouldBe(1);

        await _service.SetDefaultAsync(usd.Id);
        var rows = (await _service.GetListAsync(List<CurrencyListRequestDto>())).Rows;
        rows.Count(r => r.IsDefault).ShouldBe(1);
        rows.Single(r => r.IsDefault).Id.ShouldBe(usd.Id);
        (await _service.GetAsync(eur.Id)).IsDefault.ShouldBeFalse();

        /* Idempotent: the default stays, nothing changes. */
        await _service.SetDefaultAsync(usd.Id);
        (await _service.GetListAsync(List<CurrencyListRequestDto>())).Rows.Count(r => r.IsDefault).ShouldBe(1);
    }

    [Fact]
    public async Task SetDefault_refuses_an_inactive_currency_and_an_unknown_id()
    {
        using var _ = InTenantA();
        var eur = await CreateAsync("EUR", "Euro");
        await _service.SetDefaultAsync(eur.Id);
        var xau = await CreateAsync("XAU", "Gold", active: false);

        var ex = await Should.ThrowAsync<BusinessException>(() => _service.SetDefaultAsync(xau.Id));
        ex.Code.ShouldBe(MasterDataErrorCodes.DefaultMustBeActive);

        /* That the failed swap leaves the previous default in place is a transactional guarantee; the
         * SQLite module runs without transactions, so MasterDataTransactionTests asserts it on PostgreSQL. */
        (await _service.GetAsync(xau.Id)).IsDefault.ShouldBeFalse();

        await Should.ThrowAsync<EntityNotFoundException>(() => _service.SetDefaultAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task The_default_cannot_be_deactivated_through_update_and_IsDefault_is_not_writable()
    {
        using var _ = InTenantA();
        var eur = await CreateAsync("EUR", "Euro");
        await _service.SetDefaultAsync(eur.Id);

        var deactivate = UpdateFrom(await _service.GetAsync(eur.Id));
        deactivate.IsActive = false;
        (await Should.ThrowAsync<BusinessException>(() => _service.UpdateAsync(eur.Id, deactivate))).Code.ShouldBe(MasterDataErrorCodes.DefaultMustBeActive);
        (await _service.GetAsync(eur.Id)).IsActive.ShouldBeTrue();

        /* Nothing on the update DTO can set or clear the default. */
        typeof(CurrencyUpdateDto).GetProperty("IsDefault").ShouldBeNull();
    }

    // ── Delete and in-use ─────────────────────────────────────────────────────

    [Fact]
    public async Task The_default_currency_cannot_be_deleted()
    {
        using var _ = InTenantA();
        using var culture = UiCulture("en");
        var eur = await CreateAsync("EUR", "Euro");
        await _service.SetDefaultAsync(eur.Id);

        var ex = await Should.ThrowAsync<BusinessException>(() => _service.DeleteAsync(eur.Id));

        ex.Code.ShouldBe(MasterDataErrorCodes.InUse);
        ex.Data["reason"].ShouldBe("The default currency cannot be deleted. Make another currency the default first.");
        (await _service.GetAsync(eur.Id)).IsDefault.ShouldBeTrue();
    }

    [Fact]
    public async Task A_currency_used_by_a_country_cannot_be_deleted_until_the_country_lets_go()
    {
        using var _ = InTenantA();
        using var culture = UiCulture("en");
        var eur = await CreateAsync("EUR", "Euro");
        var nl = await _countries.CreateAsync(new CountryCreateDto { IsoAlpha2 = "NL", IsoAlpha3 = "NLD", Names = Names("Netherlands"), DefaultCurrencyId = eur.Id });

        var ex = await Should.ThrowAsync<BusinessException>(() => _service.DeleteAsync(eur.Id));
        ex.Code.ShouldBe(MasterDataErrorCodes.InUse);
        ex.Data["reason"].ShouldBe("used as default currency by a country");

        await _countries.UpdateAsync(nl.Id, new CountryUpdateDto { IsoAlpha2 = "NL", IsoAlpha3 = "NLD", Names = Names("Netherlands"), DefaultCurrencyId = null, ConcurrencyStamp = nl.ConcurrencyStamp });
        await _service.DeleteAsync(eur.Id);
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(eur.Id));
    }

    [Fact]
    public async Task Delete_of_an_unknown_id_is_not_found()
    {
        using var _ = InTenantA();

        await Should.ThrowAsync<EntityNotFoundException>(() => _service.DeleteAsync(Guid.NewGuid()));
    }

    // ── Bulk delete ───────────────────────────────────────────────────────────

    [Fact]
    public async Task BulkDelete_explicit_deletes_the_deletable_rows_and_reports_the_skipped_ones()
    {
        using var _ = InTenantA();
        using var culture = UiCulture("en");
        var eur = await CreateAsync("EUR", "Euro");
        var usd = await CreateAsync("USD", "US dollar");
        var gbp = await CreateAsync("GBP", "Pound");
        var chf = await CreateAsync("CHF", "Franc");
        await _service.SetDefaultAsync(eur.Id);
        await _countries.CreateAsync(new CountryCreateDto { IsoAlpha2 = "US", IsoAlpha3 = "USA", Names = Names("United States"), DefaultCurrencyId = usd.Id });

        var result = await _service.BulkDeleteAsync(new CurrencyBulkDeleteRequestDto { Selection = Explicit(eur.Id, usd.Id, gbp.Id, gbp.Id, Guid.NewGuid()) });

        result.DeletedCount.ShouldBe(1);
        result.SkippedRows.Count.ShouldBe(2);
        result.SkippedRows.Single(s => s.Id == eur.Id).Name.ShouldBe("EUR");
        result.SkippedRows.Single(s => s.Id == eur.Id).Reason.ShouldBe("The default currency cannot be deleted. Make another currency the default first.");
        result.SkippedRows.Single(s => s.Id == usd.Id).Reason.ShouldBe("used as default currency by a country");

        var remaining = (await _service.GetListAsync(List<CurrencyListRequestDto>(sortBy: "isoCode"))).Rows.Select(r => r.IsoCode);
        remaining.ShouldBe(["CHF", "EUR", "USD"]);
        chf.Id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task BulkDelete_run_twice_with_the_same_ids_deletes_nothing_the_second_time()
    {
        using var _ = InTenantA();
        var usd = await CreateAsync("USD", "US dollar");
        var gbp = await CreateAsync("GBP", "Pound");
        var request = new CurrencyBulkDeleteRequestDto { Selection = Explicit(usd.Id, gbp.Id) };

        var first = await _service.BulkDeleteAsync(request);
        var second = await _service.BulkDeleteAsync(request);

        first.DeletedCount.ShouldBe(2);
        second.DeletedCount.ShouldBe(0);
        second.SkippedRows.ShouldBeEmpty();
        (await _service.GetListAsync(List<CurrencyListRequestDto>())).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task BulkDelete_filter_based_uses_the_search_the_activity_filter_and_the_exclusions()
    {
        using var _ = InTenantA();
        var aaa = await CreateAsync("AAA", "Test alpha");
        var bbb = await CreateAsync("BBB", "Test bravo");
        var ccc = await CreateAsync("CCC", "Test charlie", active: false);
        var ddd = await CreateAsync("DDD", "Other");

        var result = await _service.BulkDeleteAsync(new CurrencyBulkDeleteRequestDto
        {
            Selection = FilterBased("test", bbb.Id),
            IsActive = true
        });

        result.DeletedCount.ShouldBe(1);
        result.SkippedRows.ShouldBeEmpty();
        var remaining = (await _service.GetListAsync(List<CurrencyListRequestDto>(sortBy: "isoCode"))).Rows.Select(r => r.Id).ToList();
        remaining.ShouldBe([bbb.Id, ccc.Id, ddd.Id]);
        aaa.Id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task BulkDelete_with_an_empty_explicit_selection_does_nothing()
    {
        using var _ = InTenantA();
        await CreateAsync("EUR", "Euro");

        var result = await _service.BulkDeleteAsync(new CurrencyBulkDeleteRequestDto { Selection = Explicit() });

        result.DeletedCount.ShouldBe(0);
        (await _service.GetListAsync(List<CurrencyListRequestDto>())).TotalCount.ShouldBe(1);
    }

    // ── Tenant isolation (Critical) ───────────────────────────────────────────

    [Fact]
    public async Task Rows_of_tenant_A_are_invisible_and_untouchable_from_tenant_B()
    {
        CurrencyDto eur;
        using (InTenantA())
        {
            eur = await CreateAsync("EUR", "Euro");
            await _service.SetDefaultAsync(eur.Id);
            await CreateAsync("USD", "US dollar");
        }

        using (InTenantB())
        {
            var page = await _service.GetListAsync(List<CurrencyListRequestDto>());
            page.TotalCount.ShouldBe(0);
            page.Rows.ShouldBeEmpty();
            (await _service.GetListAsync(List<CurrencyListRequestDto>(search: "eur"))).FilteredCount.ShouldBe(0);
            (await _service.GetLookupAsync()).ShouldBeEmpty();
            (await _service.GetLookupAsync("eur")).ShouldBeEmpty();

            await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(eur.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.UpdateAsync(eur.Id, UpdateFrom(eur)));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.DeleteAsync(eur.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.SetDefaultAsync(eur.Id));

            (await _service.BulkDeleteAsync(new CurrencyBulkDeleteRequestDto { Selection = Explicit(eur.Id) })).DeletedCount.ShouldBe(0);
            (await _service.BulkDeleteAsync(new CurrencyBulkDeleteRequestDto { Selection = FilterBased() })).DeletedCount.ShouldBe(0);

            /* Tenant B may use the same code and its own default. */
            var own = await CreateAsync("EUR", "Euro B");
            await _service.SetDefaultAsync(own.Id);
            (await _service.GetListAsync(List<CurrencyListRequestDto>())).Rows.ShouldHaveSingleItem().Name.ShouldBe("Euro B");
        }

        using (InTenantA())
        {
            var rows = (await _service.GetListAsync(List<CurrencyListRequestDto>(sortBy: "isoCode"))).Rows;
            rows.Select(r => r.IsoCode).ShouldBe(["EUR", "USD"]);
            rows.Single(r => r.IsoCode == "EUR").IsDefault.ShouldBeTrue();
            rows.Single(r => r.IsoCode == "EUR").Name.ShouldBe("Euro");
        }
    }

    [Fact]
    public async Task Host_rows_and_tenant_rows_do_not_mix()
    {
        var hostRow = await CreateAsync("EUR", "Euro (host)");

        using (InTenantA())
        {
            (await _service.GetListAsync(List<CurrencyListRequestDto>())).TotalCount.ShouldBe(0);
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(hostRow.Id));
            await CreateAsync("EUR", "Euro (tenant)");
        }

        (await _service.GetListAsync(List<CurrencyListRequestDto>())).Rows.ShouldHaveSingleItem().Name.ShouldBe("Euro (host)");
    }

    [Fact]
    public async Task A_country_of_tenant_B_cannot_reference_a_currency_of_tenant_A()
    {
        CurrencyDto eur;
        using (InTenantA())
        {
            eur = await CreateAsync("EUR", "Euro");
        }

        using (InTenantB())
        {
            var ex = await Should.ThrowAsync<BusinessException>(() => _countries.CreateAsync(new CountryCreateDto
            {
                IsoAlpha2 = "NL", IsoAlpha3 = "NLD", Names = Names("Netherlands"), DefaultCurrencyId = eur.Id
            }));
            ex.Code.ShouldBe(MasterDataErrorCodes.ReferenceNotFound);
            ex.Data["field"].ShouldBe("defaultCurrencyId");
            (await _countries.GetListAsync(List())).TotalCount.ShouldBe(0);
        }
    }
}
