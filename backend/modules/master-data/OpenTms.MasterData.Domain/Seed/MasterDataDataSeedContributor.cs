using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTms.MasterData.Reference;
using OpenTms.Platform.Localization;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace OpenTms.MasterData.Seed;

/// <summary>
/// Seeds the ISO reference data per tenant database (design 41, "Seed"; assessment §2.2), run by the
/// DbMigrator through ABP's data seeder. Idempotent on the natural key: missing rows are inserted, existing
/// rows get only their ISO-fixed fields re-asserted, soft-deleted rows are left deleted (the read of existing
/// keys disables the soft-delete filter only; the tenant filter stays on, constitution rule 1). Skips the host
/// (host-level rows are parked, assessment §4). Logs tenant id and counts only.
/// </summary>
public class MasterDataDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly MasterDataSeedData _seedData;
    private readonly IRepository<Currency, Guid> _currencies;
    private readonly IRepository<Country, Guid> _countries;
    private readonly IRepository<AddressType, Guid> _addressTypes;
    private readonly ICurrentTenant _currentTenant;
    private readonly IDataFilter _dataFilter;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ILogger<MasterDataDataSeedContributor> _logger;

    public MasterDataDataSeedContributor(
        MasterDataSeedData seedData,
        IRepository<Currency, Guid> currencies,
        IRepository<Country, Guid> countries,
        IRepository<AddressType, Guid> addressTypes,
        ICurrentTenant currentTenant,
        IDataFilter dataFilter,
        IGuidGenerator guidGenerator,
        ILogger<MasterDataDataSeedContributor> logger)
    {
        _seedData = seedData;
        _currencies = currencies;
        _countries = countries;
        _addressTypes = addressTypes;
        _currentTenant = currentTenant;
        _dataFilter = dataFilter;
        _guidGenerator = guidGenerator;
        _logger = logger;
    }

    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        if (context.TenantId == null)
        {
            /* Host-level master data (transport profiles) is parked: assessment §4. */
            return;
        }

        using (_currentTenant.Change(context.TenantId))
        {
            var tenantId = context.TenantId;
            var currencyIdsByCode = await SeedCurrenciesAsync(tenantId.Value);
            await SeedCountriesAsync(tenantId.Value, currencyIdsByCode);
            await SeedAddressTypesAsync(tenantId.Value);
        }
    }

    private async Task<Dictionary<string, Guid>> SeedCurrenciesAsync(Guid tenantId)
    {
        var records = await _seedData.GetCurrenciesAsync();

        List<Currency> existing;
        /* Soft-deleted rows included: a currency the tenant deleted is never re-inserted. The multi-tenancy
         * filter stays active; only ISoftDelete is disabled for this read (design 41, "Seed"). */
        using (_dataFilter.Disable<ISoftDelete>())
        {
            existing = await _currencies.GetListAsync();
        }

        var byCode = existing.ToDictionary(c => c.IsoCode, StringComparer.Ordinal);
        var toInsert = new List<Currency>();
        var hasDefault = existing.Any(c => c.IsDefault && !c.IsDeleted);

        foreach (var record in records)
        {
            var code = MasterDataCodes.NormalizeIsoAlpha3(record.IsoCode, "isoCode");
            if (byCode.TryGetValue(code, out var current))
            {
                if (current.IsDeleted)
                {
                    continue;
                }

                /* Re-assert the ISO-fixed fields only; names, symbol, activity and description are the tenant's. */
                current.SetIsoNumeric(record.IsoNumeric);
                current.SetMinorUnits(record.MinorUnits);
                await _currencies.UpdateAsync(current);
                continue;
            }

            var currency = new Currency(_guidGenerator.Create(), tenantId, code, LocalizedName.Create(record.Names), isActive: code == "EUR");
            currency.SetIsoNumeric(record.IsoNumeric);
            currency.SetMinorUnits(record.MinorUnits);
            currency.SetSymbol(record.Symbol);
            if (!hasDefault && code == "EUR")
            {
                currency.MarkDefault();
                hasDefault = true;
            }

            toInsert.Add(currency);
            byCode[code] = currency;
        }

        if (toInsert.Count > 0)
        {
            await _currencies.InsertManyAsync(toInsert);
        }

        _logger.LogInformation("Master Data seed for tenant {TenantId}: {Inserted} currencies inserted, {Existing} existing.", tenantId, toInsert.Count, existing.Count);

        return byCode.Where(p => !p.Value.IsDeleted).ToDictionary(p => p.Key, p => p.Value.Id, StringComparer.Ordinal);
    }

    private async Task SeedCountriesAsync(Guid tenantId, IReadOnlyDictionary<string, Guid> currencyIdsByCode)
    {
        var records = await _seedData.GetCountriesAsync();

        List<Country> existing;
        using (_dataFilter.Disable<ISoftDelete>())
        {
            existing = await _countries.GetListAsync();
        }

        var byAlpha2 = existing.ToDictionary(c => c.IsoAlpha2, StringComparer.Ordinal);
        var toInsert = new List<Country>();

        foreach (var record in records)
        {
            var alpha2 = MasterDataCodes.NormalizeIsoAlpha2(record.IsoAlpha2);
            if (byAlpha2.TryGetValue(alpha2, out var current))
            {
                if (current.IsDeleted)
                {
                    continue;
                }

                current.SetIsoCodes(alpha2, record.IsoAlpha3);
                current.SetIsoNumeric(record.IsoNumeric);
                await _countries.UpdateAsync(current);
                continue;
            }

            var country = new Country(_guidGenerator.Create(), tenantId, alpha2, record.IsoAlpha3, LocalizedName.Create(record.Names));
            country.SetIsoNumeric(record.IsoNumeric);
            var currencyCode = MasterDataCodes.Normalize(record.DefaultCurrency);
            if (currencyCode != null && currencyIdsByCode.TryGetValue(currencyCode, out var currencyId))
            {
                country.SetDefaultCurrency(currencyId);
            }

            country.SetPrimaryTimeZone(record.PrimaryTimeZoneId);
            toInsert.Add(country);
            byAlpha2[alpha2] = country;
        }

        if (toInsert.Count > 0)
        {
            await _countries.InsertManyAsync(toInsert);
        }

        _logger.LogInformation("Master Data seed for tenant {TenantId}: {Inserted} countries inserted, {Existing} existing.", tenantId, toInsert.Count, existing.Count);
    }

    private async Task SeedAddressTypesAsync(Guid tenantId)
    {
        var records = await _seedData.GetAddressTypesAsync();

        List<AddressType> existing;
        using (_dataFilter.Disable<ISoftDelete>())
        {
            existing = await _addressTypes.GetListAsync();
        }

        var toInsert = new List<AddressType>();
        foreach (var record in records)
        {
            var kind = Enum.Parse<AddressTypeKind>(record.Kind, ignoreCase: true);
            if (kind == AddressTypeKind.Other)
            {
                throw new InvalidOperationException("The address-type seed may only hold Visiting and Postal kinds.");
            }

            if (existing.Any(t => t.Kind == kind))
            {
                continue;
            }

            toInsert.Add(new AddressType(_guidGenerator.Create(), tenantId, kind, LocalizedName.Create(record.Names)));
        }

        if (toInsert.Count > 0)
        {
            await _addressTypes.InsertManyAsync(toInsert);
        }

        _logger.LogInformation("Master Data seed for tenant {TenantId}: {Inserted} address types inserted, {Existing} existing.", tenantId, toInsert.Count, existing.Count);
    }
}
