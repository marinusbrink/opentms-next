using System;
using System.Threading.Tasks;
using OpenTms.Financial;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Regions;
using MasterDataNames = OpenTms.MasterData.LocalizedNameDto;

namespace OpenTms.EntityFrameworkCore.Financial;

/// <summary>
/// Shared plumbing for the Financial master-data tests on SQLite (design 52): the Master Data rows the services refer
/// to through IMasterDataReferenceLookup (countries and currencies by ISO code, regions by id) are created through
/// the Master Data services in the same tenant, since SQLite tests run no seed. Today is the date the services use.
/// </summary>
public abstract class FinancialAppServiceTestBase : ReferenceDataAppServiceTestBase
{
    protected DateOnly Today { get; }

    protected FinancialAppServiceTestBase()
    {
        Today = GetRequiredService<FinancialToday>().Get();
    }

    protected Task CountryAsync(string isoAlpha2, string isoAlpha3)
        => GetRequiredService<ICountryAppService>().CreateAsync(new CountryCreateDto
        {
            IsoAlpha2 = isoAlpha2,
            IsoAlpha3 = isoAlpha3,
            Names = new MasterDataNames { ["en"] = isoAlpha2 },
            PrimaryTimeZoneId = "Europe/Amsterdam"
        });

    protected Task CurrencyAsync(string isoCode, bool active = true)
        => GetRequiredService<ICurrencyAppService>().CreateAsync(new CurrencyCreateDto
        {
            IsoCode = isoCode,
            Names = new MasterDataNames { ["en"] = isoCode },
            IsActive = active,
            IsoNumeric = 1,
            MinorUnits = 2
        });

    protected async Task<Guid> RegionAsync(string name)
        => (await GetRequiredService<IGeographicalRegionAppService>().CreateAsync(new GeographicalRegionCreateDto { Name = name })).Id;
}
