using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Features;
using OpenTms.MasterData;
using OpenTms.MasterData.Drivers;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Regions;
using OpenTms.MasterData.Relations;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Xunit;

namespace OpenTms.EntityFrameworkCore.MasterData;

/// <summary>
/// RelationAppService on SQLite (design 43). Risk class: Critical for tenant isolation, the own-company
/// rule, the natural keys (code, VAT, CoC, address type, IBAN) and the feature flag; High for the CRUD
/// with child replace semantics, the time-zone default, the list, the lookup page and the bulk delete.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class RelationAppServiceTests : MasterDataAppServiceTestBase
{
    private readonly IRelationAppService _service;
    private readonly IDriverAppService _drivers;
    private readonly ICountryAppService _countries;
    private readonly IAddressTypeAppService _addressTypes;
    private readonly IRelationCategoryAppService _categories;
    private readonly IGeographicalRegionAppService _regions;
    private readonly OrganizationUnitManager _organizationUnits;
    private readonly IRepository<RelationAddress, Guid> _addressRows;
    private readonly IRepository<RelationContactPerson, Guid> _contactRows;
    private readonly IRepository<RelationBankAccount, Guid> _bankRows;
    private readonly IRepository<RelationCategoryMembership> _membershipRows;

    private Guid _nl;
    private Guid _be;
    private Guid _visiting;
    private Guid _postal;
    private Guid _warehouse;
    private Guid _category;
    private Guid _region;
    private Guid _unit;

    public RelationAppServiceTests()
    {
        _service = GetRequiredService<IRelationAppService>();
        _drivers = GetRequiredService<IDriverAppService>();
        _countries = GetRequiredService<ICountryAppService>();
        _addressTypes = GetRequiredService<IAddressTypeAppService>();
        _categories = GetRequiredService<IRelationCategoryAppService>();
        _regions = GetRequiredService<IGeographicalRegionAppService>();
        _organizationUnits = GetRequiredService<OrganizationUnitManager>();
        _addressRows = GetRequiredService<IRepository<RelationAddress, Guid>>();
        _contactRows = GetRequiredService<IRepository<RelationContactPerson, Guid>>();
        _bankRows = GetRequiredService<IRepository<RelationBankAccount, Guid>>();
        _membershipRows = GetRequiredService<IRepository<RelationCategoryMembership>>();
    }

    // ── set-up ───────────────────────────────────────────────────────────────

    /// <summary>The reference rows of the current tenant every relation test needs (run-1 entities).</summary>
    private async Task SeedReferencesAsync()
    {
        _nl = (await _countries.CreateAsync(new CountryCreateDto { IsoAlpha2 = "NL", IsoAlpha3 = "NLD", Names = Names("Netherlands", "Nederland"), PrimaryTimeZoneId = "Europe/Amsterdam" })).Id;
        _be = (await _countries.CreateAsync(new CountryCreateDto { IsoAlpha2 = "BE", IsoAlpha3 = "BEL", Names = Names("Belgium", "België"), PrimaryTimeZoneId = "Europe/Brussels" })).Id;
        _visiting = (await _addressTypes.CreateAsync(new AddressTypeCreateDto { Kind = AddressTypeKind.Visiting, Names = Names("Visiting address", "Bezoekadres") })).Id;
        _postal = (await _addressTypes.CreateAsync(new AddressTypeCreateDto { Kind = AddressTypeKind.Postal, Names = Names("Postal address", "Postadres") })).Id;
        _warehouse = (await _addressTypes.CreateAsync(new AddressTypeCreateDto { Kind = AddressTypeKind.Other, Names = Names("Warehouse", "Magazijn") })).Id;
        _category = (await _categories.CreateAsync(new RelationCategoryCreateDto { Name = "Key accounts" })).Id;
        _region = (await _regions.CreateAsync(new GeographicalRegionCreateDto { Name = "Randstad" })).Id;
        _unit = Guid.NewGuid();
        await WithUnitOfWorkAsync(() => _organizationUnits.CreateAsync(new OrganizationUnit(_unit, "Depot Utrecht", null, CurrentTenant.Id)));
    }

    /// <summary>A null <paramref name="typeId"/> is the relation's default address ("all addresses equal", PO 2026-10-12).</summary>
    private RelationAddressDto Address(Guid? typeId, string street = "Keizersgracht", string postalCode = "1015 CJ", string city = "Amsterdam", Guid? countryId = null, string? timeZone = null, Guid? id = null) => new()
    {
        Id = id,
        AddressTypeId = typeId,
        CountryId = countryId ?? _nl,
        Street = street,
        HouseNumber = "12a",
        PostalCode = postalCode,
        City = city,
        TimeZoneId = timeZone
    };

    private static RelationContactPersonDto Contact(string first, string last, string? email = null, ContactPersonRole role = ContactPersonRole.Planning, Guid? id = null)
        => new() { Id = id, FirstName = first, LastName = last, Email = email, Role = role };

    private static RelationBankAccountDto Bank(string iban, bool isDefault = false, Guid? id = null)
        => new() { Id = id, Iban = iban, IsDefault = isDefault };

    private static RelationCreateDto Dto(string code, string name, RelationRoles roles = RelationRoles.Customer)
        => new() { Code = code, Name = name, Roles = roles };

    private static RelationCreateDto Make(string code, string name, RelationRoles roles, Action<RelationCreateDto>? configure)
    {
        var dto = Dto(code, name, roles);
        configure?.Invoke(dto);
        return dto;
    }

    private Task<RelationDto> CreateAsync(string code, string name, RelationRoles roles = RelationRoles.Customer, Action<RelationCreateDto>? configure = null)
        => _service.CreateAsync(Make(code, name, roles, configure));

    private static RelationUpdateDto UpdateFrom(RelationDto dto) => new()
    {
        Code = dto.Code,
        Name = dto.Name,
        Roles = dto.Roles,
        IsArchived = dto.IsArchived,
        IsBlocked = dto.IsBlocked,
        Phone = dto.Phone,
        Mobile = dto.Mobile,
        Email = dto.Email,
        Website = dto.Website,
        ChamberOfCommerceNumber = dto.ChamberOfCommerceNumber,
        VatNumber = dto.VatNumber,
        PreferredCulture = dto.PreferredCulture,
        Description = dto.Description,
        Addresses = dto.Addresses.Select(a => new RelationAddressDto
        {
            Id = a.Id, AddressTypeId = a.AddressTypeId, CountryId = a.CountryId, Street = a.Street, HouseNumber = a.HouseNumber, AddressLine2 = a.AddressLine2,
            PostalCode = a.PostalCode, City = a.City, Latitude = a.Latitude, Longitude = a.Longitude, TimeZoneId = a.TimeZoneId
        }).ToList(),
        ContactPersons = dto.ContactPersons.Select(c => new RelationContactPersonDto
        {
            Id = c.Id, FirstName = c.FirstName, MiddleName = c.MiddleName, LastName = c.LastName, Role = c.Role, JobTitle = c.JobTitle, Department = c.Department, Email = c.Email, Phone = c.Phone, Mobile = c.Mobile
        }).ToList(),
        BankAccounts = dto.BankAccounts.Select(b => new RelationBankAccountDto { Id = b.Id, Iban = b.Iban, Bic = b.Bic, BankName = b.BankName, AccountHolder = b.AccountHolder, IsDefault = b.IsDefault }).ToList(),
        CategoryIds = [.. dto.CategoryIds],
        GeographicalRegionIds = [.. dto.GeographicalRegionIds],
        OrganizationUnitIds = [.. dto.OrganizationUnitIds],
        ConcurrencyStamp = dto.ConcurrencyStamp
    };

    private static RelationListRequestDto Request(string? search = null, string? sortBy = null, string? sortDir = null, RelationRoles? roles = null, bool includeArchived = false, Guid? categoryId = null, Guid? unitId = null, int start = 0, int end = 100)
    {
        var request = List<RelationListRequestDto>(start, end, search, sortBy, sortDir);
        request.Roles = roles;
        request.IncludeArchived = includeArchived;
        request.CategoryId = categoryId;
        request.OrganizationUnitId = unitId;
        return request;
    }

    private static async Task<BusinessException> ShouldFailAsync(Func<Task> action, string code, string? field = null)
    {
        var ex = await Should.ThrowAsync<BusinessException>(action);
        ex.Code.ShouldBe(code);
        if (field != null)
        {
            ex.Data["field"].ShouldBe(field);
        }

        return ex;
    }

    // ── CRUD ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_get_list_lookup_and_addresses_round_trip_with_every_child_and_join()
    {
        using var _ = InTenantA();
        using var culture = UiCulture("en");
        await SeedReferencesAsync();

        var created = await CreateAsync(" acm01 ", " ACME Transport ", RelationRoles.Customer | RelationRoles.Carrier, dto =>
        {
            dto.Phone = " 020-1234567 ";
            dto.Mobile = "06-12345678";
            dto.Email = " planning@acme.nl ";
            dto.Website = "https://acme.nl";
            dto.ChamberOfCommerceNumber = " 12 34 56 78 ";
            dto.VatNumber = " nl 8012.34.567.b01 ";
            dto.PreferredCulture = " NL ";
            dto.Description = " key account ";
            dto.Addresses = [Address(_visiting), Address(_postal, "Postbus", "1000 AA", "Amsterdam", timeZone: "Europe/Brussels"), Address(_warehouse, "Rue Neuve", "1000", "Brussel", _be)];
            dto.ContactPersons = [Contact("Jan", "Jansen", "jan@acme.nl"), Contact("Piet", "de Vries", role: ContactPersonRole.Invoicing)];
            dto.BankAccounts = [Bank("nl91 abna 0417 1643 00"), Bank("DE89370400440532013000", isDefault: true)];
            dto.CategoryIds = [_category, _category];
            dto.GeographicalRegionIds = [_region];
            dto.OrganizationUnitIds = [_unit];
        });

        created.Code.ShouldBe("ACM01");
        created.Name.ShouldBe("ACME Transport");
        created.Roles.ShouldBe(RelationRoles.Customer | RelationRoles.Carrier);
        created.IsArchived.ShouldBeFalse();
        created.Phone.ShouldBe("020-1234567");
        created.Mobile.ShouldBe("06-12345678");
        created.Email.ShouldBe("planning@acme.nl");
        created.Website.ShouldBe("https://acme.nl");
        created.ChamberOfCommerceNumber.ShouldBe("12345678");
        created.VatNumber.ShouldBe("NL801234567B01");
        created.PreferredCulture.ShouldBe("nl");
        created.Description.ShouldBe("key account");
        created.ConcurrencyStamp.ShouldNotBeNullOrEmpty();
        created.CreationTime.ShouldNotBe(default);

        created.Addresses.Count.ShouldBe(3);
        var visiting = created.Addresses.Single(a => a.AddressTypeId == _visiting);
        visiting.Id.ShouldNotBeNull();
        visiting.AddressTypeName.ShouldBe("Visiting address");
        visiting.CountryCode.ShouldBe("NL");
        visiting.HouseNumber.ShouldBe("12a");
        visiting.TimeZoneId.ShouldBe("Europe/Amsterdam", "the country's primary zone is the default");
        created.Addresses.Single(a => a.AddressTypeId == _postal).TimeZoneId.ShouldBe("Europe/Brussels", "an explicit zone wins");
        created.Addresses.Single(a => a.CountryId == _be).CountryCode.ShouldBe("BE");

        created.ContactPersons.Select(c => c.LastName).ShouldBe(["de Vries", "Jansen"], "ordered by last name");
        created.ContactPersons.ShouldAllBe(c => c.Id != null);
        created.ContactPersons.Single(c => c.FirstName == "Jan").Email.ShouldBe("jan@acme.nl");
        created.BankAccounts.Select(b => b.Iban).ShouldBe(["DE89370400440532013000", "NL91ABNA0417164300"], "the default first");
        created.BankAccounts.Single(b => b.IsDefault).Iban.ShouldBe("DE89370400440532013000");
        created.CategoryIds.ShouldBe([_category]);
        created.GeographicalRegionIds.ShouldBe([_region]);
        created.OrganizationUnitIds.ShouldBe([_unit]);

        var fetched = await _service.GetAsync(created.Id);
        fetched.Addresses.Count.ShouldBe(3);
        fetched.ContactPersons.Count.ShouldBe(2);
        fetched.BankAccounts.Count.ShouldBe(2);
        fetched.CategoryIds.ShouldBe([_category]);

        var row = (await _service.GetListAsync(Request())).Rows.ShouldHaveSingleItem();
        row.Code.ShouldBe("ACM01");
        row.Roles.ShouldBe(RelationRoles.Customer | RelationRoles.Carrier);
        row.VatNumber.ShouldBe("NL801234567B01");
        row.ChamberOfCommerceNumber.ShouldBe("12345678");
        row.City.ShouldBe("Amsterdam", "the visiting address by kind");
        row.CountryCode.ShouldBe("NL");
        row.CategoryNames.ShouldBe(["Key accounts"]);

        var lookup = (await _service.GetLookupAsync()).ShouldHaveSingleItem();
        lookup.Code.ShouldBe("ACM01");
        lookup.Name.ShouldBe("ACME Transport");

        var addresses = await _service.GetAddressesAsync(created.Id);
        addresses.Count.ShouldBe(3);
        addresses.Single(a => a.AddressTypeId == _visiting).City.ShouldBe("Amsterdam");
        addresses.Single(a => a.AddressTypeId == _warehouse).City.ShouldBe("Brussel");
        addresses.Select(a => a.AddressTypeName).ShouldBe(["Postal address", "Visiting address", "Warehouse"], "ordered by type name");
    }

    [Fact]
    public async Task A_relation_without_a_visiting_address_shows_its_first_address_city_in_the_row()
    {
        using var _ = InTenantA();
        await SeedReferencesAsync();

        await CreateAsync("ONLYPOST", "Postal only", configure: dto => dto.Addresses = [Address(_postal, city: "Zwolle")]);
        await CreateAsync("NOADDR", "No address");

        var rows = (await _service.GetListAsync(Request())).Rows;
        rows.Single(r => r.Code == "ONLYPOST").City.ShouldBe("Zwolle");
        rows.Single(r => r.Code == "NOADDR").City.ShouldBeNull();
        rows.Single(r => r.Code == "NOADDR").CountryCode.ShouldBeNull();
    }

    // ── Default address ("all addresses equal", PO 2026-10-12) ───────────────

    [Fact]
    public async Task Create_and_update_round_trip_a_default_address_which_comes_first_without_a_type_name()
    {
        using var _ = InTenantA();
        using var culture = UiCulture("en");
        await SeedReferencesAsync();

        var created = await CreateAsync("DEF01", "Default address", configure: dto =>
            dto.Addresses = [Address(_postal, "Postbus", "1000 AA", "Zwolle"), Address(null, "Dorpsstraat", "3511 AB", "Utrecht")]);

        created.Addresses.Count.ShouldBe(2);
        var @default = created.Addresses[0];
        @default.Id.ShouldNotBeNull();
        @default.AddressTypeId.ShouldBeNull("the default address has no type");
        @default.AddressTypeName.ShouldBeNull();
        @default.Street.ShouldBe("Dorpsstraat");
        @default.City.ShouldBe("Utrecht");
        @default.CountryCode.ShouldBe("NL");
        @default.TimeZoneId.ShouldBe("Europe/Amsterdam", "the country's primary zone is the default for the default address too");
        created.Addresses[1].AddressTypeId.ShouldBe(_postal);
        created.Addresses[1].AddressTypeName.ShouldBe("Postal address");

        var fetched = await _service.GetAsync(created.Id);
        fetched.Addresses.Select(a => a.AddressTypeName).ShouldBe([null, "Postal address"], "the default first, then by type name");
        var listed = await _service.GetAddressesAsync(created.Id);
        listed.Select(a => a.AddressTypeName).ShouldBe([null, "Postal address"]);
        listed[0].Id.ShouldBe(@default.Id);

        /* Update by id: the default row keeps its identity; a typed row is added and sorts after it. */
        var defaultId = @default.Id!.Value;
        var update = UpdateFrom(created);
        update.Addresses = [Address(_visiting, "Keizersgracht", city: "Amsterdam"), Address(null, "Dorpsstraat 2", "3511 AB", "Utrecht", id: defaultId), Address(_postal, "Postbus", "1000 AA", "Zwolle", id: created.Addresses[1].Id)];
        var updated = await _service.UpdateAsync(created.Id, update);
        updated.Addresses.Count.ShouldBe(3);
        updated.Addresses[0].Id.ShouldBe(defaultId);
        updated.Addresses[0].AddressTypeId.ShouldBeNull();
        updated.Addresses[0].AddressTypeName.ShouldBeNull();
        updated.Addresses[0].Street.ShouldBe("Dorpsstraat 2");
        updated.Addresses.Skip(1).Select(a => a.AddressTypeName).ShouldBe(["Postal address", "Visiting address"]);

        /* Update without an id: the null type matches the existing default row in place. */
        var again = UpdateFrom(updated);
        again.Addresses = [Address(null, "Dorpsstraat 3", "3511 AB", "Utrecht"), Address(_visiting, "Keizersgracht", city: "Amsterdam", id: updated.Addresses.Single(a => a.AddressTypeId == _visiting).Id)];
        var replaced = await _service.UpdateAsync(created.Id, again);
        replaced.Addresses.Count.ShouldBe(2);
        replaced.Addresses[0].Id.ShouldBe(defaultId);
        replaced.Addresses[0].Street.ShouldBe("Dorpsstraat 3");
        replaced.Addresses[1].AddressTypeId.ShouldBe(_visiting);
        (await WithUnitOfWorkAsync(() => _addressRows.CountAsync(a => a.RelationId == created.Id))).ShouldBe(2, "the dropped postal row is gone, nothing duplicated");
        (await WithUnitOfWorkAsync(() => _addressRows.CountAsync(a => a.RelationId == created.Id && a.AddressTypeId == null))).ShouldBe(1);

        /* A second default on update is refused and leaves the stored set alone. */
        var twoDefaults = UpdateFrom(replaced);
        twoDefaults.Addresses = [Address(null, "One", id: defaultId), Address(null, "Two")];
        await ShouldFailAsync(() => _service.UpdateAsync(created.Id, twoDefaults), MasterDataErrorCodes.DuplicateKey, "addressTypeId");
        (await _service.GetAsync(created.Id)).Addresses.Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_row_and_lookup_city_come_from_the_visiting_address_else_the_default_else_the_first_by_city()
    {
        using var _ = InTenantA();
        await SeedReferencesAsync();
        await CreateAsync("VIS", "Visiting wins", configure: dto => dto.Addresses = [Address(null, city: "Utrecht"), Address(_postal, city: "Aalst"), Address(_visiting, city: "Amsterdam")]);
        await CreateAsync("DEF", "Default next", configure: dto => dto.Addresses = [Address(_postal, city: "Aalst"), Address(_warehouse, city: "Breda"), Address(null, city: "Utrecht", countryId: _be)]);
        await CreateAsync("FIRST", "First by city", configure: dto => dto.Addresses = [Address(_warehouse, city: "Zwolle"), Address(_postal, city: "Alkmaar")]);
        await CreateAsync("NONE", "No address");

        var rows = (await _service.GetListAsync(Request())).Rows;
        rows.Single(r => r.Code == "VIS").City.ShouldBe("Amsterdam", "the visiting address by kind, before the default");
        rows.Single(r => r.Code == "VIS").CountryCode.ShouldBe("NL");
        rows.Single(r => r.Code == "DEF").City.ShouldBe("Utrecht", "the default address, before any other typed one");
        rows.Single(r => r.Code == "DEF").CountryCode.ShouldBe("BE");
        rows.Single(r => r.Code == "FIRST").City.ShouldBe("Alkmaar", "the first address by city");
        rows.Single(r => r.Code == "NONE").City.ShouldBeNull();

        var lookup = (await _service.GetLookupPageAsync()).Items;
        lookup.Count.ShouldBe(4);
        lookup.Single(l => l.Code == "VIS").City.ShouldBe("Amsterdam");
        lookup.Single(l => l.Code == "DEF").City.ShouldBe("Utrecht");
        lookup.Single(l => l.Code == "DEF").CountryCode.ShouldBe("BE");
        lookup.Single(l => l.Code == "FIRST").City.ShouldBe("Alkmaar");
        lookup.Single(l => l.Code == "NONE").City.ShouldBeNull();

        /* The city search sees the default address like any other. */
        (await _service.GetListAsync(Request(search: "utrecht"))).Rows.Select(r => r.Code).ShouldBe(["DEF", "VIS"]);
    }

    [Fact]
    public async Task The_city_sort_prefers_the_visiting_address_over_the_default_one()
    {
        using var _ = InTenantA();
        await SeedReferencesAsync();
        await CreateAsync("BOTH", "Both", configure: dto => dto.Addresses = [Address(null, city: "Amsterdam"), Address(_visiting, city: "Zwolle")]);
        await CreateAsync("DEFONLY", "Default only", configure: dto => dto.Addresses = [Address(null, city: "Breda"), Address(_postal, city: "Aalst")]);
        await CreateAsync("VISONLY", "Visiting only", configure: dto => dto.Addresses = [Address(_visiting, city: "Groningen")]);
        await CreateAsync("TYPED", "Typed only", configure: dto => dto.Addresses = [Address(_postal, city: "Aalst")]);

        (await _service.GetListAsync(Request(sortBy: "city"))).Rows.Select(r => r.Code).ShouldBe(["TYPED", "DEFONLY", "VISONLY", "BOTH"], "no visiting or default address sorts first; BOTH sorts on Zwolle, not Amsterdam");
        (await _service.GetListAsync(Request(sortBy: "city", sortDir: "desc"))).Rows.Select(r => r.Code).ShouldBe(["BOTH", "VISONLY", "DEFONLY", "TYPED"]);
    }

    // ── Blocked flag (PO 2026-10-12) ─────────────────────────────────────────

    [Fact]
    public async Task IsBlocked_round_trips_through_create_update_row_and_detail_and_sorts_without_hiding_anything()
    {
        using var _ = InTenantA();
        var blocked = await CreateAsync("BLK", "Blocked one", configure: dto => dto.IsBlocked = true);
        var open = await CreateAsync("OPN", "Open one");

        blocked.IsBlocked.ShouldBeTrue();
        blocked.IsArchived.ShouldBeFalse("blocked is not archived");
        open.IsBlocked.ShouldBeFalse("off by default");
        (await _service.GetAsync(blocked.Id)).IsBlocked.ShouldBeTrue();
        (await _service.GetAsync(open.Id)).IsBlocked.ShouldBeFalse();

        var rows = (await _service.GetListAsync(Request())).Rows;
        rows.Count.ShouldBe(2, "nothing enforces the block yet: a blocked relation stays listed");
        rows.Single(r => r.Code == "BLK").IsBlocked.ShouldBeTrue();
        rows.Single(r => r.Code == "OPN").IsBlocked.ShouldBeFalse();
        (await _service.GetLookupAsync()).Select(l => l.Code).ShouldBe(["BLK", "OPN"], "a blocked relation is still selectable");
        (await _service.GetLookupPageAsync()).TotalCount.ShouldBe(2);

        (await _service.GetListAsync(Request(sortBy: "isBlocked", sortDir: "desc"))).Rows.Select(r => r.Code).ShouldBe(["BLK", "OPN"]);
        (await _service.GetListAsync(Request(sortBy: "isblocked"))).Rows.Select(r => r.Code).ShouldBe(["OPN", "BLK"]);

        /* Update flips it both ways; the other flags are untouched. */
        var unblock = UpdateFrom(blocked);
        unblock.IsBlocked = false;
        var unblocked = await _service.UpdateAsync(blocked.Id, unblock);
        unblocked.IsBlocked.ShouldBeFalse();
        unblocked.IsArchived.ShouldBeFalse();
        unblocked.ConcurrencyStamp.ShouldNotBe(blocked.ConcurrencyStamp);

        var block = UpdateFrom(open);
        block.IsBlocked = true;
        block.IsArchived = true;
        var nowBlocked = await _service.UpdateAsync(open.Id, block);
        nowBlocked.IsBlocked.ShouldBeTrue();
        nowBlocked.IsArchived.ShouldBeTrue("both flags are independent");
        (await _service.GetListAsync(Request(includeArchived: true))).Rows.Single(r => r.Code == "OPN").IsBlocked.ShouldBeTrue();
        (await _service.GetListAsync(Request())).Rows.ShouldHaveSingleItem().IsBlocked.ShouldBeFalse();

        /* An update that does not mention the flag (false in the DTO) clears it: the DTO is the whole truth. */
        var silent = UpdateFrom(nowBlocked);
        silent.IsBlocked = false;
        (await _service.UpdateAsync(open.Id, silent)).IsBlocked.ShouldBeFalse();
    }

    [Fact]
    public async Task Update_replaces_children_by_id_keeps_unchanged_rows_and_bumps_the_stamp()
    {
        using var _ = InTenantA();
        await SeedReferencesAsync();
        var created = await CreateAsync("ACM01", "ACME", configure: dto =>
        {
            dto.Addresses = [Address(_visiting, "Old street"), Address(_postal, "Postbus")];
            dto.ContactPersons = [Contact("Jan", "Jansen"), Contact("Piet", "Pietersen")];
            dto.BankAccounts = [Bank("NL91ABNA0417164300", isDefault: true), Bank("DE89370400440532013000")];
            dto.CategoryIds = [_category];
            dto.GeographicalRegionIds = [_region];
            dto.OrganizationUnitIds = [_unit];
        });
        var visitingId = created.Addresses.Single(a => a.AddressTypeId == _visiting).Id!.Value;
        var janId = created.ContactPersons.Single(c => c.FirstName == "Jan").Id!.Value;
        var deId = created.BankAccounts.Single(b => b.Iban.StartsWith("DE")).Id!.Value;

        var update = UpdateFrom(created);
        update.Name = "ACME Transport";
        update.Roles = RelationRoles.Customer | RelationRoles.Depot;
        update.Addresses = [Address(_visiting, "New street", id: visitingId)];
        update.ContactPersons = [Contact("Jan", "de Vries", id: janId), Contact("Kees", "Klaassen")];
        update.BankAccounts = [Bank("DE89370400440532013000", id: deId)];
        update.CategoryIds = [];
        update.GeographicalRegionIds = [_region];
        update.OrganizationUnitIds = [];
        var updated = await _service.UpdateAsync(created.Id, update);

        updated.Name.ShouldBe("ACME Transport");
        updated.Roles.ShouldBe(RelationRoles.Customer | RelationRoles.Depot);
        updated.ConcurrencyStamp.ShouldNotBe(created.ConcurrencyStamp);
        updated.Addresses.ShouldHaveSingleItem().Id.ShouldBe(visitingId);
        updated.Addresses[0].Street.ShouldBe("New street");
        updated.ContactPersons.Count.ShouldBe(2);
        updated.ContactPersons.Single(c => c.Id == janId).LastName.ShouldBe("de Vries");
        updated.ContactPersons.ShouldContain(c => c.FirstName == "Kees");
        updated.BankAccounts.ShouldHaveSingleItem().Id.ShouldBe(deId);
        updated.BankAccounts[0].IsDefault.ShouldBeTrue("the only account becomes the default");
        updated.CategoryIds.ShouldBeEmpty();
        updated.GeographicalRegionIds.ShouldBe([_region]);
        updated.OrganizationUnitIds.ShouldBeEmpty();

        /* The removed children are gone from the tables, not orphaned. */
        (await WithUnitOfWorkAsync(() => _addressRows.CountAsync(a => a.RelationId == created.Id))).ShouldBe(1);
        (await WithUnitOfWorkAsync(() => _contactRows.CountAsync(c => c.RelationId == created.Id))).ShouldBe(2);
        (await WithUnitOfWorkAsync(() => _bankRows.CountAsync(b => b.RelationId == created.Id))).ShouldBe(1);
        (await WithUnitOfWorkAsync(() => _membershipRows.CountAsync(m => m.RelationId == created.Id))).ShouldBe(0);

        await Should.ThrowAsync<AbpDbConcurrencyException>(() => _service.UpdateAsync(created.Id, UpdateFrom(created)));
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.UpdateAsync(Guid.NewGuid(), UpdateFrom(updated)));
    }

    // ── Own company ──────────────────────────────────────────────────────────

    [Fact]
    public async Task A_second_own_company_is_refused_on_create_and_on_update()
    {
        using var _ = InTenantA();
        var own = await CreateAsync("OWN", "Our company", RelationRoles.OwnCompany | RelationRoles.Carrier);
        var other = await CreateAsync("OTH", "Other", RelationRoles.Customer);

        await ShouldFailAsync(() => CreateAsync("OWN2", "Second own", RelationRoles.OwnCompany), MasterDataErrorCodes.OwnCompanyAlreadyExists);

        var promote = UpdateFrom(other);
        promote.Roles = RelationRoles.Customer | RelationRoles.OwnCompany;
        await ShouldFailAsync(() => _service.UpdateAsync(other.Id, promote), MasterDataErrorCodes.OwnCompanyAlreadyExists);

        /* Updating the own company itself keeps the role. */
        var rename = UpdateFrom(own);
        rename.Name = "Our company B.V.";
        (await _service.UpdateAsync(own.Id, rename)).Roles.ShouldBe(RelationRoles.OwnCompany | RelationRoles.Carrier);

        /* Removing the role frees the slot. */
        var demote = UpdateFrom(await _service.GetAsync(own.Id));
        demote.Roles = RelationRoles.Carrier;
        await _service.UpdateAsync(own.Id, demote);
        (await CreateAsync("OWN2", "Second own", RelationRoles.OwnCompany)).Roles.ShouldBe(RelationRoles.OwnCompany);
    }

    // ── Natural keys and shapes ──────────────────────────────────────────────

    [Fact]
    public async Task Duplicate_code_vat_and_coc_are_refused_with_DuplicateKey_and_freed_by_delete()
    {
        using var _ = InTenantA();
        var first = await CreateAsync("ACM01", "ACME", configure: dto =>
        {
            dto.VatNumber = "NL801234567B01";
            dto.ChamberOfCommerceNumber = "12345678";
        });

        var code = await ShouldFailAsync(() => CreateAsync(" acm01 ", "Other"), MasterDataErrorCodes.DuplicateKey, "code");
        code.Data["value"].ShouldBe("ACM01");
        await ShouldFailAsync(() => CreateAsync("OTH", "Other", configure: dto => dto.VatNumber = "nl 8012.34.567.b01"), MasterDataErrorCodes.DuplicateKey, "vatNumber");
        await ShouldFailAsync(() => CreateAsync("OTH", "Other", configure: dto => dto.ChamberOfCommerceNumber = "12-34-56-78"), MasterDataErrorCodes.DuplicateKey, "chamberOfCommerceNumber");

        /* The same values on the relation itself are not a conflict. */
        var same = UpdateFrom(first);
        same.Name = "ACME again";
        (await _service.UpdateAsync(first.Id, same)).VatNumber.ShouldBe("NL801234567B01");

        /* A second relation may take them after a rename, and the keys are freed by a soft delete. */
        var second = await CreateAsync("OTH", "Other");
        var clash = UpdateFrom(second);
        clash.Code = "ACM01";
        await ShouldFailAsync(() => _service.UpdateAsync(second.Id, clash), MasterDataErrorCodes.DuplicateKey, "code");

        await _service.DeleteAsync(first.Id);
        var freed = await CreateAsync("ACM01", "ACME reborn", configure: dto =>
        {
            dto.VatNumber = "NL801234567B01";
            dto.ChamberOfCommerceNumber = "12345678";
        });
        freed.Id.ShouldNotBe(first.Id);
    }

    [Fact]
    public async Task Shapes_and_roles_are_validated_through_the_service()
    {
        using var _ = InTenantA();
        await SeedReferencesAsync();

        await ShouldFailAsync(() => CreateAsync("A", "A", RelationRoles.None), MasterDataErrorCodes.RolesRequired);
        await ShouldFailAsync(() => CreateAsync("A", "A", (RelationRoles)64), MasterDataErrorCodes.RolesRequired);
        await ShouldFailAsync(() => CreateAsync("A", "A", configure: dto => dto.VatNumber = "801234567B01"), MasterDataErrorCodes.InvalidVatNumber);
        await ShouldFailAsync(() => CreateAsync("A", "A", configure: dto => dto.Email = "planner@acme"), MasterDataErrorCodes.InvalidEmail);
        await ShouldFailAsync(() => CreateAsync("A", "A", configure: dto => dto.PreferredCulture = "de"), MasterDataErrorCodes.InvalidCulture);
        await ShouldFailAsync(() => CreateAsync("A", "A", configure: dto => dto.BankAccounts = [Bank("NL91ABNA0417164301")]), MasterDataErrorCodes.InvalidIban);
        await ShouldFailAsync(() => CreateAsync("A", "A", configure: dto => dto.BankAccounts = [Bank("NL91ABNA0417164300", true), Bank("DE89370400440532013000", true)]), MasterDataErrorCodes.DuplicateKey, "isDefault");
        await ShouldFailAsync(() => CreateAsync("A", "A", configure: dto => dto.BankAccounts = [Bank("NL91ABNA0417164300"), Bank("nl91 abna 0417 1643 00")]), MasterDataErrorCodes.DuplicateKey, "iban");
        await ShouldFailAsync(() => CreateAsync("A", "A", configure: dto => dto.Addresses = [Address(_visiting, timeZone: "Mars/Olympus")]), MasterDataErrorCodes.InvalidTimeZone);
        await ShouldFailAsync(() => CreateAsync("A", "A", configure: dto => dto.Addresses = [Address(_visiting), Address(_visiting, "Other")]), MasterDataErrorCodes.DuplicateKey, "addressTypeId");
        await ShouldFailAsync(() => CreateAsync("A", "A", configure: dto => dto.Addresses = [Address(null), Address(_visiting), Address(null, "Other")]), MasterDataErrorCodes.DuplicateKey, "addressTypeId");
        await ShouldFailAsync(() => CreateAsync("A", "A", configure: dto => dto.ContactPersons = [Contact("Jan", "Jansen", "jan@")]), MasterDataErrorCodes.InvalidEmail);
        await ShouldFailAsync(() => CreateAsync("A", "A", configure: dto => dto.ContactPersons = Enumerable.Range(0, MasterDataConsts.MaxContactPersonsPerRelation + 1).Select(i => Contact("P", $"Person {i}")).ToList()), MasterDataErrorCodes.TooManyChildren);

        /* Nothing was written by the refused creates. */
        (await _service.GetListAsync(Request())).TotalCount.ShouldBe(0);
        (await WithUnitOfWorkAsync(() => _addressRows.GetCountAsync())).ShouldBe(0);
        (await WithUnitOfWorkAsync(() => _bankRows.GetCountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task Unknown_country_address_type_category_region_or_organisation_unit_is_ReferenceNotFound()
    {
        using var _ = InTenantA();
        await SeedReferencesAsync();

        await ShouldFailAsync(() => CreateAsync("A", "A", configure: dto => dto.Addresses = [Address(_visiting, countryId: Guid.NewGuid())]), MasterDataErrorCodes.ReferenceNotFound, "addresses.countryId");
        await ShouldFailAsync(() => CreateAsync("A", "A", configure: dto => dto.Addresses = [Address(Guid.NewGuid())]), MasterDataErrorCodes.ReferenceNotFound, "addresses.addressTypeId");
        /* The default address has no type to check; an unknown typed one next to it is still refused. */
        await ShouldFailAsync(() => CreateAsync("A", "A", configure: dto => dto.Addresses = [Address(null), Address(Guid.NewGuid())]), MasterDataErrorCodes.ReferenceNotFound, "addresses.addressTypeId");
        await ShouldFailAsync(() => CreateAsync("A", "A", configure: dto => dto.Addresses = [Address(null, countryId: Guid.NewGuid())]), MasterDataErrorCodes.ReferenceNotFound, "addresses.countryId");
        await ShouldFailAsync(() => CreateAsync("A", "A", configure: dto => dto.CategoryIds = [_category, Guid.NewGuid()]), MasterDataErrorCodes.ReferenceNotFound, "categoryIds");
        await ShouldFailAsync(() => CreateAsync("A", "A", configure: dto => dto.GeographicalRegionIds = [Guid.NewGuid()]), MasterDataErrorCodes.ReferenceNotFound, "geographicalRegionIds");
        await ShouldFailAsync(() => CreateAsync("A", "A", configure: dto => dto.OrganizationUnitIds = [Guid.NewGuid()]), MasterDataErrorCodes.ReferenceNotFound, "organizationUnitIds");

        (await _service.GetListAsync(Request())).TotalCount.ShouldBe(0);
    }

    // ── Archive, list, lookup ────────────────────────────────────────────────

    [Fact]
    public async Task Archive_hides_the_relation_from_the_list_lookup_and_lookup_page_until_unarchived()
    {
        using var _ = InTenantA();
        var acme = await CreateAsync("ACM01", "ACME");
        await CreateAsync("BET01", "Beta");

        await _service.ArchiveAsync(acme.Id);

        (await _service.GetAsync(acme.Id)).IsArchived.ShouldBeTrue();
        (await _service.GetListAsync(Request())).Rows.Select(r => r.Code).ShouldBe(["BET01"]);
        (await _service.GetListAsync(Request())).TotalCount.ShouldBe(1);
        var all = await _service.GetListAsync(Request(includeArchived: true, sortBy: "isArchived", sortDir: "desc"));
        all.Rows.Select(r => r.Code).ShouldBe(["ACM01", "BET01"]);
        all.Rows.Single(r => r.Code == "ACM01").IsArchived.ShouldBeTrue();
        (await _service.GetLookupAsync()).Select(l => l.Code).ShouldBe(["BET01"]);
        (await _service.GetLookupPageAsync()).Items.Select(l => l.Code).ShouldBe(["BET01"]);
        (await _service.GetLookupPageAsync("acme")).TotalCount.ShouldBe(0);

        await _service.UnarchiveAsync(acme.Id);

        (await _service.GetAsync(acme.Id)).IsArchived.ShouldBeFalse();
        (await _service.GetListAsync(Request())).TotalCount.ShouldBe(2);
        (await _service.GetLookupAsync()).Count.ShouldBe(2);

        /* The flag also travels through create and update. */
        var archivedAtBirth = await CreateAsync("ARC", "Born archived", configure: dto => dto.IsArchived = true);
        archivedAtBirth.IsArchived.ShouldBeTrue();
        (await _service.GetListAsync(Request())).TotalCount.ShouldBe(2);
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.ArchiveAsync(Guid.NewGuid()));
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.UnarchiveAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Lookup_page_searches_the_folded_key_filters_on_roles_pages_and_shows_the_visiting_city()
    {
        using var _ = InTenantA();
        await SeedReferencesAsync();
        await CreateAsync("MUL01", "Müller Logistik", RelationRoles.Carrier, dto => dto.Addresses = [Address(_visiting, city: "Köln", countryId: _be)]);
        await CreateAsync("ACM01", "ACME Transport", RelationRoles.Customer | RelationRoles.Carrier, dto => dto.Addresses = [Address(_visiting, city: "Amsterdam"), Address(_postal, city: "Postbus-stad")]);
        await CreateAsync("ACM02", "ACME Depot", RelationRoles.Depot);
        await CreateAsync("ARC", "ACME Archived", RelationRoles.Customer, dto => dto.IsArchived = true);
        for (var i = 0; i < 30; i++)
        {
            await CreateAsync($"FIL{i:D2}", $"Filler {i:D2}", RelationRoles.Creditor);
        }

        var byName = await _service.GetLookupPageAsync("muller");
        byName.TotalCount.ShouldBe(1);
        var muller = byName.Items.ShouldHaveSingleItem();
        muller.Code.ShouldBe("MUL01");
        muller.Name.ShouldBe("Müller Logistik");
        muller.Roles.ShouldBe(RelationRoles.Carrier);
        muller.City.ShouldBe("Köln");
        muller.CountryCode.ShouldBe("BE");

        (await _service.GetLookupPageAsync("MÜLLER")).TotalCount.ShouldBe(1);
        (await _service.GetLookupPageAsync("acm0")).Items.Select(l => l.Code).ShouldBe(["ACM02", "ACM01"], "by code fragment, ordered by name");
        (await _service.GetLookupPageAsync("acme")).Items.Select(l => l.Name).ShouldBe(["ACME Depot", "ACME Transport"], "the archived one is out");
        (await _service.GetLookupPageAsync("acme").ContinueWith(t => t.Result.Items.Single(i => i.Code == "ACM01").City)).ShouldBe("Amsterdam");
        (await _service.GetLookupPageAsync("nothing here")).Items.ShouldBeEmpty();

        var carriers = await _service.GetLookupPageAsync(roles: RelationRoles.Carrier);
        carriers.Items.Select(l => l.Code).ShouldBe(["ACM01", "MUL01"]);
        (await _service.GetLookupPageAsync(roles: RelationRoles.Carrier | RelationRoles.Depot)).TotalCount.ShouldBe(3, "any of the roles");
        (await _service.GetLookupPageAsync("acme", roles: RelationRoles.Depot)).Items.ShouldHaveSingleItem().Code.ShouldBe("ACM02");
        (await _service.GetLookupPageAsync(roles: RelationRoles.None)).TotalCount.ShouldBe(33, "None means no filter");

        var page1 = await _service.GetLookupPageAsync();
        page1.TotalCount.ShouldBe(33);
        page1.Items.Count.ShouldBe(MasterDataConsts.LookupPageSize);
        page1.Items[0].Name.ShouldBe("ACME Depot");
        var page2 = await _service.GetLookupPageAsync(skip: MasterDataConsts.LookupPageSize);
        page2.Items.Count.ShouldBe(8);
        page1.Items.Select(i => i.Id).Intersect(page2.Items.Select(i => i.Id)).ShouldBeEmpty();
        (await _service.GetLookupPageAsync(take: 1000)).Items.Count.ShouldBe(MasterDataConsts.LookupPageSize, "take is clamped to the lookup page size (design 43, security quickscan)");
        (await _service.GetLookupPageAsync(skip: -5, take: 0)).Items.Count.ShouldBe(1, "skip floors at 0, take at 1");
    }

    [Fact]
    public async Task List_searches_name_code_vat_coc_and_city_filters_on_roles_category_and_unit_and_sorts_on_the_whitelist()
    {
        using var _ = InTenantA();
        await SeedReferencesAsync();
        var acme = await CreateAsync("ACM01", "ACME Transport", RelationRoles.Customer | RelationRoles.Carrier, dto =>
        {
            dto.VatNumber = "NL801234567B01";
            dto.ChamberOfCommerceNumber = "12345678";
            dto.Email = "a@acme.nl";
            dto.Addresses = [Address(_visiting, city: "Zwolle")];
            dto.CategoryIds = [_category];
            dto.OrganizationUnitIds = [_unit];
        });
        await CreateAsync("BET01", "Beta Logistics", RelationRoles.Carrier, dto =>
        {
            dto.VatNumber = "BE0123456789";
            dto.Addresses = [Address(_visiting, city: "Antwerpen", countryId: _be)];
        });
        await CreateAsync("GAM01", "Gamma Depot", RelationRoles.Depot, dto => dto.Addresses = [Address(_postal, city: "Amsterdam")]);

        var list = await _service.GetListAsync(Request());
        list.TotalCount.ShouldBe(3);
        list.FilteredCount.ShouldBe(3);
        list.Rows.Select(r => r.Name).ShouldBe(["ACME Transport", "Beta Logistics", "Gamma Depot"]);

        (await _service.GetListAsync(Request(search: "acme"))).Rows.ShouldHaveSingleItem().Id.ShouldBe(acme.Id);
        (await _service.GetListAsync(Request(search: "bet0"))).Rows.ShouldHaveSingleItem().Code.ShouldBe("BET01");
        (await _service.GetListAsync(Request(search: "nl8012"))).Rows.ShouldHaveSingleItem().Code.ShouldBe("ACM01");
        (await _service.GetListAsync(Request(search: "1234567"))).Rows.Select(r => r.Code).ShouldBe(["ACM01", "BET01"], "VAT and CoC fragments");
        (await _service.GetListAsync(Request(search: "antwerp"))).Rows.ShouldHaveSingleItem().Code.ShouldBe("BET01");
        var searched = await _service.GetListAsync(Request(search: "zwol"));
        searched.FilteredCount.ShouldBe(1);
        searched.TotalCount.ShouldBe(3);

        (await _service.GetListAsync(Request(roles: RelationRoles.Carrier))).Rows.Select(r => r.Code).ShouldBe(["ACM01", "BET01"]);
        (await _service.GetListAsync(Request(roles: RelationRoles.Depot | RelationRoles.Customer))).Rows.Select(r => r.Code).ShouldBe(["ACM01", "GAM01"]);
        (await _service.GetListAsync(Request(categoryId: _category))).Rows.ShouldHaveSingleItem().Code.ShouldBe("ACM01");
        (await _service.GetListAsync(Request(categoryId: Guid.NewGuid()))).TotalCount.ShouldBe(0);
        (await _service.GetListAsync(Request(unitId: _unit))).Rows.ShouldHaveSingleItem().Code.ShouldBe("ACM01");

        (await _service.GetListAsync(Request(sortBy: "code", sortDir: "desc"))).Rows.Select(r => r.Code).ShouldBe(["GAM01", "BET01", "ACM01"]);
        (await _service.GetListAsync(Request(sortBy: "vatNumber"))).Rows.Select(r => r.Code).ShouldBe(["GAM01", "BET01", "ACM01"]);
        (await _service.GetListAsync(Request(sortBy: "city"))).Rows.Select(r => r.Code).ShouldBe(["GAM01", "BET01", "ACM01"], "the visiting city, missing first");
        (await _service.GetListAsync(Request(sortBy: "email", sortDir: "desc"))).Rows.First().Code.ShouldBe("ACM01");
        (await ShouldFailAsync(() => _service.GetListAsync(Request(sortBy: "roles")), MasterDataErrorCodes.UnknownSortColumn)).Data["sortBy"].ShouldBe("roles");

        var page = await _service.GetListAsync(Request(start: 1, end: 2));
        page.Rows.ShouldHaveSingleItem().Code.ShouldBe("BET01");
        page.TotalCount.ShouldBe(3);
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_is_refused_while_a_driver_has_the_relation_as_employer_and_clears_children_otherwise()
    {
        using var _ = InTenantA();
        await SeedReferencesAsync();
        var carrier = await CreateAsync("CAR", "Carrier", RelationRoles.Carrier, dto =>
        {
            dto.Addresses = [Address(_visiting)];
            dto.ContactPersons = [Contact("Jan", "Jansen")];
            dto.BankAccounts = [Bank("NL91ABNA0417164300")];
            dto.CategoryIds = [_category];
        });
        var driver = await _drivers.CreateAsync(new DriverCreateDto { Code = "D1", FirstName = "Jan", LastName = "Jansen", EmployerRelationId = carrier.Id });

        var ex = await ShouldFailAsync(() => _service.DeleteAsync(carrier.Id), MasterDataErrorCodes.InUse);
        ex.Data["reason"].ShouldNotBeNull();
        (await _service.GetAsync(carrier.Id)).Addresses.ShouldHaveSingleItem();

        await _drivers.DeleteAsync(driver.Id);
        await _service.DeleteAsync(carrier.Id);

        await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(carrier.Id));
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAddressesAsync(carrier.Id));
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.DeleteAsync(carrier.Id));
        (await WithUnitOfWorkAsync(() => _addressRows.CountAsync(a => a.RelationId == carrier.Id))).ShouldBe(0);
        (await WithUnitOfWorkAsync(() => _contactRows.CountAsync(c => c.RelationId == carrier.Id))).ShouldBe(0);
        (await WithUnitOfWorkAsync(() => _bankRows.CountAsync(b => b.RelationId == carrier.Id))).ShouldBe(0);
        (await WithUnitOfWorkAsync(() => _membershipRows.CountAsync(m => m.RelationId == carrier.Id))).ShouldBe(0);
    }

    [Fact]
    public async Task Bulk_delete_explicit_and_filter_based_skips_employers_and_deletes_nothing_the_second_time()
    {
        using var _ = InTenantA();
        var a = await CreateAsync("A", "Alpha group", RelationRoles.Carrier);
        var b = await CreateAsync("B", "Beta group", RelationRoles.Customer);
        var c = await CreateAsync("C", "Gamma group", RelationRoles.Customer, dto => dto.IsArchived = true);
        var d = await CreateAsync("D", "Delta", RelationRoles.Customer);
        await _drivers.CreateAsync(new DriverCreateDto { Code = "D1", FirstName = "Jan", LastName = "Jansen", EmployerRelationId = a.Id });

        var explicitRequest = new RelationBulkDeleteRequestDto { Selection = Explicit(a.Id, b.Id, Guid.NewGuid()) };
        var first = await _service.BulkDeleteAsync(explicitRequest);
        first.DeletedCount.ShouldBe(1);
        var skipped = first.SkippedRows.ShouldHaveSingleItem();
        skipped.Id.ShouldBe(a.Id);
        skipped.Name.ShouldBe("Alpha group");
        skipped.Reason.ShouldNotBeNullOrEmpty();

        var second = await _service.BulkDeleteAsync(explicitRequest);
        second.DeletedCount.ShouldBe(0);
        second.SkippedRows.ShouldHaveSingleItem().Id.ShouldBe(a.Id);

        /* Filter-based: the search, the role filter and the archived flag narrow the set; exclusions hold. */
        (await _service.BulkDeleteAsync(new RelationBulkDeleteRequestDto { Selection = FilterBased("group"), Roles = RelationRoles.Customer })).DeletedCount.ShouldBe(0, "Gamma is archived and excluded by default");
        var filtered = await _service.BulkDeleteAsync(new RelationBulkDeleteRequestDto { Selection = FilterBased("group", a.Id), IncludeArchived = true });
        filtered.DeletedCount.ShouldBe(1);
        filtered.SkippedRows.ShouldBeEmpty("the excluded employer was never a target");
        (await _service.GetListAsync(Request(includeArchived: true))).Rows.Select(r => r.Code).ShouldBe(["A", "D"]);
        (await _service.BulkDeleteAsync(new RelationBulkDeleteRequestDto { Selection = FilterBased(), IncludeArchived = true })).DeletedCount.ShouldBe(1);
        (await _service.GetListAsync(Request(includeArchived: true))).Rows.ShouldHaveSingleItem().Id.ShouldBe(a.Id);
        c.Id.ShouldNotBe(d.Id);
    }

    // ── Tenant isolation (Critical) ───────────────────────────────────────────

    [Fact]
    public async Task Rows_of_tenant_A_are_invisible_and_untouchable_from_tenant_B_which_may_have_its_own_own_company()
    {
        RelationDto own;
        Guid countryA, typeA, categoryA, regionA;
        using (InTenantA())
        {
            await SeedReferencesAsync();
            (countryA, typeA, categoryA, regionA) = (_nl, _visiting, _category, _region);
            own = await CreateAsync("OWN", "Our company", RelationRoles.OwnCompany | RelationRoles.Carrier, dto =>
            {
                dto.VatNumber = "NL801234567B01";
                dto.Addresses = [Address(_visiting)];
                dto.ContactPersons = [Contact("Jan", "Jansen", "jan@acme.nl")];
            });
        }

        using (InTenantB())
        {
            /* A fresh service: the production instance is request-scoped and never serves two tenants. */
            var service = GetRequiredService<IRelationAppService>();

            (await service.GetListAsync(Request(includeArchived: true))).TotalCount.ShouldBe(0);
            (await service.GetListAsync(Request(search: "our"))).Rows.ShouldBeEmpty();
            (await service.GetListAsync(Request(search: "NL8012"))).Rows.ShouldBeEmpty();
            (await service.GetLookupAsync()).ShouldBeEmpty();
            (await service.GetLookupPageAsync("our")).TotalCount.ShouldBe(0);
            (await service.GetLookupPageAsync(roles: RelationRoles.OwnCompany)).Items.ShouldBeEmpty();
            await Should.ThrowAsync<EntityNotFoundException>(() => service.GetAsync(own.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => service.GetAddressesAsync(own.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => service.UpdateAsync(own.Id, UpdateFrom(own)));
            await Should.ThrowAsync<EntityNotFoundException>(() => service.ArchiveAsync(own.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => service.UnarchiveAsync(own.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => service.DeleteAsync(own.Id));
            (await service.BulkDeleteAsync(new RelationBulkDeleteRequestDto { Selection = Explicit(own.Id) })).DeletedCount.ShouldBe(0);
            (await service.BulkDeleteAsync(new RelationBulkDeleteRequestDto { Selection = FilterBased(), IncludeArchived = true })).DeletedCount.ShouldBe(0);
            (await service.MatchAsync(new RelationMatchRequestDto { Code = "OWN" })).Matches.ShouldBeEmpty();
            (await service.MatchAsync(new RelationMatchRequestDto { VatNumber = "NL801234567B01" })).Matches.ShouldBeEmpty();
            (await service.MatchAsync(new RelationMatchRequestDto { Name = "Our company" })).Matches.ShouldBeEmpty();

            /* Tenant A's reference ids are not tenant B's. */
            await ShouldFailAsync(() => service.CreateAsync(Make("X", "X", RelationRoles.Customer, dto => dto.Addresses = [Address(typeA, countryId: countryA)])), MasterDataErrorCodes.ReferenceNotFound);
            await ShouldFailAsync(() => service.CreateAsync(Make("X", "X", RelationRoles.Customer, dto => dto.CategoryIds = [categoryA])), MasterDataErrorCodes.ReferenceNotFound, "categoryIds");
            await ShouldFailAsync(() => service.CreateAsync(Make("X", "X", RelationRoles.Customer, dto => dto.GeographicalRegionIds = [regionA])), MasterDataErrorCodes.ReferenceNotFound, "geographicalRegionIds");

            /* Tenant B has its own own-company slot and its own key space. */
            var ownB = await service.CreateAsync(Make("OWN", "Our company", RelationRoles.OwnCompany, dto => dto.VatNumber = "NL801234567B01"));
            ownB.Id.ShouldNotBe(own.Id);
            (await service.GetListAsync(Request())).Rows.ShouldHaveSingleItem().Id.ShouldBe(ownB.Id);
            (await service.GetLookupPageAsync(roles: RelationRoles.OwnCompany)).Items.ShouldHaveSingleItem().Id.ShouldBe(ownB.Id);
            (await service.MatchAsync(new RelationMatchRequestDto { Code = "OWN" })).Matches.ShouldHaveSingleItem().RelationId.ShouldBe(ownB.Id);
        }

        using (InTenantA())
        {
            (await _service.GetListAsync(Request())).Rows.ShouldHaveSingleItem().Id.ShouldBe(own.Id);
            (await _service.GetAsync(own.Id)).ContactPersons.ShouldHaveSingleItem().Email.ShouldBe("jan@acme.nl");
        }
    }

    // ── Feature flag (Critical) ───────────────────────────────────────────────

    [Fact]
    public async Task With_the_flag_off_every_relation_service_method_is_refused_before_touching_data()
    {
        using var _ = InTenantA();
        var acme = await CreateAsync("ACM01", "ACME");

        using (TestFeatureValueProvider.Override("false"))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetListAsync(Request()));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetAsync(acme.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetLookupAsync());
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetLookupPageAsync());
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetAddressesAsync(acme.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.CreateAsync(Dto("X", "X")));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.UpdateAsync(acme.Id, UpdateFrom(acme)));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.DeleteAsync(acme.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.BulkDeleteAsync(new RelationBulkDeleteRequestDto { Selection = Explicit(acme.Id) }));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.ArchiveAsync(acme.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.UnarchiveAsync(acme.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.MatchAsync(new RelationMatchRequestDto { Code = "ACM01" }));
        }

        (await _service.GetAsync(acme.Id)).IsArchived.ShouldBeFalse();
        (await _service.GetListAsync(Request())).TotalCount.ShouldBe(1);
    }
}
