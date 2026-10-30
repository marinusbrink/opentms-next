using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTms.MasterData.Drivers;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Incompatibilities;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Regions;
using OpenTms.MasterData.Relations;
using OpenTms.MasterData.Transport;
using OpenTms.Platform.TestData;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Localization;
using Volo.Abp.Uow;

namespace OpenTms.MasterData.TestData;

/// <summary>
/// Master Data's part of "Reset to dataset" (docs/designs/65-tenant-test-datasets.md). Wipes every business
/// master-data list of the tenant and generates the profile's relations (exactly <see cref="TestDatasetProfile.Relations"/>,
/// on real addresses) and a random count per other master-data type, through the domain constructors so every invariant
/// and unique key holds. Countries, currencies and address types are ISO/seeded reference data and stay. Publishes
/// <see cref="MasterDataTestDataset"/> for Financial, Orders and Planning &amp; Execution. Logs tenant id and counts only.
/// </summary>
[DisableAuditing]
public class MasterDataTestDatasetContributor : DomainService, ITestDatasetContributor
{
    public int Order => 100;

    protected TestDatasetWriter Writer => LazyServiceProvider.LazyGetRequiredService<TestDatasetWriter>();

    protected IRepository<TEntity> Repository<TEntity>()
        where TEntity : class, IEntity
        => LazyServiceProvider.LazyGetRequiredService<IRepository<TEntity>>();

    /// <summary>
    /// The order follows the Restrict foreign keys of MasterDataDbContext: combination members → vehicle, product
    /// relation code → relation/product/transport unit, vehicle → type/owner/depot, default driver → driver, driver →
    /// employer, load factor → vehicle type. Every child table (addresses, contact persons, bank accounts, external ids,
    /// join rows, members, load factors, qualifications, postcode ranges) and the incompatibility pairs go with their
    /// parent through a cascading foreign key.
    /// </summary>
    public virtual async Task WipeAsync(TestDatasetContext context)
    {
        await Writer.DeleteAllAsync(Repository<VehicleCombination>());
        await Writer.DeleteAllAsync(Repository<ProductRelationCode>());
        await Writer.DeleteAllAsync(Repository<Vehicle>());
        await Writer.DeleteAllAsync(Repository<Driver>());
        await Writer.DeleteAllAsync(Repository<TransportUnit>());
        await Writer.DeleteAllAsync(Repository<Product>());
        await Writer.DeleteAllAsync(Repository<Temperature>());
        await Writer.DeleteAllAsync(Repository<VehicleType>());
        await Writer.DeleteAllAsync(Repository<Relation>());
        await Writer.DeleteAllAsync(Repository<RelationCategory>());
        await Writer.DeleteAllAsync(Repository<GeographicalRegion>());
        await Writer.DeleteAllAsync(Repository<TransportCharacteristics>());
        Logger.LogInformation("Master Data business data wiped for tenant {TenantId}", context.TenantId);
    }

    public virtual async Task GenerateAsync(TestDatasetContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var run = await LoadReferenceDataAsync(context);

        await GenerateRelationCategoriesAsync(run);
        await GenerateTransportCharacteristicsAsync(run);
        await GenerateGeographicalRegionsAsync(run);
        await GenerateTemperaturesAsync(run);
        await GenerateVehicleTypesAsync(run);
        await GenerateTransportUnitsAsync(run);
        await GenerateProductsAsync(run);

        var relations = await Writer.InsertAsync(Repository<Relation>(), Relations(run));
        Logger.LogInformation(
            "Tenant {TenantId}: {Count} relations generated ({Elapsed}s)", context.TenantId, relations, (int)stopwatch.Elapsed.TotalSeconds);

        await GenerateDriversAsync(run);
        await GenerateFleetAsync(run);
        await GenerateProductRelationCodesAsync(run);
        await GenerateIncompatibilitiesAsync(run);

        context.Publish(run.Output);
        var o = run.Output;
        Logger.LogInformation(
            "Tenant {TenantId}: Master Data generated in {Elapsed}s: {Relations} relations, {Categories} relation categories, " +
            "{Characteristics} transport characteristics, {Regions} regions, {Temperatures} temperatures, {VehicleTypes} vehicle types, " +
            "{TransportUnits} transport units, {Products} products, {Drivers} drivers, {Vehicles} vehicles, {Combinations} combinations, " +
            "{ProductRelationCodes} product relation codes, {Incompatibilities} incompatibility pairs",
            context.TenantId, (int)stopwatch.Elapsed.TotalSeconds, o.Relations.Count, o.RelationCategoryIds.Count,
            o.TransportCharacteristicsIds.Count, o.GeographicalRegionIds.Count, o.TemperatureIds.Count, o.VehicleTypeIds.Count,
            o.TransportUnitIds.Count, o.ProductIds.Count, o.DriverIds.Count, o.VehicleIds.Count, o.VehicleCombinationIds.Count,
            run.ProductRelationCodeCount, run.IncompatibilityCount);
    }

    private async Task<Run> LoadReferenceDataAsync(TestDatasetContext context)
    {
        List<Country> countries;
        List<Currency> currencies;
        List<AddressType> addressTypes;
        using (var unitOfWork = LazyServiceProvider.LazyGetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: false))
        {
            /* Seeded reference lists: bounded (ISO), so read whole. */
            countries = await Repository<Country>().GetListAsync();
            currencies = await Repository<Currency>().GetListAsync();
            addressTypes = await Repository<AddressType>().GetListAsync();
            await unitOfWork.CompleteAsync();
        }

        var cultures = LazyServiceProvider.LazyGetRequiredService<IOptions<AbpLocalizationOptions>>().Value.Languages
            .Select(l => l.CultureName.ToLowerInvariant())
            .ToList();
        var run = new Run(context, cultures)
        {
            CountriesByCode = countries.GroupBy(c => c.IsoAlpha2, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal),
            EuroId = currencies.FirstOrDefault(c => c.IsoCode == "EUR")?.Id,
            VisitingTypeId = addressTypes.FirstOrDefault(t => t.Kind == AddressTypeKind.Visiting)?.Id,
            PostalTypeId = addressTypes.FirstOrDefault(t => t.Kind == AddressTypeKind.Postal)?.Id
        };

        foreach (var country in context.Addresses.Countries)
        {
            if (!run.CountriesByCode.ContainsKey(country))
            {
                throw new InvalidOperationException($"Country {country} of the test addresses is not in the tenant's country list.");
            }
        }

        foreach (var (code, country) in run.CountriesByCode)
        {
            run.Output.CountryIds[code] = country.Id;
            run.CountryCodeById[country.Id] = code;
        }

        foreach (var currency in currencies)
        {
            run.Output.CurrencyIds[currency.IsoCode] = currency.Id;
        }

        return run;
    }

    private async Task GenerateRelationCategoriesAsync(Run run)
    {
        var categories = MasterDataTestCatalog.TakeUnique(MasterDataTestCatalog.RelationCategoryCandidates(), run.Context.NextCount(), MasterDataConsts.MaxNameLength)
            .Select(c => new RelationCategory(GuidGenerator.Create(), run.TenantId, c.Name))
            .ToList();
        await Writer.InsertAsync(Repository<RelationCategory>(), categories);
        run.Output.RelationCategoryIds.AddRange(categories.Select(c => c.Id));
    }

    private async Task GenerateTransportCharacteristicsAsync(Run run)
    {
        var codes = new UniqueCodes();
        var characteristics = new List<TransportCharacteristics>();
        foreach (var (name, (template, code)) in MasterDataTestCatalog.TakeUnique(MasterDataTestCatalog.CharacteristicCandidates(), run.Context.NextCount(), MasterDataConsts.MaxNameLength))
        {
            var characteristic = new TransportCharacteristics(GuidGenerator.Create(), run.TenantId, codes.Next(code, MasterDataConsts.MaxCodeLength), name);
            characteristics.Add(characteristic);
            if (name == template.Name)
            {
                run.BaseCharacteristicIds[template.Code] = characteristic.Id;
            }
        }

        await Writer.InsertAsync(Repository<TransportCharacteristics>(), characteristics);
        run.Output.TransportCharacteristicsIds.AddRange(characteristics.Select(c => c.Id));
    }

    private async Task GenerateGeographicalRegionsAsync(Run run)
    {
        var regions = new List<GeographicalRegion>();
        foreach (var (name, template) in MasterDataTestCatalog.TakeUnique(MasterDataTestCatalog.RegionCandidates(), run.Context.NextCount(), MasterDataConsts.MaxNameLength))
        {
            var region = new GeographicalRegion(GuidGenerator.Create(), run.TenantId, name);
            if (run.CountriesByCode.TryGetValue(template.CountryCode, out var country))
            {
                var ranges = template.Prefix != null
                    ? new List<(Guid? Id, Guid CountryId, int? FromNumeric, int? TillNumeric, string? Prefix)> { (null, country.Id, null, null, template.Prefix) }
                    : template.Ranges.Select(r => ((Guid?)null, country.Id, (int?)r.From, (int?)r.Till, (string?)null)).ToList();
                region.SetPostalCodeRanges(ranges, GuidGenerator.Create);
            }

            regions.Add(region);
        }

        await Writer.InsertAsync(Repository<GeographicalRegion>(), regions);
        run.Output.GeographicalRegionIds.AddRange(regions.Select(r => r.Id));
    }

    private async Task GenerateTemperaturesAsync(Run run)
    {
        var temperatures = new List<Temperature>();
        foreach (var (name, template) in MasterDataTestCatalog.TakeUnique(MasterDataTestCatalog.TemperatureCandidates(), run.Context.NextCount(), MasterDataConsts.MaxNameLength))
        {
            var temperature = new Temperature(GuidGenerator.Create(), run.TenantId, name);
            temperature.SetRange(template.MinCelsius, template.MaxCelsius);
            temperature.IsArchived = run.Random.NextDouble() < 0.02;
            temperatures.Add(temperature);
            if (template.MaxCelsius <= -15)
            {
                run.FrozenTemperatureIds.Add(temperature.Id);
            }
            else if (template.MinCelsius >= 10)
            {
                run.WarmTemperatureIds.Add(temperature.Id);
            }
        }

        await Writer.InsertAsync(Repository<Temperature>(), temperatures);
        run.Output.TemperatureIds.AddRange(temperatures.Select(t => t.Id));
    }

    private async Task GenerateVehicleTypesAsync(Run run)
    {
        var r = run.Random;
        var types = new List<VehicleType>();
        foreach (var (name, (template, brand)) in MasterDataTestCatalog.TakeUnique(MasterDataTestCatalog.VehicleTypeCandidates(), run.Context.NextCount(), MasterDataConsts.MaxNameLength))
        {
            var type = new VehicleType(GuidGenerator.Create(), run.TenantId, name, template.Kind)
            {
                MaxEpq = template.MaxEpq,
                MaxLoadingMeters = template.MaxLoadingMeters,
                DefaultLengthCm = template.LengthCm,
                DefaultWidthCm = template.WidthCm,
                DefaultHeightCm = template.HeightCm,
                DefaultGrossVehicleWeightKg = template.GrossVehicleWeightKg
            };

            /* A brand variant costs a little more or less than the generic type. */
            type.SetCostPricePerKm(brand == null ? template.CostPricePerKm : decimal.Round(template.CostPricePerKm * (decimal)(0.92 + r.NextDouble() * 0.16), 2));
            type.SetCharacteristics(template.Characteristics.Where(run.BaseCharacteristicIds.ContainsKey).Select(c => run.BaseCharacteristicIds[c]));
            types.Add(type);
            run.VehicleTypes.Add(new GeneratedVehicleType(type.Id, template, brand));
        }

        await Writer.InsertAsync(Repository<VehicleType>(), types);
        run.Output.VehicleTypeIds.AddRange(types.Select(t => t.Id));
    }

    private async Task GenerateTransportUnitsAsync(Run run)
    {
        var r = run.Random;
        var loadable = run.VehicleTypes.Where(t => t.Template.MaxEpq != null).ToList();
        var units = new List<TransportUnit>();
        foreach (var (name, t) in MasterDataTestCatalog.TakeUnique(MasterDataTestCatalog.TransportUnitCandidates(), run.Context.NextCount(), MasterDataConsts.MaxNameLength))
        {
            var unit = new TransportUnit(GuidGenerator.Create(), run.TenantId, name)
            {
                LengthCm = t.LengthCm,
                WidthCm = t.WidthCm,
                HeightCm = t.HeightCm,
                TareWeightKg = t.TareWeightKg,
                MaxLoadKg = t.MaxLoadKg,
                IsStackable = t.IsStackable,
                MaxStackCount = t.IsStackable ? t.MaxStackCount : null,
                LoadingMeters = t.LoadingMeters,
                EpqFactor = t.EpqFactor,
                IsExchangeable = t.IsExchangeable,
                IsArchived = r.NextDouble() < 0.02
            };

            /* How many fit on a few vehicle types: floor positions over the unit's pallet-place factor. */
            if (t.EpqFactor is > 0 && loadable.Count > 0)
            {
                var factors = PickDistinct(r, loadable, r.Next(0, 4))
                    .Select(v => ((Guid?)null, v.Id, (double?)t.EpqFactor, (double?)Math.Floor(v.Template.MaxEpq!.Value / t.EpqFactor.Value)))
                    .ToList();
                unit.SetLoadFactors(factors, GuidGenerator.Create);
            }

            units.Add(unit);
            run.TransportUnits.Add((unit.Id, t.Use));
        }

        await Writer.InsertAsync(Repository<TransportUnit>(), units);
        run.Output.TransportUnitIds.AddRange(units.Select(u => u.Id));
    }

    private async Task GenerateProductsAsync(Run run)
    {
        var r = run.Random;
        var products = new List<Product>();
        foreach (var (name, (template, packaging, factor)) in MasterDataTestCatalog.TakeUnique(MasterDataTestCatalog.ProductCandidates(), run.Context.NextCount(), MasterDataConsts.MaxNameLength))
        {
            var product = new Product(GuidGenerator.Create(), run.TenantId, name)
            {
                DefaultQuantity = Math.Max(1, Math.Round(packaging.Quantity * factor)),
                DefaultColli = Math.Max(1, Math.Round(packaging.Colli * factor)),
                DefaultWeightKg = Math.Round(packaging.WeightKg * factor),
                DefaultVolumeM3 = Math.Round(packaging.VolumeM3 * factor, 2),
                DefaultLoadingMeters = packaging.LoadingMeters == null ? null : Math.Min(13.6, Math.Round(packaging.LoadingMeters.Value * factor, 2)),
                DefaultEpq = packaging.Epq == null ? null : Math.Min(33, Math.Round(packaging.Epq.Value * factor, 2)),
                DefaultLengthCm = packaging.LengthCm,
                DefaultWidthCm = packaging.WidthCm,
                DefaultHeightCm = packaging.HeightCm,
                IsArchived = r.NextDouble() < 0.02
            };

            if (template.Adr != null)
            {
                product.SetAdr(true, template.Adr);
            }

            products.Add(product);
            run.Products.Add((product.Id, template.Kind));
        }

        await Writer.InsertAsync(Repository<Product>(), products);
        run.Output.ProductIds.AddRange(products.Select(p => p.Id));
    }

    /// <summary>Built lazily: the writer commits every 1,000, so 150,000 relations never sit in one unit of work.</summary>
    private IEnumerable<Relation> Relations(Run run)
    {
        foreach (var roles in RolePlan(run.Context.Profile.Relations, run.Random))
        {
            yield return NewRelation(run, roles);
        }
    }

    /// <summary>
    /// Exactly one own company first (the partial unique index allows one), a handful of depots, about 4 % carriers,
    /// 3 % creditors (suppliers), 1 % customers that also haul for us, 0.5 % customers that also supply us; the rest
    /// are customers. Shuffled after the own company, so codes and creation order mix like a real list.
    /// </summary>
    private static RelationRoles[] RolePlan(int count, Random random)
    {
        var plan = new RelationRoles[count];
        var next = 0;
        void Add(int n, RelationRoles roles)
        {
            for (var i = 0; i < n && next < count; i++)
            {
                plan[next++] = roles;
            }
        }

        Add(1, RelationRoles.OwnCompany);
        Add(Math.Clamp(count / 5_000 + 5, 5, 30), RelationRoles.Depot);
        Add(count * 4 / 100, RelationRoles.Carrier);
        Add(count * 3 / 100, RelationRoles.Creditor);
        Add(count / 100, RelationRoles.Customer | RelationRoles.Carrier);
        Add(count / 200, RelationRoles.Customer | RelationRoles.Creditor);
        Add(count - next, RelationRoles.Customer);
        if (count > 1)
        {
            random.Shuffle(plan.AsSpan(1));
        }

        return plan;
    }

    private Relation NewRelation(Run run, RelationRoles roles)
    {
        var r = run.Random;
        var isOwnCompany = roles == RelationRoles.OwnCompany;
        var isDepot = roles == RelationRoles.Depot;
        var isCustomer = roles.HasFlag(RelationRoles.Customer);
        var isCarrier = roles.HasFlag(RelationRoles.Carrier);
        var addresses = run.Context.Addresses;
        var main = isOwnCompany && addresses.Countries.Contains("NL") ? addresses.Next("NL") : addresses.Next();
        var country = main.CountryCode;

        var name = MasterDataTestCatalog.Fit(RelationName(run, roles, main), MasterDataConsts.MaxNameLength);
        var code = run.RelationCodes.Next(TestNames.Code(isDepot ? $"Depot {main.City}" : name, 8), MasterDataConsts.MaxCodeLength);
        var relation = new Relation(GuidGenerator.Create(), run.TenantId, code, name, roles);
        if (isDepot && run.OwnCompanyName != null)
        {
            relation.SetName(name, MasterDataTestCatalog.Fit($"{run.OwnCompanyName}, vestiging {main.City}", MasterDataConsts.MaxNameLength));
        }

        var domain = TestNames.EmailDomain(isDepot && run.OwnCompanyName != null ? run.OwnCompanyName : name);
        relation.SetContact(
            TestNames.Phone(r, country),
            r.NextDouble() < 0.3 ? TestNames.Mobile(r, country) : null,
            $"{(isCarrier ? "planning" : isDepot ? "depot" : "info")}@{domain}",
            r.NextDouble() < 0.6 || isOwnCompany ? $"https://www.{domain}" : null);

        if (!isDepot)
        {
            relation.SetLegal(
                r.NextDouble() < 0.85 || isOwnCompany ? UniqueLegalNumber(run.ChamberOfCommerceNumbers, () => TestNames.RegistrationNumber(r, country)) : null,
                r.NextDouble() < 0.9 || isOwnCompany ? UniqueLegalNumber(run.VatNumbers, () => TestNames.VatNumber(r, country)) : null);
        }

        relation.SetAccountNumbers(
            isCustomer && r.NextDouble() < 0.92 ? (100_000 + ++run.DebtorSerial).ToString(CultureInfo.InvariantCulture) : null,
            (isCarrier || roles.HasFlag(RelationRoles.Creditor)) && r.NextDouble() < 0.95 ? (700_000 + ++run.CreditorSerial).ToString(CultureInfo.InvariantCulture) : null);
        if (isCarrier)
        {
            relation.SetCarrierCode(TestNames.Code(name, 4) + r.Next(10, 100).ToString(CultureInfo.InvariantCulture));
        }

        relation.SetCurrency(run.EuroId);
        relation.SetPreferredCulture(run.CultureOf(country), run.Cultures);
        if (isCustomer && r.NextDouble() < 0.15)
        {
            relation.SetDescription(TestNames.Pick(r, MasterDataTestCatalog.RelationRemarks));
        }

        SetAddresses(run, relation, roles, main);
        relation.SetContactPersons(ContactPersons(run, roles, country, domain), GuidGenerator.Create);
        if (!isDepot && r.NextDouble() < 0.88)
        {
            relation.SetBankAccounts(BankAccounts(run, name, country), GuidGenerator.Create);
        }

        if (isCustomer && r.NextDouble() < 0.12)
        {
            relation.SetExternalIdentifiers(new[] { new RelationExternalIdentifierInput(null, "GLN", MasterDataTestCatalog.Gln(++run.GlnSerial)) }, GuidGenerator.Create);
        }
        else if (isCarrier && r.NextDouble() < 0.3)
        {
            relation.SetExternalIdentifiers(new[] { new RelationExternalIdentifierInput(null, "Transporeon", $"TP{100_000 + ++run.PlatformSerial}") }, GuidGenerator.Create);
        }

        if (isCustomer)
        {
            var roll = r.NextDouble();
            relation.SetCategories(PickDistinct(r, run.Output.RelationCategoryIds, roll < 0.15 ? 2 : roll < 0.85 ? 1 : 0));
            if (r.NextDouble() < 0.05)
            {
                relation.SetTransportCharacteristics(PickDistinct(r, run.BaseCharacteristicIds.Values.ToList(), r.Next(1, 3)));
            }
        }

        if (isCarrier)
        {
            /* The regions a carrier serves and the equipment it offers. */
            relation.SetGeographicalRegions(PickDistinct(r, run.Output.GeographicalRegionIds, r.Next(1, 5)));
            relation.SetTransportCharacteristics(PickDistinct(r, run.BaseCharacteristicIds.Values.ToList(), r.Next(1, 5)));
        }

        var archived = false;
        if (!isOwnCompany && !isDepot)
        {
            if (r.NextDouble() < 0.02)
            {
                relation.SetBlocked(true, PickBlockReason(r));
            }

            if (r.NextDouble() < 0.03)
            {
                relation.Archive();
                archived = true;
            }
        }

        var output = relation.Addresses
            .Select(a => new TestRelationAddress(a.Id, run.CountryCodeById[a.CountryId], a.Street, a.HouseNumber, a.PostalCode, a.City, a.Latitude ?? 0, a.Longitude ?? 0, a.HouseNumberSuffix))
            .ToList();
        run.Output.Relations.Add(new TestRelation(relation.Id, relation.Code, relation.Name, roles, country, output));

        if (isOwnCompany)
        {
            run.OwnCompanyName = name;
            run.OwnCompanyDomain = domain;
            run.Output.OwnCompanyRelationId = relation.Id;
        }
        else if (!archived)
        {
            if (isDepot)
            {
                run.DepotIds.Add(relation.Id);
            }

            if (isCarrier)
            {
                run.Carriers.Add((relation.Id, country));
            }

            if (isCustomer)
            {
                run.Customers.Add((relation.Id, relation.Code));
            }
        }

        return relation;
    }

    private static string RelationName(Run run, RelationRoles roles, TestAddress main)
    {
        var r = run.Random;
        var country = main.CountryCode;
        var forms = country switch { "BE" => MasterDataTestCatalog.BeForms, "DE" => MasterDataTestCatalog.DeForms, _ => MasterDataTestCatalog.NlForms };
        var surname = MasterDataTestCatalog.Capitalize(TestNames.PersonLastName(r, country));
        if (roles == RelationRoles.OwnCompany)
        {
            return $"{surname} Transport en Logistiek B.V.";
        }

        if (roles == RelationRoles.Depot)
        {
            return $"Depot {main.City}";
        }

        if (roles == RelationRoles.Carrier)
        {
            var word = TestNames.Pick(r, country == "DE" ? MasterDataTestCatalog.CarrierWordsDe : MasterDataTestCatalog.CarrierWords);
            return country == "DE" ? $"{word} {surname} {TestNames.Pick(r, forms)}" : $"{surname} {word} {TestNames.Pick(r, forms)}";
        }

        if (roles == RelationRoles.Creditor)
        {
            var word = TestNames.Pick(r, country == "DE" ? MasterDataTestCatalog.SupplierWordsDe : MasterDataTestCatalog.SupplierWords);
            return r.NextDouble() < 0.4 ? $"{word} {main.City} {TestNames.Pick(r, forms)}" : $"{surname} {word} {TestNames.Pick(r, forms)}";
        }

        return TestNames.CompanyName(r, country, main.City);
    }

    /// <summary>
    /// The main address takes the Visiting type (what Orders and Planning ask for first). A customer's second site is the
    /// default address (no type: it serves every type without one of its own, e.g. a loading or delivery site); a
    /// postal address comes after it. Other relations add a postal address first.
    /// </summary>
    private void SetAddresses(Run run, Relation relation, RelationRoles roles, TestAddress main)
    {
        var r = run.Random;
        var isCustomer = roles.HasFlag(RelationRoles.Customer);
        var wanted = roles switch
        {
            RelationRoles.OwnCompany => 2,
            RelationRoles.Depot => 1,
            _ when isCustomer => Weighted(r, (1, 55), (2, 30), (3, 15)),
            _ => r.NextDouble() < 0.25 ? 2 : 1
        };

        var slots = isCustomer ? run.CustomerAddressSlots : run.OtherAddressSlots;
        var drawn = new List<TestAddress> { main };
        while (drawn.Count < Math.Min(wanted, slots.Count))
        {
            drawn.Add(r.NextDouble() < 0.8 ? run.Context.Addresses.Next(main.CountryCode) : run.Context.Addresses.Next());
        }

        relation.SetAddresses(
            drawn.Select((a, i) => new RelationAddressInput(
                null,
                slots[i],
                run.CountriesByCode[a.CountryCode].Id,
                MasterDataTestCatalog.Fit(a.Street, MasterDataConsts.MaxStreetLength),
                FitOrNull(a.HouseNumber, MasterDataConsts.MaxHouseNumberLength),
                FitOrNull(a.HouseNumberSuffix, MasterDataConsts.MaxHouseNumberLength),
                null,
                MasterDataTestCatalog.Fit(a.PostalCode, MasterDataConsts.MaxPostalCodeLength),
                MasterDataTestCatalog.Fit(a.City, MasterDataConsts.MaxCityLength),
                a.Latitude,
                a.Longitude,
                null)),
            GuidGenerator.Create);

        foreach (var address in relation.Addresses)
        {
            address.DefaultTimeZone(run.CountriesByCode[run.CountryCodeById[address.CountryId]].PrimaryTimeZoneId);
        }
    }

    private static List<RelationContactPersonInput> ContactPersons(Run run, RelationRoles roles, string country, string domain)
    {
        var r = run.Random;
        var count = roles == RelationRoles.Depot ? 1 : Weighted(r, (1, 45), (2, 35), (3, 20));
        var contacts = new List<RelationContactPersonInput>(count);
        for (var i = 0; i < count; i++)
        {
            var role = i switch
            {
                0 when roles == RelationRoles.OwnCompany => ContactPersonRole.Management,
                0 when roles == RelationRoles.Creditor => ContactPersonRole.Sales,
                0 => ContactPersonRole.Planning,
                1 => ContactPersonRole.Invoicing,
                _ => TestNames.Pick(r, new[] { ContactPersonRole.Sales, ContactPersonRole.Management, ContactPersonRole.Other })
            };
            var (title, department) = TestNames.Pick(r, role switch
            {
                ContactPersonRole.Invoicing => MasterDataTestCatalog.InvoicingTitles,
                ContactPersonRole.Sales => MasterDataTestCatalog.SalesTitles,
                ContactPersonRole.Management => MasterDataTestCatalog.ManagementTitles,
                _ => MasterDataTestCatalog.PlanningTitles
            });
            var firstName = TestNames.PersonFirstName(r, country);
            var surname = TestNames.PersonLastName(r, country);
            var (middleName, lastName) = MasterDataTestCatalog.SplitSurname(surname);
            contacts.Add(new RelationContactPersonInput(
                null, firstName, middleName, lastName, role, title, department,
                TestNames.Email(firstName, surname, domain),
                TestNames.Phone(r, country),
                r.NextDouble() < 0.7 ? TestNames.Mobile(r, country) : null));
        }

        return contacts;
    }

    private static List<RelationBankAccountInput> BankAccounts(Run run, string holder, string country)
    {
        var r = run.Random;
        var count = r.NextDouble() < 0.06 ? 2 : 1;
        var ibans = new List<string>(count);
        while (ibans.Count < count)
        {
            var iban = TestNames.Iban(r, country);
            if (!ibans.Contains(iban))
            {
                ibans.Add(iban);
            }
        }

        return ibans.Select((iban, i) =>
        {
            var bank = country == "NL" && MasterDataTestCatalog.NlBanks.TryGetValue(iban.Substring(4, 4), out var b) ? b : default;
            return new RelationBankAccountInput(null, iban, bank.Bic, bank.Name, MasterDataTestCatalog.Fit(holder, MasterDataConsts.MaxAccountHolderLength), i == 0);
        }).ToList();
    }

    private async Task GenerateDriversAsync(Run run)
    {
        var r = run.Random;
        var today = DateOnly.FromDateTime(run.Context.Today);
        var count = run.Context.NextCount();
        var drivers = new List<Driver>(count);
        for (var i = 0; i < count; i++)
        {
            var roll = r.NextDouble();
            var country = roll < 0.8 ? "NL" : roll < 0.9 ? "BE" : "DE";
            var firstName = TestNames.PersonFirstName(r, country);
            var surname = TestNames.PersonLastName(r, country);
            var (middleName, lastName) = MasterDataTestCatalog.SplitSurname(surname);
            var driver = new Driver(GuidGenerator.Create(), run.TenantId, $"D{i + 1:0000}", firstName, lastName);
            driver.SetPerson(firstName, middleName, lastName, $"{firstName[0]}.");
            driver.SetContact(null, TestNames.Mobile(r, country), r.NextDouble() < 0.6 && run.OwnCompanyDomain != null ? TestNames.Email(firstName, surname, run.OwnCompanyDomain) : null);
            driver.SetPreferredCulture(run.CultureOf(country), run.Cultures);

            /* Most drive for the own company; the rest are charter drivers employed by a carrier. */
            var employer = run.Carriers.Count > 0 && (run.Output.OwnCompanyRelationId == null || r.NextDouble() < 0.15)
                ? run.Carriers[r.Next(run.Carriers.Count)].Id
                : run.Output.OwnCompanyRelationId;
            driver.SetEmployer(employer);
            driver.SetQualifications(Qualifications(r, today, country), GuidGenerator.Create);
            if (r.NextDouble() < 0.04)
            {
                driver.Archive();
            }

            drivers.Add(driver);
            if (!driver.IsArchived)
            {
                run.ActiveDriverIds.Add(driver.Id);
            }
        }

        await Writer.InsertAsync(Repository<Driver>(), drivers);
        run.Output.DriverIds.AddRange(drivers.Select(d => d.Id));
    }

    /// <summary>Licence C or CE, code 95 and tachograph card for everyone; ADR and forklift for some; a few expired.</summary>
    private static List<DriverQualificationInput> Qualifications(Random r, DateOnly today, string country)
    {
        (DateOnly From, DateOnly Until) Validity(int years)
        {
            var from = today.AddDays(-r.Next(0, years * 365));
            var until = from.AddYears(years);
            return r.NextDouble() < 0.03 ? (from.AddYears(-years), today.AddDays(-r.Next(1, 60))) : (from, until);
        }

        var licence = Validity(10);
        var code95 = Validity(5);
        var card = Validity(5);
        var qualifications = new List<DriverQualificationInput>
        {
            new(null, r.NextDouble() < 0.9 ? DriverQualificationKind.DrivingLicenceCE : DriverQualificationKind.DrivingLicenceC, TestNames.Digits(r, 10), licence.From, licence.Until, null),
            new(null, DriverQualificationKind.Code95, null, code95.From, code95.Until, null),
            new(null, DriverQualificationKind.TachographCard, (country switch { "BE" => "B", "DE" => "D", _ => "NB" }) + TestNames.Digits(r, 14), card.From, card.Until, null)
        };

        if (r.NextDouble() < 0.25)
        {
            var adr = Validity(5);
            qualifications.Add(new(null, DriverQualificationKind.AdrCertificate, TestNames.Digits(r, 7), adr.From, adr.Until, null));
        }

        if (r.NextDouble() < 0.2)
        {
            var forklift = Validity(5);
            qualifications.Add(new(null, DriverQualificationKind.ForkliftLicence, null, forklift.From, forklift.Until, null));
        }

        return qualifications;
    }

    /// <summary>
    /// Vehicles and their combinations. Every combination couples its own motorised vehicle with its own trailer(s) (a
    /// vehicle is in one active combination at most), so the fleet holds at least two vehicles per combination; the
    /// vehicle count is the profile's random count or that minimum, whichever is larger.
    /// </summary>
    private async Task GenerateFleetAsync(Run run)
    {
        var r = run.Random;
        var combinationCount = run.Context.NextCount();
        var vehicleCount = Math.Max(run.Context.NextCount(), combinationCount * 2 + combinationCount / 10 + 10);
        var kinds = new List<VehicleTypeKind>(vehicleCount);
        for (var i = 0; i < combinationCount; i++)
        {
            kinds.Add(r.NextDouble() < 0.85 ? VehicleTypeKind.Tractor : VehicleTypeKind.Rigid);
        }

        for (var i = 0; i < combinationCount + combinationCount / 10; i++)
        {
            kinds.Add(VehicleTypeKind.Trailer);
        }

        while (kinds.Count < vehicleCount)
        {
            kinds.Add(Weighted(r, (VehicleTypeKind.Tractor, 30), (VehicleTypeKind.Trailer, 35), (VehicleTypeKind.Rigid, 20), (VehicleTypeKind.Van, 15)));
        }

        var typesByKind = run.VehicleTypes.GroupBy(t => t.Template.Kind).ToDictionary(g => g.Key, g => g.ToList());
        var fleetSystem = TestNames.Pick(r, MasterDataTestCatalog.FleetSystems);
        var numbers = new Dictionary<VehicleTypeKind, int>();
        var plates = new HashSet<string>(StringComparer.Ordinal);
        var vins = new HashSet<string>(StringComparer.Ordinal);
        var characteristicIds = run.BaseCharacteristicIds.Values.ToList();
        var vehicles = new List<(Vehicle Vehicle, VehicleTypeKind Kind)>(vehicleCount);
        var externalSerial = 0;

        for (var i = 0; i < kinds.Count; i++)
        {
            var kind = kinds[i];
            if (!typesByKind.TryGetValue(kind, out var candidates))
            {
                continue;
            }

            var type = candidates[r.Next(candidates.Count)];
            var t = type.Template;
            numbers[kind] = numbers.GetValueOrDefault(kind) + 1;
            var prefix = kind switch { VehicleTypeKind.Tractor => "T", VehicleTypeKind.Trailer => "O", VehicleTypeKind.Rigid => "B", _ => "V" };

            var own = run.Carriers.Count == 0 || r.NextDouble() < 0.85;
            var (ownerId, country) = own ? (run.Output.OwnCompanyRelationId!.Value, "NL") : run.Carriers[r.Next(run.Carriers.Count)];
            if (!run.CountriesByCode.ContainsKey(country))
            {
                country = run.CountriesByCode.ContainsKey("NL") ? "NL" : run.CountriesByCode.Keys.First();
            }

            var vehicle = new Vehicle(GuidGenerator.Create(), run.TenantId, $"{prefix}-{numbers[kind]:000}", type.Id, ownerId);
            string? plate = null;
            for (var attempt = 0; attempt < 20 && plate == null; attempt++)
            {
                VehicleRegistration.TryNormalizeLicencePlate(TestNames.LicensePlate(r, country), out var normalized);
                if (normalized != null && plates.Add($"{country}|{normalized}"))
                {
                    plate = normalized;
                }
            }

            var brand = type.Brand ?? TestNames.Pick(r, MasterDataTestCatalog.BrandsOf(kind));
            string vin;
            do
            {
                vin = MasterDataTestCatalog.Vin(r, brand);
            }
            while (!vins.Add(vin));

            vehicle.SetRegistration(plate, run.CountriesByCode[country].Id, vin);
            var homeDepot = own ? (run.DepotIds.Count > 0 && r.NextDouble() < 0.7 ? run.DepotIds[r.Next(run.DepotIds.Count)] : ownerId) : (Guid?)null;
            vehicle.SetOwnership(ownerId, homeDepot);
            vehicle.SetDimensions(new VehicleDimensions(t.LengthCm, t.WidthCm, t.HeightCm, t.GrossVehicleWeightKg, t.EmptyWeightKg, t.MaxEpq, t.MaxLoadingMeters, t.MaxPayloadKg));
            if (r.NextDouble() < 0.15)
            {
                vehicle.SetCostPricePerKm(decimal.Round(t.CostPricePerKm * (decimal)(0.9 + r.NextDouble() * 0.25), 2));
            }

            if (kind == VehicleTypeKind.Trailer)
            {
                vehicle.SetEngine(FuelType.NotApplicable, null);
            }
            else
            {
                var fuel = t.Fuel ?? Weighted(r, (FuelType.Diesel, 80), (FuelType.Hvo, 12), (FuelType.DieselElectricHybrid, 3), (FuelType.Lng, 5));
                var euro = fuel == FuelType.Electric ? EuroClass.ZeroEmission : Weighted(r, (EuroClass.Euro6, 82), (EuroClass.Euro5, 13), (EuroClass.Eev, 5));
                vehicle.SetEngine(fuel, euro);
                if (run.ActiveDriverIds.Count > 0 && r.NextDouble() < 0.7)
                {
                    vehicle.SetDefaultDrivers(PickDistinct(r, run.ActiveDriverIds, r.Next(1, 3)));
                }

                if (own && r.NextDouble() < 0.6)
                {
                    vehicle.SetExternalIdentifiers(new[] { new VehicleExternalIdentifierInput(null, fleetSystem, (100_000 + ++externalSerial).ToString(CultureInfo.InvariantCulture)) }, GuidGenerator.Create);
                }
            }

            if (characteristicIds.Count > 0 && r.NextDouble() < 0.2)
            {
                vehicle.SetTransportCharacteristics(PickDistinct(r, characteristicIds, 1));
            }

            vehicles.Add((vehicle, kind));
        }

        /* The first vehicles are the combinations' motorised members, the next ones their trailers. */
        var motorised = vehicles.Take(combinationCount).Where(v => v.Kind != VehicleTypeKind.Trailer).ToList();
        var trailers = new Queue<Vehicle>(vehicles.Skip(combinationCount).Where(v => v.Kind == VehicleTypeKind.Trailer).Select(v => v.Vehicle));
        var coupled = new HashSet<Guid>();
        var combinations = new List<VehicleCombination>();
        foreach (var (motor, kind) in motorised)
        {
            if (trailers.Count == 0)
            {
                break;
            }

            var members = new List<(Guid VehicleId, VehicleTypeKind Kind)> { (motor.Id, kind) };
            var names = new List<string> { motor.Name };
            /* Some tractors pull an LZV (eco-combi) with a second trailer. */
            var trailerCount = kind == VehicleTypeKind.Tractor && r.NextDouble() < 0.05 && trailers.Count > 1 ? 2 : 1;
            for (var i = 0; i < trailerCount; i++)
            {
                var trailer = trailers.Dequeue();
                members.Add((trailer.Id, VehicleTypeKind.Trailer));
                names.Add(trailer.Name);
            }

            var combination = new VehicleCombination(GuidGenerator.Create(), run.TenantId, MasterDataTestCatalog.Fit(string.Join(" + ", names), MasterDataConsts.MaxVehicleCombinationNameLength));
            combination.SetMembers(members);
            if (r.NextDouble() < 0.04)
            {
                combination.Archive();
            }

            combinations.Add(combination);
            coupled.UnionWith(members.Select(m => m.VehicleId));
        }

        foreach (var (vehicle, _) in vehicles)
        {
            if (!coupled.Contains(vehicle.Id) && r.NextDouble() < 0.03)
            {
                vehicle.Archive();
            }
        }

        await Writer.InsertAsync(Repository<Vehicle>(), vehicles.Select(v => v.Vehicle));
        await Writer.InsertAsync(Repository<VehicleCombination>(), combinations);
        run.Output.VehicleIds.AddRange(vehicles.Select(v => v.Vehicle.Id));
        run.Output.VehicleCombinationIds.AddRange(combinations.Select(c => c.Id));
    }

    /// <summary>Customers' article codes (EAN-13 or the customer's own "CODE-12345" style) mapped to product, unit and destination.</summary>
    private async Task GenerateProductRelationCodesAsync(Run run)
    {
        var r = run.Random;
        var count = run.Context.NextCount();
        var used = new HashSet<string>(StringComparer.Ordinal);
        var codes = new List<ProductRelationCode>(count);
        while (codes.Count < count)
        {
            var customer = run.Customers.Count > 0 ? run.Customers[r.Next(run.Customers.Count)] : default;
            var code = r.NextDouble() < 0.6 || customer.Code == null
                ? MasterDataTestCatalog.WithGs1CheckDigit("87" + TestNames.Digits(r, 10))
                : $"{customer.Code}-{TestNames.Digits(r, 5)}";
            if (!used.Add(ProductRelationCode.Normalize(code)!))
            {
                continue;
            }

            var relationId = customer.Code != null && r.NextDouble() < 0.8 ? customer.Id : (Guid?)null;
            var productId = run.Products.Count > 0 && r.NextDouble() < 0.9 ? run.Products[r.Next(run.Products.Count)].Id : (Guid?)null;
            var unitId = run.TransportUnits.Count > 0 && (r.NextDouble() < 0.7 || (relationId == null && productId == null)) ? run.TransportUnits[r.Next(run.TransportUnits.Count)].Id : (Guid?)null;
            if (relationId == null && productId == null && unitId == null)
            {
                continue;
            }

            var productRelationCode = new ProductRelationCode(GuidGenerator.Create(), run.TenantId, code);
            productRelationCode.SetTargets(relationId, productId, unitId);
            if (r.NextDouble() < 0.02)
            {
                productRelationCode.Archive();
            }

            codes.Add(productRelationCode);
        }

        run.ProductRelationCodeCount = await Writer.InsertAsync(Repository<ProductRelationCode>(), codes);
    }

    /// <summary>Frozen with warm temperatures, dangerous goods with food, chemical containers with food carriers.</summary>
    private async Task GenerateIncompatibilitiesAsync(Run run)
    {
        var r = run.Random;
        var temperatures = Pairs(r, run.FrozenTemperatureIds, run.WarmTemperatureIds, run.Context.NextCount())
            .Select(p => new TemperatureIncompatibility(run.TenantId, p.Left, p.Right));
        run.IncompatibilityCount += await Writer.InsertAsync(Repository<TemperatureIncompatibility>(), temperatures);

        var products = Pairs(
                r,
                run.Products.Where(p => p.Kind == GoodsKind.Chemical).Select(p => p.Id).ToList(),
                run.Products.Where(p => p.Kind == GoodsKind.Food).Select(p => p.Id).ToList(),
                run.Context.NextCount())
            .Select(p => new ProductIncompatibility(run.TenantId, p.Left, p.Right));
        run.IncompatibilityCount += await Writer.InsertAsync(Repository<ProductIncompatibility>(), products);

        var units = Pairs(
                r,
                run.TransportUnits.Where(u => u.Use == GoodsKind.Chemical).Select(u => u.Id).ToList(),
                run.TransportUnits.Where(u => u.Use == GoodsKind.Food).Select(u => u.Id).ToList(),
                run.Context.NextCount())
            .Select(p => new TransportUnitIncompatibility(run.TenantId, p.Left, p.Right));
        run.IncompatibilityCount += await Writer.InsertAsync(Repository<TransportUnitIncompatibility>(), units);
    }

    /// <summary>Up to <paramref name="count"/> distinct canonical pairs of one id from each list.</summary>
    private static List<(Guid Left, Guid Right)> Pairs(Random r, List<Guid> left, List<Guid> right, int count)
    {
        var pairs = new List<(Guid Left, Guid Right)>();
        if (left.Count == 0 || right.Count == 0)
        {
            return pairs;
        }

        count = (int)Math.Min(count, (long)left.Count * right.Count);
        var seen = new HashSet<(Guid, Guid)>();
        for (var attempt = 0; pairs.Count < count && attempt < count * 20; attempt++)
        {
            var a = left[r.Next(left.Count)];
            var b = right[r.Next(right.Count)];
            if (a == b)
            {
                continue;
            }

            var pair = Incompatibility.Canonical(a, b);
            if (seen.Add(pair))
            {
                pairs.Add(pair);
            }
        }

        return pairs;
    }

    private static string? UniqueLegalNumber(HashSet<string> used, Func<string> next)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var value = next();
            var key = MasterDataValues.NormalizeLegalNumber(value);
            if (key != null && used.Add(key))
            {
                return value;
            }
        }

        return null;
    }

    private static RelationBlockReason PickBlockReason(Random r) => Weighted(
        r,
        (RelationBlockReason.PaymentArrears, 50),
        (RelationBlockReason.CreditLimitExceeded, 25),
        (RelationBlockReason.Dispute, 15),
        (RelationBlockReason.Insolvency, 5),
        (RelationBlockReason.Other, 5));

    private static List<T> PickDistinct<T>(Random r, IReadOnlyList<T> source, int count)
    {
        var picked = new List<T>(count);
        count = Math.Min(count, source.Count);
        for (var attempt = 0; picked.Count < count && attempt < count * 10; attempt++)
        {
            var item = source[r.Next(source.Count)];
            if (!picked.Contains(item))
            {
                picked.Add(item);
            }
        }

        return picked;
    }

    private static T Weighted<T>(Random r, params (T Value, int Weight)[] options)
    {
        var roll = r.Next(options.Sum(o => o.Weight));
        foreach (var (value, weight) in options)
        {
            if (roll < weight)
            {
                return value;
            }

            roll -= weight;
        }

        return options[^1].Value;
    }

    private static string? FitOrNull(string? value, int maxLength)
        => string.IsNullOrWhiteSpace(value) ? null : MasterDataTestCatalog.Fit(value, maxLength);

    private sealed record GeneratedVehicleType(Guid Id, VehicleTypeTemplate Template, string? Brand);

    /// <summary>Codes made unique the way a user does it: "JANSEN", "JANSEN2", "JANSEN3", …</summary>
    private sealed class UniqueCodes
    {
        private readonly HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _next = new(StringComparer.OrdinalIgnoreCase);

        public string Next(string wanted, int maxLength)
        {
            var code = MasterDataTestCatalog.Fit(wanted.ToUpperInvariant(), maxLength);
            if (code.Length == 0)
            {
                code = "REL";
            }

            if (_used.Add(code))
            {
                return code;
            }

            for (var n = _next.GetValueOrDefault(code, 2); ; n++)
            {
                var suffix = n.ToString(CultureInfo.InvariantCulture);
                var candidate = MasterDataTestCatalog.Fit(code, maxLength - suffix.Length) + suffix;
                if (_used.Add(candidate))
                {
                    _next[code] = n + 1;
                    return candidate;
                }
            }
        }
    }

    /// <summary>The state of one generation run: reference ids, what later steps pick from, unique-key bookkeeping.</summary>
    private sealed class Run
    {
        public Run(TestDatasetContext context, IReadOnlyCollection<string> cultures)
        {
            Context = context;
            Cultures = cultures;
        }

        public TestDatasetContext Context { get; }

        public Random Random => Context.Random;

        public Guid? TenantId => Context.TenantId;

        public IReadOnlyCollection<string> Cultures { get; }

        public MasterDataTestDataset Output { get; } = new();

        public Dictionary<string, Country> CountriesByCode { get; init; } = new(StringComparer.Ordinal);

        public Dictionary<Guid, string> CountryCodeById { get; } = new();

        public Guid? EuroId { get; init; }

        public Guid? VisitingTypeId { get; init; }

        public Guid? PostalTypeId { get; init; }

        /// <summary>Address types in the order a customer's addresses take them: visiting, default (no type), postal.</summary>
        public List<Guid?> CustomerAddressSlots => _customerSlots ??= Slots(null, PostalTypeId);

        /// <summary>Other relations: visiting, postal, default.</summary>
        public List<Guid?> OtherAddressSlots => _otherSlots ??= Slots(PostalTypeId, null);

        public Dictionary<string, Guid> BaseCharacteristicIds { get; } = new(StringComparer.Ordinal);

        public List<GeneratedVehicleType> VehicleTypes { get; } = new();

        public List<(Guid Id, GoodsKind Use)> TransportUnits { get; } = new();

        public List<(Guid Id, GoodsKind Kind)> Products { get; } = new();

        public List<Guid> FrozenTemperatureIds { get; } = new();

        public List<Guid> WarmTemperatureIds { get; } = new();

        public UniqueCodes RelationCodes { get; } = new();

        public HashSet<string> VatNumbers { get; } = new(StringComparer.Ordinal);

        public HashSet<string> ChamberOfCommerceNumbers { get; } = new(StringComparer.Ordinal);

        public int DebtorSerial { get; set; }

        public int CreditorSerial { get; set; }

        public long GlnSerial { get; set; }

        public int PlatformSerial { get; set; }

        public string? OwnCompanyName { get; set; }

        public string? OwnCompanyDomain { get; set; }

        public List<Guid> DepotIds { get; } = new();

        public List<(Guid Id, string Country)> Carriers { get; } = new();

        public List<(Guid Id, string? Code)> Customers { get; } = new();

        public List<Guid> ActiveDriverIds { get; } = new();

        public int ProductRelationCodeCount { get; set; }

        public int IncompatibilityCount { get; set; }

        private List<Guid?>? _customerSlots;
        private List<Guid?>? _otherSlots;

        /// <summary>Dutch for NL and BE relations, English for the rest; only cultures the tenant has configured.</summary>
        public string? CultureOf(string countryCode)
        {
            var culture = countryCode is "NL" or "BE" ? "nl" : "en";
            return Cultures.Contains(culture) ? culture : null;
        }

        private List<Guid?> Slots(Guid? second, Guid? third)
        {
            var slots = new List<Guid?> { VisitingTypeId };
            foreach (var slot in new[] { second, third })
            {
                if (!slots.Contains(slot))
                {
                    slots.Add(slot);
                }
            }

            return slots;
        }
    }
}
