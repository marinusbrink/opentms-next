using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Regions;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Validation;
using Xunit;

namespace OpenTms.EntityFrameworkCore.MasterData;

/// <summary>
/// CountryAppService on SQLite (design 41). Risk class: Critical for tenant isolation and the two
/// unique ISO keys; High for the currency reference, time-zone validation, in-use and bulk delete.
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
}
