using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Regions;
using OpenTms.MasterData.Transport;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace OpenTms.PostgreSql.MasterData;

/// <summary>
/// Tenant isolation against real PostgreSQL (design 41, test risk analysis, Critical): rows written
/// by tenant A (own database) are unreachable from the shared-tier tenant (host database) and from
/// the host, through list, get, lookup, update, delete, delete-many and match; the shared tenant sees
/// its own rows in the host database; a reference from one tenant to another tenant's id is refused.
/// Constitution rule 1.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class MasterDataTenantIsolationTests : MasterDataPostgreSqlTestBase
{
    private readonly ICurrencyAppService _currencies;
    private readonly ICountryAppService _countries;
    private readonly IProductAppService _products;
    private readonly IGeographicalRegionAppService _regions;

    public MasterDataTenantIsolationTests()
    {
        _currencies = GetRequiredService<ICurrencyAppService>();
        _countries = GetRequiredService<ICountryAppService>();
        _products = GetRequiredService<IProductAppService>();
        _regions = GetRequiredService<IGeographicalRegionAppService>();
    }

    [Fact]
    public async Task A_currency_of_tenant_A_is_unreachable_from_the_shared_tenant_and_the_host()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var qqa = await _currencies.CreateAsync(new CurrencyCreateDto { IsoCode = "QQA", Names = Names("Isolation probe"), IsActive = true });
            var update = new CurrencyUpdateDto { IsoCode = "QQA", Names = Names("Changed"), IsActive = true, ConcurrencyStamp = qqa.ConcurrencyStamp };

            foreach (var other in new Guid?[] { SharedTenant, null })
            {
                using (CurrentTenant.Change(other))
                {
                    (await _currencies.GetListAsync(List<CurrencyListRequestDto>(search: "QQA"))).FilteredCount.ShouldBe(0);
                    (await _currencies.GetListAsync(List<CurrencyListRequestDto>(search: "isolation probe"))).Rows.ShouldBeEmpty();
                    (await _currencies.GetLookupAsync("QQA")).ShouldBeEmpty();
                    await Should.ThrowAsync<EntityNotFoundException>(() => _currencies.GetAsync(qqa.Id));
                    await Should.ThrowAsync<EntityNotFoundException>(() => _currencies.UpdateAsync(qqa.Id, update));
                    await Should.ThrowAsync<EntityNotFoundException>(() => _currencies.DeleteAsync(qqa.Id));
                    await Should.ThrowAsync<EntityNotFoundException>(() => _currencies.SetDefaultAsync(qqa.Id));
                    (await _currencies.BulkDeleteAsync(new CurrencyBulkDeleteRequestDto { Selection = Explicit(qqa.Id) })).DeletedCount.ShouldBe(0);
                    (await _currencies.BulkDeleteAsync(new CurrencyBulkDeleteRequestDto { Selection = FilterBased("QQA") })).DeletedCount.ShouldBe(0);
                    await FlushAsync();
                }
            }

            /* The shared tenant's own list is its own seed, not tenant A's. */
            using (CurrentTenant.Change(SharedTenant))
            {
                var page = await _currencies.GetListAsync(List<CurrencyListRequestDto>(end: 200));
                page.TotalCount.ShouldBe(178);
                page.Rows.ShouldNotContain(r => r.IsoCode == "QQA");
                page.Rows.Single(r => r.IsDefault).IsoCode.ShouldBe("EUR");
            }

            using (CurrentTenant.Change(null))
            {
                (await _currencies.GetListAsync(List<CurrencyListRequestDto>())).TotalCount.ShouldBe(0);
            }

            (await _currencies.GetAsync(qqa.Id)).Name.ShouldBe("Isolation probe");
            (await _currencies.GetListAsync(List<CurrencyListRequestDto>())).TotalCount.ShouldBe(179);
        });
    }

    [Fact]
    public async Task A_product_of_tenant_A_is_unreachable_from_the_shared_tenant_and_the_host()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var probe = await _products.CreateAsync(new ProductCreateDto { Name = "Isolation probe product" });
            var update = new ProductUpdateDto { Name = "Changed", ConcurrencyStamp = probe.ConcurrencyStamp };

            foreach (var other in new Guid?[] { SharedTenant, null })
            {
                using (CurrentTenant.Change(other))
                {
                    (await _products.GetListAsync(new ArchivableListRequestDto { IncludeArchived = true })).TotalCount.ShouldBe(0);
                    (await _products.GetListAsync(List<ArchivableListRequestDto>(search: "isolation"))).Rows.ShouldBeEmpty();
                    (await _products.GetLookupAsync()).ShouldBeEmpty();
                    await Should.ThrowAsync<EntityNotFoundException>(() => _products.GetAsync(probe.Id));
                    await Should.ThrowAsync<EntityNotFoundException>(() => _products.UpdateAsync(probe.Id, update));
                    await Should.ThrowAsync<EntityNotFoundException>(() => _products.DeleteAsync(probe.Id));
                    (await _products.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = Explicit(probe.Id) })).DeletedCount.ShouldBe(0);
                    (await _products.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = FilterBased(), IncludeArchived = true })).DeletedCount.ShouldBe(0);
                    await FlushAsync();

                    /* An incompatibility may not reach into tenant A. */
                    var ex = await Should.ThrowAsync<BusinessException>(() => _products.CreateAsync(new ProductCreateDto { Name = "Other", IncompatibleProductIds = [probe.Id] }));
                    ex.Code.ShouldBe(MasterDataErrorCodes.ReferenceNotFound);
                }
            }

            /* The shared tenant keeps its own rows in the host database, invisible to the host. (The refused
             * creates above left their "Other" row inside this test's outer transaction, which the service's own
             * transaction would have rolled back: MasterDataTransactionTests covers that; hence the search.) */
            using (CurrentTenant.Change(SharedTenant))
            {
                var own = await _products.CreateAsync(new ProductCreateDto { Name = "Isolation probe product" });
                own.Id.ShouldNotBe(probe.Id);
                (await _products.GetListAsync(List<ArchivableListRequestDto>(search: "isolation probe"))).Rows.ShouldHaveSingleItem().Id.ShouldBe(own.Id);
                (await _products.GetLookupAsync("isolation probe")).ShouldHaveSingleItem().Id.ShouldBe(own.Id);
            }

            using (CurrentTenant.Change(null))
            {
                (await _products.GetListAsync(List<ArchivableListRequestDto>(search: "isolation probe"))).FilteredCount.ShouldBe(0);
            }

            (await _products.GetListAsync(List<ArchivableListRequestDto>(search: "isolation probe"))).Rows.ShouldHaveSingleItem().Id.ShouldBe(probe.Id);
        });
    }

    [Fact]
    public async Task A_region_of_tenant_A_is_unreachable_from_the_shared_tenant_and_the_host_including_the_match()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var nl = (await _countries.GetListAsync(List<MasterDataListRequestDto>(search: "NLD"))).Rows.Single(r => r.IsoAlpha3 == "NLD");
            var region = await _regions.CreateAsync(new GeographicalRegionCreateDto
            {
                Name = "Isolation probe region",
                PostalCodeRanges = [new GeographicalRegionPostalCodeRangeDto { CountryId = nl.Id, FromNumeric = 1000, TillNumeric = 1999 }]
            });
            (await _regions.GetMatchesAsync(nl.Id, "1234 AB")).ShouldHaveSingleItem().Id.ShouldBe(region.Id);
            var update = new GeographicalRegionUpdateDto { Name = "Changed", ConcurrencyStamp = region.ConcurrencyStamp };

            foreach (var other in new Guid?[] { SharedTenant, null })
            {
                using (CurrentTenant.Change(other))
                {
                    (await _regions.GetListAsync(List<GeographicalRegionListRequestDto>())).TotalCount.ShouldBe(0);
                    (await _regions.GetListAsync(new GeographicalRegionListRequestDto { CountryId = nl.Id })).TotalCount.ShouldBe(0);
                    (await _regions.GetLookupAsync()).ShouldBeEmpty();
                    (await _regions.GetMatchesAsync(nl.Id, "1234 AB")).ShouldBeEmpty();
                    await Should.ThrowAsync<EntityNotFoundException>(() => _regions.GetAsync(region.Id));
                    await Should.ThrowAsync<EntityNotFoundException>(() => _regions.UpdateAsync(region.Id, update));
                    await Should.ThrowAsync<EntityNotFoundException>(() => _regions.DeleteAsync(region.Id));
                    (await _regions.BulkDeleteAsync(new GeographicalRegionBulkDeleteRequestDto { Selection = Explicit(region.Id) })).DeletedCount.ShouldBe(0);
                    (await _regions.BulkDeleteAsync(new GeographicalRegionBulkDeleteRequestDto { Selection = FilterBased() })).DeletedCount.ShouldBe(0);
                    await FlushAsync();

                    /* A range may not point at tenant A's country row. */
                    var ex = await Should.ThrowAsync<BusinessException>(() => _regions.CreateAsync(new GeographicalRegionCreateDto
                    {
                        Name = "Other",
                        PostalCodeRanges = [new GeographicalRegionPostalCodeRangeDto { CountryId = nl.Id, FromNumeric = 1000, TillNumeric = 1999 }]
                    }));
                    ex.Code.ShouldBe(MasterDataErrorCodes.ReferenceNotFound);
                    await Should.ThrowAsync<EntityNotFoundException>(() => _countries.GetAsync(nl.Id));
                }
            }

            /* The shared tenant matches against its own country row only. */
            using (CurrentTenant.Change(SharedTenant))
            {
                var sharedNl = (await _countries.GetListAsync(List<MasterDataListRequestDto>(search: "NLD"))).Rows.Single(r => r.IsoAlpha3 == "NLD");
                sharedNl.Id.ShouldNotBe(nl.Id);
                var own = await _regions.CreateAsync(new GeographicalRegionCreateDto
                {
                    Name = "Isolation probe region",
                    PostalCodeRanges = [new GeographicalRegionPostalCodeRangeDto { CountryId = sharedNl.Id, FromNumeric = 1000, TillNumeric = 1999 }]
                });
                (await _regions.GetMatchesAsync(sharedNl.Id, "1234 AB")).ShouldHaveSingleItem().Id.ShouldBe(own.Id);
                (await _regions.GetMatchesAsync(nl.Id, "1234 AB")).ShouldBeEmpty();
            }

            (await _regions.GetMatchesAsync(nl.Id, "1234 AB")).ShouldHaveSingleItem().Id.ShouldBe(region.Id);
            (await _regions.GetListAsync(List<GeographicalRegionListRequestDto>())).Rows.ShouldHaveSingleItem().RangeCount.ShouldBe(1);
        });
    }

    [Fact]
    public async Task The_shared_tenant_cannot_reach_a_currency_of_tenant_A_through_a_country_reference()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var eurOfA = (await _currencies.GetLookupAsync()).Single(l => l.Code == "EUR");

            using (CurrentTenant.Change(SharedTenant))
            {
                var eurOfShared = (await _currencies.GetLookupAsync()).Single(l => l.Code == "EUR");
                eurOfShared.Id.ShouldNotBe(eurOfA.Id);

                var ex = await Should.ThrowAsync<BusinessException>(() => _countries.CreateAsync(new CountryCreateDto
                {
                    IsoAlpha2 = "QQ", IsoAlpha3 = "QQQ", Names = Names("Probe"), DefaultCurrencyId = eurOfA.Id
                }));
                ex.Code.ShouldBe(MasterDataErrorCodes.ReferenceNotFound);

                (await _countries.CreateAsync(new CountryCreateDto { IsoAlpha2 = "QQ", IsoAlpha3 = "QQQ", Names = Names("Probe"), DefaultCurrencyId = eurOfShared.Id }))
                    .DefaultCurrencyCode.ShouldBe("EUR");
            }
        });
    }

    [Fact]
    public async Task The_default_country_of_tenant_A_is_unreachable_from_the_shared_tenant_and_the_host()
    {
        /* PO 2026-10-13: GetDefault and SetDefault are tenant-scoped like every other read and write (constitution rule 1). */
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var nl = (await _countries.GetListAsync(List<MasterDataListRequestDto>(search: "NLD"))).Rows.Single(r => r.IsoAlpha3 == "NLD");
            (await _countries.GetDefaultAsync()).ShouldBeNull("the seed sets no default country");
            await _countries.SetDefaultAsync(nl.Id);
            (await _countries.GetDefaultAsync())!.Id.ShouldBe(nl.Id);

            foreach (var other in new Guid?[] { SharedTenant, null })
            {
                using (CurrentTenant.Change(other))
                {
                    (await _countries.GetDefaultAsync()).ShouldBeNull(other == null ? "tenant A's default leaks into the host" : "tenant A's default leaks into the shared tenant");
                    await Should.ThrowAsync<EntityNotFoundException>(() => _countries.SetDefaultAsync(nl.Id));
                    (await _countries.GetListAsync(List<MasterDataListRequestDto>(sortBy: "isDefault", sortDir: "desc", end: 5))).Rows.ShouldAllBe(r => !r.IsDefault);
                    await FlushAsync();
                }
            }

            /* The shared tenant picks its own default: its own NL row, in the host database. */
            using (CurrentTenant.Change(SharedTenant))
            {
                var sharedNl = (await _countries.GetListAsync(List<MasterDataListRequestDto>(search: "NLD"))).Rows.Single(r => r.IsoAlpha3 == "NLD");
                sharedNl.Id.ShouldNotBe(nl.Id);
                await _countries.SetDefaultAsync(sharedNl.Id);
                (await _countries.GetDefaultAsync())!.Id.ShouldBe(sharedNl.Id);
                var top = (await _countries.GetListAsync(List<MasterDataListRequestDto>(sortBy: "isDefault", sortDir: "desc", end: 5))).Rows;
                top.First().Id.ShouldBe(sharedNl.Id);
                top.Count(r => r.IsDefault).ShouldBe(1);
            }

            using (CurrentTenant.Change(null))
            {
                (await _countries.GetDefaultAsync()).ShouldBeNull("the shared tenant's default is not the host's");
            }

            (await _countries.GetDefaultAsync())!.Id.ShouldBe(nl.Id);
            (await _countries.GetListAsync(List<MasterDataListRequestDto>(sortBy: "isDefault", sortDir: "desc", end: 5))).Rows.First().Id.ShouldBe(nl.Id);
        });
    }
}
