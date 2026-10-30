using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData;
using OpenTms.MasterData.Published;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Regions;
using OpenTms.MasterData.Relations;
using OpenTms.MasterData.Transport;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace OpenTms.EntityFrameworkCore.MasterData;

/// <summary>
/// The published lookup's additions of design 58 (module boundary rule 3): relation addresses with the relation's name,
/// postal fields, zone and the geographical regions they lie in (the relation's own regions plus the postal-code ranges),
/// the generic reference read per kind, and the own company; and design 61's contact persons with their relation (a trip
/// type's default contact must be its carrier's). Tenant-filtered like every read. Risk class: Critical.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class MasterDataReferenceLookupTests : MasterDataAppServiceTestBase
{
    private IMasterDataReferenceLookup Lookup => GetRequiredService<IMasterDataReferenceLookup>();

    [Fact]
    public async Task Addresses_come_with_their_relation_country_zone_and_regions()
    {
        using var _ = InTenantA();
        var country = await GetRequiredService<ICountryAppService>().CreateAsync(new CountryCreateDto { IsoAlpha2 = "NL", IsoAlpha3 = "NLD", Names = Names("Netherlands"), PrimaryTimeZoneId = "Europe/Amsterdam" });
        var regions = GetRequiredService<IGeographicalRegionAppService>();
        var limburg = await regions.CreateAsync(new GeographicalRegionCreateDto { Name = "Limburg", PostalCodeRanges = [new() { CountryId = country.Id, FromNumeric = 5800, TillNumeric = 6499 }] });
        var benelux = await regions.CreateAsync(new GeographicalRegionCreateDto { Name = "Benelux", PostalCodeRanges = [new() { CountryId = country.Id, Prefix = "5" }] });
        var elsewhere = await regions.CreateAsync(new GeographicalRegionCreateDto { Name = "Randstad", PostalCodeRanges = [new() { CountryId = country.Id, FromNumeric = 1000, TillNumeric = 3999 }] });
        var keyAccounts = await regions.CreateAsync(new GeographicalRegionCreateDto { Name = "Key accounts" });

        var relation = await GetRequiredService<IRelationAppService>().CreateAsync(new RelationCreateDto
        {
            Code = "DEPOT", Name = "DC Venlo", Roles = RelationRoles.Customer, GeographicalRegionIds = [keyAccounts.Id],
            Addresses =
            [
                new RelationAddressDto { CountryId = country.Id, Street = "Celsiusweg", HouseNumber = "12", HouseNumberSuffix = "a", PostalCode = "5928 PR", City = "Venlo", Latitude = 51.392, Longitude = 6.154 },
            ]
        });
        var addressId = relation.Addresses.Single().Id!.Value;

        var address = (await Lookup.GetRelationAddressesAsync([addressId, Guid.NewGuid()])).ShouldHaveSingleItem();
        address.RelationId.ShouldBe(relation.Id);
        address.RelationName.ShouldBe("DC Venlo");
        (address.Street, address.HouseNumber, address.PostalCode, address.City, address.CountryCode).ShouldBe(("Celsiusweg", "12a", "5928 PR", "Venlo", "NL"));
        address.Latitude.ShouldBe(51.392);
        address.TimeZoneId.ShouldBe("Europe/Amsterdam", "the country's primary zone when the address has none");
        address.GeographicalRegionIds.ShouldBe([keyAccounts.Id, limburg.Id, benelux.Id], ignoreOrder: true);
        address.GeographicalRegionIds.ShouldNotContain(elsewhere.Id);
        (await Lookup.GetRelationAddressesAsync([])).ShouldBeEmpty();

        var home = (await Lookup.GetRelationDefaultAddressesAsync([relation.Id, Guid.NewGuid()])).ShouldHaveSingleItem();
        home.Id.ShouldBe(addressId);
        home.City.ShouldBe("Venlo");
        home.GeographicalRegionIds.ShouldContain(limburg.Id);
    }

    [Fact]
    public async Task References_are_read_per_kind_with_their_archived_flag_and_the_own_company_is_found()
    {
        using var _ = InTenantA();
        var unit = await GetRequiredService<ITransportUnitAppService>().CreateAsync(new TransportUnitCreateDto { Name = "Europallet" });
        var product = await GetRequiredService<IProductAppService>().CreateAsync(new ProductCreateDto { Name = "Zuivel", IsArchived = true });
        var temperature = await GetRequiredService<ITemperatureAppService>().CreateAsync(new TemperatureCreateDto { Name = "Gekoeld", MinCelsius = 2, MaxCelsius = 7 });
        var own = await GetRequiredService<IRelationAppService>().CreateAsync(new RelationCreateDto { Code = "ME", Name = "Voorbeeld Transport", Roles = RelationRoles.OwnCompany });
        await GetRequiredService<IRelationAppService>().CreateAsync(new RelationCreateDto { Code = "KLANT", Name = "Klant", Roles = RelationRoles.Customer });

        (await Lookup.GetReferencesAsync(MasterDataReferenceKind.TransportUnit, [unit.Id, product.Id])).ShouldHaveSingleItem().Name.ShouldBe("Europallet");
        var archived = (await Lookup.GetReferencesAsync(MasterDataReferenceKind.Product, [product.Id])).ShouldHaveSingleItem();
        archived.IsArchived.ShouldBeTrue();
        (await Lookup.GetReferencesAsync(MasterDataReferenceKind.Temperature, [temperature.Id])).ShouldHaveSingleItem().Name.ShouldBe("Gekoeld");
        (await Lookup.GetReferencesAsync(MasterDataReferenceKind.Vehicle, [unit.Id])).ShouldBeEmpty("a unit is not a vehicle");
        (await Lookup.GetReferencesAsync(MasterDataReferenceKind.Driver, [])).ShouldBeEmpty();
        (await Lookup.GetOwnCompanyAsync()).ShouldNotBeNull().Id.ShouldBe(own.Id);
    }

    [Fact]
    public async Task Another_tenant_sees_none_of_it()
    {
        Guid addressId, unitId;
        using (InTenantA())
        {
            var country = await GetRequiredService<ICountryAppService>().CreateAsync(new CountryCreateDto { IsoAlpha2 = "NL", IsoAlpha3 = "NLD", Names = Names("Netherlands") });
            var relation = await GetRequiredService<IRelationAppService>().CreateAsync(new RelationCreateDto
            {
                Code = "ME", Name = "Own", Roles = RelationRoles.OwnCompany,
                Addresses = [new RelationAddressDto { CountryId = country.Id, Street = "Street", PostalCode = "1234 AB", City = "City" }]
            });
            addressId = relation.Addresses.Single().Id!.Value;
            unitId = (await GetRequiredService<ITransportUnitAppService>().CreateAsync(new TransportUnitCreateDto { Name = "Europallet" })).Id;
        }

        using (InTenantB())
        {
            (await Lookup.GetRelationAddressesAsync([addressId])).ShouldBeEmpty();
            (await Lookup.GetReferencesAsync(MasterDataReferenceKind.TransportUnit, [unitId])).ShouldBeEmpty();
            (await Lookup.GetOwnCompanyAsync()).ShouldBeNull();
        }
    }

    private async Task<(Guid RelationId, List<Guid> ContactIds)> RelationWithContactsAsync(string code, RelationRoles roles, params (string First, string? Middle, string Last)[] people)
    {
        var relations = GetRequiredService<IRelationAppService>();
        var relation = await relations.CreateAsync(new RelationCreateDto
        {
            Code = code, Name = code, Roles = roles,
            ContactPersons = people.Select(p => new RelationContactPersonDto { FirstName = p.First, MiddleName = p.Middle, LastName = p.Last, Role = ContactPersonRole.Planning }).ToList()
        });
        var contacts = (await relations.GetAsync(relation.Id)).ContactPersons;
        return (relation.Id, people.Select(p => contacts.Single(c => c.FirstName == p.First).Id!.Value).ToList());
    }

    [Fact]
    public async Task Contact_persons_come_with_their_relation_and_name_and_unknown_ids_are_absent()
    {
        using var _ = InTenantA();
        var (carrier, carrierContacts) = await RelationWithContactsAsync("CARR", RelationRoles.Carrier, ("Jan", "van", "Dijk"), ("Kees", null, "Visser"));
        var (customer, customerContacts) = await RelationWithContactsAsync("KLANT", RelationRoles.Customer, ("Anna", null, "Bakker"));
        var (jan, kees, anna) = (carrierContacts[0], carrierContacts[1], customerContacts[0]);

        var rows = await Lookup.GetContactPersonsAsync([jan, kees, anna, Guid.NewGuid(), Guid.Empty, jan, carrier]);

        rows.Select(r => (r.Id, r.RelationId, r.Name)).ShouldBe(
            [(jan, carrier, "Jan van Dijk"), (kees, carrier, "Kees Visser"), (anna, customer, "Anna Bakker")], ignoreOrder: true,
            "each contact once with its own relation; an unknown id, the empty id and a relation's id are absent");
        (await Lookup.GetContactPersonsAsync([Guid.NewGuid()])).ShouldBeEmpty();
        (await Lookup.GetContactPersonsAsync([])).ShouldBeEmpty();
        (await Lookup.GetContactPersonsAsync([Guid.Empty])).ShouldBeEmpty();
    }

    [Fact]
    public async Task Contact_persons_refuse_more_than_the_bound()
    {
        using var _ = InTenantA();
        var atBound = Enumerable.Range(0, IMasterDataReferenceLookup.MaxKeys).Select(_ => Guid.NewGuid()).ToList();
        (await Lookup.GetContactPersonsAsync(atBound)).ShouldBeEmpty("exactly the bound is read");

        var tooMany = Enumerable.Range(0, IMasterDataReferenceLookup.MaxKeys + 1).Select(_ => Guid.NewGuid()).ToList();
        var error = await Should.ThrowAsync<BusinessException>(() => Lookup.GetContactPersonsAsync(tooMany));
        error.Code.ShouldBe(MasterDataErrorCodes.TooManyRows);
        error.Data["max"].ShouldBe(IMasterDataReferenceLookup.MaxKeys);

        /* Duplicates count once: the bound is on distinct ids. */
        (await Lookup.GetContactPersonsAsync(atBound.Concat(atBound).ToList())).ShouldBeEmpty();
    }

    [Fact]
    public async Task Another_tenants_contact_persons_are_absent()
    {
        Guid contactOfA;
        using (InTenantA())
        {
            contactOfA = (await RelationWithContactsAsync("CARR", RelationRoles.Carrier, ("Kees", null, "Visser"))).ContactIds.Single();
            (await Lookup.GetContactPersonsAsync([contactOfA])).ShouldHaveSingleItem("visible in its own tenant");
        }

        using (InTenantB())
        {
            var (relationOfB, contactsOfB) = await RelationWithContactsAsync("CARR", RelationRoles.Carrier, ("Anna", null, "Bakker"));
            (await Lookup.GetContactPersonsAsync([contactOfA])).ShouldBeEmpty("tenant A's contact does not exist for tenant B");
            var mixed = (await Lookup.GetContactPersonsAsync([contactOfA, contactsOfB[0]])).ShouldHaveSingleItem();
            (mixed.Id, mixed.RelationId).ShouldBe((contactsOfB[0], relationOfB));
        }
    }
}
