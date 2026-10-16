using OpenTms.Platform.Features;
using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Features;
using OpenTms.MasterData;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Regions;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Features;
using Volo.Abp.Validation;
using Xunit;

namespace OpenTms.EntityFrameworkCore.MasterData;

/// <summary>
/// CountryAppService on SQLite (design 41; default country per PO 2026-10-13). Risk class: Critical for
/// tenant isolation, the two unique ISO keys and the single default with its feature and permission gates;
/// High for the currency reference, time-zone validation, the default rules, in-use and bulk delete.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class CountryAppServiceTests : MasterDataAppServiceTestBase
{
    private readonly ICountryAppService _service;
    private readonly ICurrencyAppService _currencies;
    private readonly IGeographicalRegionAppService _regions;

    public CountryAppServiceTests()
    {
        _service = GetRequiredService<ICountryAppService>();
        _currencies = GetRequiredService<ICurrencyAppService>();
        _regions = GetRequiredService<IGeographicalRegionAppService>();
    }

    private Task<CountryDto> CreateAsync(string alpha2, string alpha3, string en, string? nl = null, Guid? currencyId = null, string? timeZone = null, int? numeric = null)
        => _service.CreateAsync(new CountryCreateDto { IsoAlpha2 = alpha2, IsoAlpha3 = alpha3, Names = Names(en, nl), DefaultCurrencyId = currencyId, PrimaryTimeZoneId = timeZone, IsoNumeric = numeric });

    private static CountryUpdateDto UpdateFrom(CountryDto dto) => new()
    {
        IsoAlpha2 = dto.IsoAlpha2,
        IsoAlpha3 = dto.IsoAlpha3,
        IsoNumeric = dto.IsoNumeric,
        Names = Copy(dto.Names),
        Description = dto.Description,
        DefaultCurrencyId = dto.DefaultCurrencyId,
        PrimaryTimeZoneId = dto.PrimaryTimeZoneId,
        ConcurrencyStamp = dto.ConcurrencyStamp
    };

    [Fact]
    public async Task Create_normalises_codes_resolves_the_currency_code_and_validates_the_time_zone()
    {
        using var _ = InTenantA();
        using var culture = UiCulture("nl");
        var eur = await _currencies.CreateAsync(new CurrencyCreateDto { IsoCode = "EUR", Names = Names("Euro") });

        var nl = await CreateAsync("nl", "nld", "Netherlands", "Nederland", eur.Id, " Europe/Amsterdam ", 528);

        nl.IsoAlpha2.ShouldBe("NL");
        nl.IsoAlpha3.ShouldBe("NLD");
        nl.IsoNumeric.ShouldBe(528);
        nl.Name.ShouldBe("Nederland");
        nl.Names.Count.ShouldBe(2);
        nl.DefaultCurrencyId.ShouldBe(eur.Id);
        nl.DefaultCurrencyCode.ShouldBe("EUR");
        nl.PrimaryTimeZoneId.ShouldBe("Europe/Amsterdam");

        var row = (await _service.GetListAsync(List())).Rows.ShouldHaveSingleItem();
        row.DefaultCurrencyCode.ShouldBe("EUR");
        row.Name.ShouldBe("Nederland");
        row.PrimaryTimeZoneId.ShouldBe("Europe/Amsterdam");

        (await _service.GetAsync(nl.Id)).DefaultCurrencyCode.ShouldBe("EUR");
        (await _service.GetLookupAsync()).ShouldHaveSingleItem().Code.ShouldBe("NL");
    }

    [Fact]
    public async Task A_country_without_currency_or_time_zone_is_allowed()
    {
        using var _ = InTenantA();

        var aq = await CreateAsync("AQ", "ATA", "Antarctica");

        aq.DefaultCurrencyId.ShouldBeNull();
        aq.DefaultCurrencyCode.ShouldBeNull();
        aq.PrimaryTimeZoneId.ShouldBeNull();
    }

    [Fact]
    public async Task An_unknown_time_zone_is_refused()
    {
        using var _ = InTenantA();

        var ex = await Should.ThrowAsync<BusinessException>(() => CreateAsync("NL", "NLD", "Netherlands", timeZone: "Mars/Olympus"));

        ex.Code.ShouldBe(MasterDataErrorCodes.InvalidTimeZone);
        (await _service.GetListAsync(List())).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_missing_currency_reference_is_refused()
    {
        using var _ = InTenantA();

        var ex = await Should.ThrowAsync<BusinessException>(() => CreateAsync("NL", "NLD", "Netherlands", currencyId: Guid.NewGuid()));

        ex.Code.ShouldBe(MasterDataErrorCodes.ReferenceNotFound);
        ex.Data["field"].ShouldBe("defaultCurrencyId");
    }

    [Fact]
    public async Task Malformed_codes_are_rejected_by_validation()
    {
        using var _ = InTenantA();

        await Should.ThrowAsync<AbpValidationException>(() => CreateAsync("NLD", "NLD", "Netherlands"));
        await Should.ThrowAsync<AbpValidationException>(() => CreateAsync("NL", "NL", "Netherlands"));
        await Should.ThrowAsync<AbpValidationException>(() => CreateAsync("N1", "NLD", "Netherlands"));
    }

    [Fact]
    public async Task Both_iso_codes_are_unique_per_tenant_and_freed_by_a_soft_delete()
    {
        using var _ = InTenantA();
        var nl = await CreateAsync("NL", "NLD", "Netherlands");

        var alpha2 = await Should.ThrowAsync<BusinessException>(() => CreateAsync("nl", "XXX", "Clash"));
        alpha2.Code.ShouldBe(MasterDataErrorCodes.DuplicateKey);
        alpha2.Data["field"].ShouldBe("isoAlpha2");

        var alpha3 = await Should.ThrowAsync<BusinessException>(() => CreateAsync("XX", "nld", "Clash"));
        alpha3.Code.ShouldBe(MasterDataErrorCodes.DuplicateKey);
        alpha3.Data["field"].ShouldBe("isoAlpha3");

        await _service.DeleteAsync(nl.Id);
        (await CreateAsync("NL", "NLD", "Netherlands again")).Id.ShouldNotBe(nl.Id);
    }

    [Fact]
    public async Task Update_changes_codes_names_currency_and_time_zone_and_checks_the_stamp()
    {
        using var _ = InTenantA();
        using var culture = UiCulture("en");
        var eur = await _currencies.CreateAsync(new CurrencyCreateDto { IsoCode = "EUR", Names = Names("Euro") });
        var be = await CreateAsync("BE", "BEL", "Belgium");
        await CreateAsync("LU", "LUX", "Luxembourg");

        var update = UpdateFrom(be);
        update.IsoAlpha2 = "bx";
        update.IsoAlpha3 = "bex";
        update.Names["nl"] = "België";
        update.DefaultCurrencyId = eur.Id;
        update.PrimaryTimeZoneId = "Europe/Brussels";
        update.IsoNumeric = 56;
        var updated = await _service.UpdateAsync(be.Id, update);

        updated.IsoAlpha2.ShouldBe("BX");
        updated.IsoAlpha3.ShouldBe("BEX");
        updated.Names["nl"].ShouldBe("België");
        updated.DefaultCurrencyCode.ShouldBe("EUR");
        updated.PrimaryTimeZoneId.ShouldBe("Europe/Brussels");
        updated.IsoNumeric.ShouldBe(56);
        updated.ConcurrencyStamp.ShouldNotBe(be.ConcurrencyStamp);

        var clash = UpdateFrom(updated);
        clash.IsoAlpha2 = "LU";
        (await Should.ThrowAsync<BusinessException>(() => _service.UpdateAsync(be.Id, clash))).Code.ShouldBe(MasterDataErrorCodes.DuplicateKey);

        var stale = UpdateFrom(be);
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => _service.UpdateAsync(be.Id, stale));

        var badZone = UpdateFrom(updated);
        badZone.PrimaryTimeZoneId = "Nowhere/Town";
        (await Should.ThrowAsync<BusinessException>(() => _service.UpdateAsync(be.Id, badZone))).Code.ShouldBe(MasterDataErrorCodes.InvalidTimeZone);
    }

    [Fact]
    public async Task List_searches_codes_and_localized_names_and_sorts_on_the_resolved_name()
    {
        using var _ = InTenantA();
        await CreateAsync("NL", "NLD", "Netherlands", "Nederland");
        await CreateAsync("DE", "DEU", "Germany", "Duitsland");
        await CreateAsync("BE", "BEL", "Belgium", "België");

        using (UiCulture("nl"))
        {
            (await _service.GetListAsync(List())).Rows.Select(r => r.Name).ShouldBe(["België", "Duitsland", "Nederland"]);
            (await _service.GetListAsync(List(search: "nederland"))).Rows.ShouldHaveSingleItem().IsoAlpha2.ShouldBe("NL");
            (await _service.GetListAsync(List(search: "Netherlands"))).Rows.ShouldHaveSingleItem().IsoAlpha2.ShouldBe("NL");
            (await _service.GetListAsync(List(search: "deu"))).Rows.ShouldHaveSingleItem().IsoAlpha2.ShouldBe("DE");
            (await _service.GetListAsync(List(sortBy: "name", sortDir: "desc"))).Rows.Select(r => r.IsoAlpha2).ShouldBe(["NL", "DE", "BE"]);
        }

        using (UiCulture("en"))
        {
            (await _service.GetListAsync(List())).Rows.Select(r => r.Name).ShouldBe(["Belgium", "Germany", "Netherlands"]);
            (await _service.GetListAsync(List(search: "duitsland"))).Rows.ShouldBeEmpty();
            (await _service.GetListAsync(List(sortBy: "isoAlpha3"))).Rows.Select(r => r.IsoAlpha3).ShouldBe(["BEL", "DEU", "NLD"]);
            (await _service.GetListAsync(List(sortBy: "isoAlpha2", sortDir: "desc"))).Rows.Select(r => r.IsoAlpha2).ShouldBe(["NL", "DE", "BE"]);
            (await _service.GetLookupAsync("ger")).ShouldHaveSingleItem().Name.ShouldBe("Germany");
        }

        foreach (var column in new[] { "isoNumeric", "primaryTimeZoneId", "description", "defaultCurrencyCode" })
        {
            (await _service.GetListAsync(List(sortBy: column))).Rows.Count.ShouldBe(3, column);
        }

        (await Should.ThrowAsync<BusinessException>(() => _service.GetListAsync(List(sortBy: "names")))).Code.ShouldBe(MasterDataErrorCodes.UnknownSortColumn);
    }

    [Fact]
    public async Task A_country_used_by_a_postal_code_range_cannot_be_deleted()
    {
        using var _ = InTenantA();
        using var culture = UiCulture("en");
        var nl = await CreateAsync("NL", "NLD", "Netherlands");
        var region = await _regions.CreateAsync(new GeographicalRegionCreateDto
        {
            Name = "Randstad",
            PostalCodeRanges = ListOf(new GeographicalRegionPostalCodeRangeDto { CountryId = nl.Id, FromNumeric = 1000, TillNumeric = 1999 })
        });

        var ex = await Should.ThrowAsync<BusinessException>(() => _service.DeleteAsync(nl.Id));
        ex.Code.ShouldBe(MasterDataErrorCodes.InUse);
        ex.Data["reason"].ShouldBe("used by a postal-code range of a geographical region");

        var bulk = await _service.BulkDeleteAsync(new MasterDataBulkDeleteRequestDto { Selection = Explicit(nl.Id) });
        bulk.DeletedCount.ShouldBe(0);
        bulk.SkippedRows.ShouldHaveSingleItem().Name.ShouldBe("NL");

        await _regions.DeleteAsync(region.Id);
        await _service.DeleteAsync(nl.Id);
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(nl.Id));
    }

    [Fact]
    public async Task BulkDelete_filter_based_and_twice_is_idempotent()
    {
        using var _ = InTenantA();
        var nl = await CreateAsync("NL", "NLD", "Netherlands");
        var be = await CreateAsync("BE", "BEL", "Belgium");
        await CreateAsync("DE", "DEU", "Germany");

        var first = await _service.BulkDeleteAsync(new MasterDataBulkDeleteRequestDto { Selection = FilterBased("l", nl.Id) });
        first.DeletedCount.ShouldBe(1);
        (await _service.GetListAsync(List(sortBy: "isoAlpha2"))).Rows.Select(r => r.IsoAlpha2).ShouldBe(["DE", "NL"]);

        (await _service.BulkDeleteAsync(new MasterDataBulkDeleteRequestDto { Selection = Explicit(be.Id) })).DeletedCount.ShouldBe(0);
    }

    // ── Default country (PO 2026-10-13, Critical for the single default and its gates, High for the rules) ──

    [Fact]
    public async Task SetDefault_moves_the_flag_from_the_previous_country_to_the_new_one_in_one_call()
    {
        using var _ = InTenantA();
        var nl = await CreateAsync("NL", "NLD", "Netherlands");
        var be = await CreateAsync("BE", "BEL", "Belgium");
        await CreateAsync("DE", "DEU", "Germany");

        nl.IsDefault.ShouldBeFalse("a new country is never the default");
        (await _service.GetListAsync(List())).Rows.ShouldAllBe(r => !r.IsDefault);

        await _service.SetDefaultAsync(nl.Id);
        (await _service.GetAsync(nl.Id)).IsDefault.ShouldBeTrue();
        (await _service.GetListAsync(List())).Rows.Count(r => r.IsDefault).ShouldBe(1);

        await _service.SetDefaultAsync(be.Id);
        var rows = (await _service.GetListAsync(List())).Rows;
        rows.Count(r => r.IsDefault).ShouldBe(1);
        rows.Single(r => r.IsDefault).Id.ShouldBe(be.Id);
        (await _service.GetAsync(nl.Id)).IsDefault.ShouldBeFalse();
        (await _service.GetAsync(be.Id)).IsDefault.ShouldBeTrue();

        await Should.ThrowAsync<EntityNotFoundException>(() => _service.SetDefaultAsync(Guid.NewGuid()));
        (await _service.GetListAsync(List())).Rows.Single(r => r.IsDefault).Id.ShouldBe(be.Id, "a failed call leaves the default alone");
    }

    [Fact]
    public async Task SetDefault_on_the_current_default_is_a_no_op()
    {
        using var _ = InTenantA();
        var nl = await CreateAsync("NL", "NLD", "Netherlands");
        await CreateAsync("BE", "BEL", "Belgium");
        await _service.SetDefaultAsync(nl.Id);
        var before = await _service.GetAsync(nl.Id);

        await _service.SetDefaultAsync(nl.Id);

        var after = await _service.GetAsync(nl.Id);
        after.IsDefault.ShouldBeTrue();
        after.ConcurrencyStamp.ShouldBe(before.ConcurrencyStamp, "nothing is written when the country already is the default");
        (await _service.GetListAsync(List())).Rows.Count(r => r.IsDefault).ShouldBe(1);
    }

    [Fact]
    public async Task The_row_and_the_detail_carry_the_default_and_the_list_sorts_on_it()
    {
        using var _ = InTenantA();
        using var culture = UiCulture("en");
        await CreateAsync("NL", "NLD", "Netherlands");
        var be = await CreateAsync("BE", "BEL", "Belgium");
        await CreateAsync("DE", "DEU", "Germany");
        await _service.SetDefaultAsync(be.Id);

        var rows = (await _service.GetListAsync(List())).Rows;
        rows.Single(r => r.IsoAlpha2 == "BE").IsDefault.ShouldBeTrue();
        rows.Where(r => r.IsoAlpha2 != "BE").ShouldAllBe(r => !r.IsDefault);
        (await _service.GetAsync(be.Id)).IsDefault.ShouldBeTrue();

        (await _service.GetListAsync(List(sortBy: "isDefault", sortDir: "desc"))).Rows.First().IsoAlpha2.ShouldBe("BE");
        (await _service.GetListAsync(List(sortBy: "isDefault", sortDir: "asc"))).Rows.Last().IsoAlpha2.ShouldBe("BE");
        (await _service.GetListAsync(List(sortBy: "IsDefault"))).Rows.Count.ShouldBe(3, "the sort key is case-insensitive like the others");

        /* The default is a command, never a writable field; an ordinary update leaves it alone. */
        typeof(CountryCreateDto).GetProperty("IsDefault").ShouldBeNull();
        typeof(CountryUpdateDto).GetProperty("IsDefault").ShouldBeNull();
        var update = UpdateFrom(await _service.GetAsync(be.Id));
        update.Description = "still the default";
        (await _service.UpdateAsync(be.Id, update)).IsDefault.ShouldBeTrue();
    }

    [Fact]
    public async Task GetDefault_returns_the_default_country_or_null_while_none_is_set()
    {
        using var _ = InTenantA();
        using var culture = UiCulture("nl");
        (await _service.GetDefaultAsync()).ShouldBeNull("a tenant starts without a default country");

        var nl = await CreateAsync("NL", "NLD", "Netherlands", "Nederland");
        var be = await CreateAsync("BE", "BEL", "Belgium", "België");
        (await _service.GetDefaultAsync()).ShouldBeNull("creating countries does not pick one");

        await _service.SetDefaultAsync(nl.Id);
        var current = await _service.GetDefaultAsync();
        current.ShouldNotBeNull();
        current!.Id.ShouldBe(nl.Id);
        current.IsDefault.ShouldBeTrue();
        current.Name.ShouldBe("Nederland");
        current.IsoAlpha2.ShouldBe("NL");
        current.ConcurrencyStamp.ShouldBe((await _service.GetAsync(nl.Id)).ConcurrencyStamp, "the same detail as GetAsync");

        await _service.SetDefaultAsync(be.Id);
        (await _service.GetDefaultAsync())!.Id.ShouldBe(be.Id);
    }

    [Fact]
    public async Task The_default_country_cannot_be_deleted_until_another_takes_over()
    {
        using var _ = InTenantA();
        var nl = await CreateAsync("NL", "NLD", "Netherlands");
        var be = await CreateAsync("BE", "BEL", "Belgium");
        var de = await CreateAsync("DE", "DEU", "Germany");
        await _service.SetDefaultAsync(nl.Id);

        using (UiCulture("en"))
        {
            var ex = await Should.ThrowAsync<BusinessException>(() => _service.DeleteAsync(nl.Id));
            ex.Code.ShouldBe(MasterDataErrorCodes.InUse);
            ex.Data["reason"].ShouldBe("The default country cannot be deleted. Make another country the default first.");
        }

        using (UiCulture("nl"))
        {
            var ex = await Should.ThrowAsync<BusinessException>(() => _service.DeleteAsync(nl.Id));
            ex.Code.ShouldBe(MasterDataErrorCodes.InUse);
            ex.Data["reason"].ShouldBe("Het standaardland kan niet worden verwijderd. Maak eerst een ander land de standaard.");

            /* Bulk: the other country deletes, the default is skipped with the same localized reason. */
            var bulk = await _service.BulkDeleteAsync(new MasterDataBulkDeleteRequestDto { Selection = Explicit(nl.Id, de.Id) });
            bulk.DeletedCount.ShouldBe(1);
            var skipped = bulk.SkippedRows.ShouldHaveSingleItem();
            skipped.Id.ShouldBe(nl.Id);
            skipped.Name.ShouldBe("NL");
            skipped.Reason.ShouldBe("Het standaardland kan niet worden verwijderd. Maak eerst een ander land de standaard.");
        }

        (await _service.GetAsync(nl.Id)).IsDefault.ShouldBeTrue();
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(de.Id));

        /* Once the default moved, the former default deletes like any other country. */
        await _service.SetDefaultAsync(be.Id);
        await _service.DeleteAsync(nl.Id);
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(nl.Id));
        (await _service.GetDefaultAsync())!.Id.ShouldBe(be.Id);

        /* A filter-based bulk delete skips the default as well. */
        (await _service.BulkDeleteAsync(new MasterDataBulkDeleteRequestDto { Selection = FilterBased() })).DeletedCount.ShouldBe(0);
        (await _service.GetAsync(be.Id)).IsDefault.ShouldBeTrue();
    }

    [Fact]
    public async Task With_the_flag_off_SetDefault_and_GetDefault_are_refused_before_touching_data()
    {
        using var _ = InTenantA();
        var nl = await CreateAsync("NL", "NLD", "Netherlands");

        using (TestFeatureValueProvider.Override(AppFeatures.MasterData.Default, "false"))
        {
            (await GetRequiredService<IFeatureChecker>().IsEnabledAsync(AppFeatures.MasterData.Default)).ShouldBeFalse();
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.SetDefaultAsync(nl.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetDefaultAsync());
        }

        /* PO 2026-10-14: GetDefault is a picker (an editor elsewhere preselects the default country) and needs only
         * the app feature; SetDefault is a command of the Countries list and needs the node. */
        using (TestFeatureValueProvider.Override(AppFeatures.MasterData.General.Reference.Countries, "false"))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.SetDefaultAsync(nl.Id));
            (await _service.GetDefaultAsync()).ShouldBeNull();
        }

        (await _service.GetAsync(nl.Id)).IsDefault.ShouldBeFalse("the refused call wrote nothing");
        (await _service.GetDefaultAsync()).ShouldBeNull();
        await _service.SetDefaultAsync(nl.Id);
        (await _service.GetDefaultAsync())!.Id.ShouldBe(nl.Id);
    }

    // ── Tenant isolation (Critical) ───────────────────────────────────────────

    [Fact]
    public async Task Rows_of_tenant_A_are_invisible_and_untouchable_from_tenant_B()
    {
        CountryDto nl;
        using (InTenantA())
        {
            nl = await CreateAsync("NL", "NLD", "Netherlands", "Nederland");
        }

        using (InTenantB())
        {
            (await _service.GetListAsync(List())).TotalCount.ShouldBe(0);
            (await _service.GetListAsync(List(search: "nl"))).Rows.ShouldBeEmpty();
            (await _service.GetLookupAsync()).ShouldBeEmpty();
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(nl.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.UpdateAsync(nl.Id, UpdateFrom(nl)));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.DeleteAsync(nl.Id));
            (await _service.BulkDeleteAsync(new MasterDataBulkDeleteRequestDto { Selection = FilterBased() })).DeletedCount.ShouldBe(0);

            (await CreateAsync("NL", "NLD", "Netherlands B")).Id.ShouldNotBe(nl.Id);
        }

        using (InTenantA())
        {
            (await _service.GetListAsync(List())).Rows.ShouldHaveSingleItem().Id.ShouldBe(nl.Id);
        }
    }

    [Fact]
    public async Task The_default_country_of_tenant_A_is_neither_visible_nor_settable_from_tenant_B()
    {
        CountryDto nl;
        using (InTenantA())
        {
            nl = await CreateAsync("NL", "NLD", "Netherlands");
            await CreateAsync("BE", "BEL", "Belgium");
            await _service.SetDefaultAsync(nl.Id);
        }

        using (InTenantB())
        {
            (await _service.GetDefaultAsync()).ShouldBeNull("tenant A's default is not tenant B's");
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.SetDefaultAsync(nl.Id));
            (await _service.GetListAsync(List(sortBy: "isDefault", sortDir: "desc"))).Rows.ShouldBeEmpty();

            /* Tenant B picks its own default with the same code; the ids differ. */
            var own = await CreateAsync("NL", "NLD", "Netherlands B");
            own.Id.ShouldNotBe(nl.Id);
            await _service.SetDefaultAsync(own.Id);
            var defaultOfB = await _service.GetDefaultAsync();
            defaultOfB.ShouldNotBeNull();
            defaultOfB!.Id.ShouldBe(own.Id);
        }

        /* The host has no tenant default and cannot reach tenant A's row either. */
        (await _service.GetDefaultAsync()).ShouldBeNull();
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.SetDefaultAsync(nl.Id));

        using (InTenantA())
        {
            var defaultOfA = await _service.GetDefaultAsync();
            defaultOfA.ShouldNotBeNull();
            defaultOfA!.Id.ShouldBe(nl.Id);
            (await _service.GetListAsync(List())).Rows.Count(r => r.IsDefault).ShouldBe(1);
        }
    }
}
