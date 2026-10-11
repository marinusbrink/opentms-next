using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Seed;
using OpenTms.Platform.Localization;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace OpenTms.PostgreSql.MasterData;

/// <summary>
/// The Master Data seed against real PostgreSQL (design 41, "Seed"): the DbMigrator that created the
/// test databases ran the contributor for tenant A (own database) and the shared tenant (host
/// database). Risk class: Critical — idempotent, never resurrects a soft-deleted row, leaves tenant
/// edits alone, re-asserts ISO-fixed fields, EUR active and default, skips the host.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class MasterDataSeedTests : MasterDataPostgreSqlTestBase
{
    private readonly IRepository<Currency, Guid> _currencies;
    private readonly IRepository<Country, Guid> _countries;
    private readonly IRepository<AddressType, Guid> _addressTypes;
    private readonly MasterDataDataSeedContributor _contributor;
    private readonly ICurrencyAppService _currencyService;
    private readonly IDataFilter _dataFilter;

    public MasterDataSeedTests()
    {
        _currencies = GetRequiredService<IRepository<Currency, Guid>>();
        _countries = GetRequiredService<IRepository<Country, Guid>>();
        _addressTypes = GetRequiredService<IRepository<AddressType, Guid>>();
        _contributor = GetRequiredService<MasterDataDataSeedContributor>();
        _currencyService = GetRequiredService<ICurrencyAppService>();
        _dataFilter = GetRequiredService<IDataFilter>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Every_tenant_has_the_iso_reference_data_with_eur_as_the_only_active_and_default_currency(bool ownDatabase)
    {
        var tenantId = ownDatabase ? TenantA : SharedTenant;

        await InRolledBackTransactionAsync(tenantId, async () =>
        {
            (await _currencies.GetCountAsync()).ShouldBe(178);
            var active = await _currencies.GetListAsync(c => c.IsActive);
            active.ShouldHaveSingleItem().IsoCode.ShouldBe("EUR");
            var defaults = await _currencies.GetListAsync(c => c.IsDefault);
            defaults.ShouldHaveSingleItem().IsoCode.ShouldBe("EUR");
            (await _currencies.GetListAsync()).ShouldAllBe(c => c.TenantId == tenantId);

            var eur = active.Single();
            eur.IsoNumeric.ShouldBe(978);
            eur.MinorUnits.ShouldBe(2);
            eur.Symbol.ShouldBe("€");
            eur.Name.Resolve("nl").ShouldBe("Euro");

            var xau = await _currencies.GetAsync(c => c.IsoCode == "XAU");
            xau.MinorUnits.ShouldBeNull();
            xau.IsActive.ShouldBeFalse();

            (await _countries.GetCountAsync()).ShouldBe(249);
            (await _countries.GetListAsync()).ShouldAllBe(c => c.TenantId == tenantId);
            var nl = await _countries.GetAsync(c => c.IsoAlpha2 == "NL");
            nl.IsoAlpha3.ShouldBe("NLD");
            nl.IsoNumeric.ShouldBe(528);
            nl.DefaultCurrencyId.ShouldBe(eur.Id);
            nl.PrimaryTimeZoneId.ShouldBe("Europe/Amsterdam");
            nl.Name.Resolve("nl").ShouldBe("Nederland");
            nl.Name.Resolve("en").ShouldBe("Netherlands");

            var aq = await _countries.GetAsync(c => c.IsoAlpha2 == "AQ");
            aq.DefaultCurrencyId.ShouldBeNull();

            var us = await _countries.GetAsync(c => c.IsoAlpha2 == "US");
            us.DefaultCurrencyId.ShouldBe((await _currencies.GetAsync(c => c.IsoCode == "USD")).Id);

            var types = await _addressTypes.GetListAsync();
            types.Count.ShouldBe(2);
            types.Select(t => t.Kind).ShouldBe([AddressTypeKind.Visiting, AddressTypeKind.Postal], ignoreOrder: true);
            types.Single(t => t.Kind == AddressTypeKind.Visiting).Name.Resolve("nl").ShouldBe("Bezoekadres");
            types.ShouldAllBe(t => t.TenantId == tenantId);
        });
    }

    [Fact]
    public async Task The_host_has_no_master_data_rows()
    {
        await InRolledBackTransactionAsync(null, async () =>
        {
            (await _currencies.GetCountAsync()).ShouldBe(0);
            (await _countries.GetCountAsync()).ShouldBe(0);
            (await _addressTypes.GetCountAsync()).ShouldBe(0);

            /* Running the seeder for the host inserts nothing either. */
            await _contributor.SeedAsync(new DataSeedContext(null));
            await FlushAsync();
            (await _currencies.GetCountAsync()).ShouldBe(0);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_second_seed_run_inserts_nothing_and_keeps_one_default(bool ownDatabase)
    {
        var tenantId = ownDatabase ? TenantA : SharedTenant;

        await InRolledBackTransactionAsync(tenantId, async () =>
        {
            var currencyIds = (await _currencies.GetListAsync()).Select(c => c.Id).OrderBy(id => id).ToList();
            var countryIds = (await _countries.GetListAsync()).Select(c => c.Id).OrderBy(id => id).ToList();
            var typeIds = (await _addressTypes.GetListAsync()).Select(t => t.Id).OrderBy(id => id).ToList();

            await _contributor.SeedAsync(new DataSeedContext(tenantId));
            await FlushAsync();

            (await _currencies.GetCountAsync()).ShouldBe(178);
            (await _countries.GetCountAsync()).ShouldBe(249);
            (await _addressTypes.GetCountAsync()).ShouldBe(2);
            (await _currencies.GetListAsync()).Select(c => c.Id).OrderBy(id => id).ShouldBe(currencyIds);
            (await _countries.GetListAsync()).Select(c => c.Id).OrderBy(id => id).ShouldBe(countryIds);
            (await _addressTypes.GetListAsync()).Select(t => t.Id).OrderBy(id => id).ShouldBe(typeIds);
            (await _currencies.GetListAsync(c => c.IsDefault)).ShouldHaveSingleItem().IsoCode.ShouldBe("EUR");
            (await _currencies.GetListAsync(c => c.IsActive)).ShouldHaveSingleItem().IsoCode.ShouldBe("EUR");
        });
    }

    [Fact]
    public async Task A_soft_deleted_seeded_currency_is_not_resurrected_by_the_next_run()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var xts = await _currencies.GetAsync(c => c.IsoCode == "XTS");
            await _currencyService.DeleteAsync(xts.Id);
            (await _currencies.GetCountAsync()).ShouldBe(177);

            await _contributor.SeedAsync(new DataSeedContext(TenantA));
            await FlushAsync();

            (await _currencies.GetCountAsync()).ShouldBe(177);
            (await _currencies.AnyAsync(c => c.IsoCode == "XTS")).ShouldBeFalse();
            using (_dataFilter.Disable<ISoftDelete>())
            {
                (await _currencies.GetCountAsync()).ShouldBe(178);
                var deleted = await _currencies.GetAsync(c => c.IsoCode == "XTS");
                deleted.IsDeleted.ShouldBeTrue();
                deleted.Id.ShouldBe(xts.Id);
            }
        });
    }

    [Fact]
    public async Task A_seeded_country_the_tenant_deleted_stays_deleted_and_a_deleted_address_type_is_not_reinserted()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var bv = await _countries.GetAsync(c => c.IsoAlpha2 == "BV");
            await _countries.DeleteAsync(bv, autoSave: true);
            var postal = await _addressTypes.GetAsync(t => t.Kind == AddressTypeKind.Postal);
            await _addressTypes.DeleteAsync(postal, autoSave: true);

            await _contributor.SeedAsync(new DataSeedContext(TenantA));
            await FlushAsync();

            (await _countries.GetCountAsync()).ShouldBe(248);
            (await _countries.AnyAsync(c => c.IsoAlpha2 == "BV")).ShouldBeFalse();
            (await _addressTypes.GetCountAsync()).ShouldBe(1);
            (await _addressTypes.AnyAsync(t => t.Kind == AddressTypeKind.Postal)).ShouldBeFalse();
        });
    }

    [Fact]
    public async Task The_seed_keeps_the_tenant_edits_but_re_asserts_the_iso_fixed_fields()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var nl = await _countries.GetAsync(c => c.IsoAlpha2 == "NL");
            nl.SetName(LocalizedName.Create("en", "Holland"));
            nl.SetIsoNumeric(999);
            nl.SetPrimaryTimeZone("Europe/Brussels");
            nl.SetDefaultCurrency(null);
            nl.SetDescription("tenant note");
            await _countries.UpdateAsync(nl, autoSave: true);

            var eur = await _currencies.GetAsync(c => c.IsoCode == "EUR");
            eur.SetName(LocalizedName.Create(new Dictionary<string, string?> { ["en"] = "Euro money", ["nl"] = "Euro geld" }));
            eur.SetMinorUnits(4);
            eur.SetIsoNumeric(1);
            eur.SetSymbol("EUR");
            await _currencies.UpdateAsync(eur, autoSave: true);

            var usd = await _currencies.GetAsync(c => c.IsoCode == "USD");
            usd.SetActive(true);
            await _currencies.UpdateAsync(usd, autoSave: true);

            await _contributor.SeedAsync(new DataSeedContext(TenantA));
            await FlushAsync();

            var nlAfter = await _countries.GetAsync(c => c.IsoAlpha2 == "NL");
            nlAfter.Name.Resolve("en").ShouldBe("Holland");
            nlAfter.IsoNumeric.ShouldBe(528);
            nlAfter.IsoAlpha3.ShouldBe("NLD");
            nlAfter.PrimaryTimeZoneId.ShouldBe("Europe/Brussels");
            nlAfter.DefaultCurrencyId.ShouldBeNull();
            nlAfter.Description.ShouldBe("tenant note");

            var eurAfter = await _currencies.GetAsync(c => c.IsoCode == "EUR");
            eurAfter.Name.Resolve("nl").ShouldBe("Euro geld");
            eurAfter.Symbol.ShouldBe("EUR");
            eurAfter.MinorUnits.ShouldBe(2);
            eurAfter.IsoNumeric.ShouldBe(978);
            eurAfter.IsDefault.ShouldBeTrue();
            eurAfter.IsActive.ShouldBeTrue();

            (await _currencies.GetAsync(c => c.IsoCode == "USD")).IsActive.ShouldBeTrue();
            (await _currencies.GetListAsync(c => c.IsDefault)).Count.ShouldBe(1);
        });
    }

    [Fact]
    public async Task The_seed_does_not_touch_a_default_the_tenant_moved_to_another_currency()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var usd = await _currencies.GetAsync(c => c.IsoCode == "USD");
            usd.SetActive(true);
            await _currencies.UpdateAsync(usd, autoSave: true);
            await _currencyService.SetDefaultAsync(usd.Id);

            await _contributor.SeedAsync(new DataSeedContext(TenantA));
            await FlushAsync();

            (await _currencies.GetListAsync(c => c.IsDefault)).ShouldHaveSingleItem().IsoCode.ShouldBe("USD");
            (await _currencies.GetAsync(c => c.IsoCode == "EUR")).IsDefault.ShouldBeFalse();
        });
    }

    [Fact]
    public async Task The_seed_fills_a_tenant_that_lost_every_currency_and_restores_the_country_links()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            /* Hard-delete the currencies and countries so the tenant is empty (the FK from countries requires the order). */
            using (_dataFilter.Disable<ISoftDelete>())
            {
                await _countries.DeleteDirectAsync(c => true);
                await _currencies.DeleteDirectAsync(c => true);
            }

            (await _currencies.GetCountAsync()).ShouldBe(0);
            (await _countries.GetCountAsync()).ShouldBe(0);

            await _contributor.SeedAsync(new DataSeedContext(TenantA));
            await FlushAsync();

            (await _currencies.GetCountAsync()).ShouldBe(178);
            (await _countries.GetCountAsync()).ShouldBe(249);
            var eur = (await _currencies.GetListAsync(c => c.IsDefault)).ShouldHaveSingleItem();
            eur.IsoCode.ShouldBe("EUR");
            eur.IsActive.ShouldBeTrue();
            (await _countries.GetAsync(c => c.IsoAlpha2 == "NL")).DefaultCurrencyId.ShouldBe(eur.Id);
            (await _countries.GetAsync(c => c.IsoAlpha2 == "AQ")).DefaultCurrencyId.ShouldBeNull();
            (await _countries.CountAsync(c => c.DefaultCurrencyId == null)).ShouldBe(1);
        });
    }
}
