using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Relations;
using Shouldly;
using Xunit;

namespace OpenTms.EntityFrameworkCore.MasterData;

/// <summary>
/// The relation matching service on SQLite (design 43, "The relation matching service", assessment
/// §3.15): the order of the rules, the normalisation of every key, the role filter, archived relations
/// never matching and the candidate cap. Risk class: High (the matcher runs once per imported order;
/// a wrong match books an order on the wrong customer). The PostgreSQL volume test times the same
/// paths on the trigram index; the tenant-isolation tests prove it never crosses a tenant.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class RelationMatcherTests : MasterDataAppServiceTestBase
{
    private readonly IRelationMatcher _matcher;
    private readonly IRelationAppService _relations;
    private readonly ICountryAppService _countries;
    private readonly IAddressTypeAppService _addressTypes;

    private Guid _nl;
    private Guid _be;
    private Guid _visiting;
    private Guid _postal;
    private RelationDto _acme = default!;
    private RelationDto _acmeNoord = default!;
    private RelationDto _acmeShort = default!;
    private RelationDto _muller = default!;

    public RelationMatcherTests()
    {
        _matcher = GetRequiredService<IRelationMatcher>();
        _relations = GetRequiredService<IRelationAppService>();
        _countries = GetRequiredService<ICountryAppService>();
        _addressTypes = GetRequiredService<IAddressTypeAppService>();
    }

    private RelationAddressDto Address(Guid typeId, string postalCode, string city, Guid? countryId = null)
        => new() { AddressTypeId = typeId, CountryId = countryId ?? _nl, Street = "Main", PostalCode = postalCode, City = city };

    private Task<RelationDto> CreateAsync(string code, string name, RelationRoles roles = RelationRoles.Customer, Action<RelationCreateDto>? configure = null)
    {
        var dto = new RelationCreateDto { Code = code, Name = name, Roles = roles };
        configure?.Invoke(dto);
        return _relations.CreateAsync(dto);
    }

    /// <summary>The fixture of the table-driven tests: four live relations, one archived, two postcode twins.</summary>
    private async Task SeedAsync()
    {
        _nl = (await _countries.CreateAsync(new CountryCreateDto { IsoAlpha2 = "NL", IsoAlpha3 = "NLD", Names = Names("Netherlands") })).Id;
        _be = (await _countries.CreateAsync(new CountryCreateDto { IsoAlpha2 = "BE", IsoAlpha3 = "BEL", Names = Names("Belgium") })).Id;
        _visiting = (await _addressTypes.CreateAsync(new AddressTypeCreateDto { Kind = AddressTypeKind.Visiting, Names = Names("Visiting") })).Id;
        _postal = (await _addressTypes.CreateAsync(new AddressTypeCreateDto { Kind = AddressTypeKind.Postal, Names = Names("Postal") })).Id;

        _acme = await CreateAsync("ACM01", "ACME Transport", RelationRoles.Customer | RelationRoles.Carrier, dto =>
        {
            dto.VatNumber = "NL801234567B01";
            dto.ChamberOfCommerceNumber = "12345678";
            dto.Addresses = [Address(_visiting, "1234 AB", "Amsterdam"), Address(_postal, "1000 AA", "Amsterdam")];
        });
        _acmeNoord = await CreateAsync("ACM02", "ACME Transport Noord", RelationRoles.Customer, dto =>
        {
            dto.VatNumber = "NL123456789B01";
            dto.Addresses = [Address(_visiting, "9700 AB", "Groningen")];
        });
        _acmeShort = await CreateAsync("ACM03", "Acme", RelationRoles.Depot);
        _muller = await CreateAsync("MUL01", "Müller Logistik", RelationRoles.Carrier, dto =>
        {
            dto.VatNumber = "DE123456789";
            dto.ChamberOfCommerceNumber = "HRB 98765";
            dto.Addresses = [Address(_visiting, "1234 AB", "Brussel", _be)];
        });
        await CreateAsync("ACM99", "ACME Archived", RelationRoles.Customer, dto =>
        {
            dto.IsArchived = true;
            dto.VatNumber = "NL999999999B01";
            dto.ChamberOfCommerceNumber = "99999999";
            dto.Addresses = [Address(_visiting, "1234 AB", "Amsterdam")];
        });
    }

    private static RelationMatchDto Single(RelationMatchResultDto result) => result.Matches.ShouldHaveSingleItem();

    /// <summary>The matcher is a published interface, not an application service: its caller owns the unit of work.</summary>
    private Task<RelationMatchResultDto> MatchAsync(RelationMatchRequestDto request) => WithUnitOfWorkAsync(() => _matcher.MatchAsync(request));

    // ── Exact keys ───────────────────────────────────────────────────────────

    [Fact]
    public async Task An_exact_code_wins_regardless_of_case_spaces_and_the_other_keys()
    {
        using var _ = InTenantA();
        await SeedAsync();

        var hit = Single(await MatchAsync(new RelationMatchRequestDto { Code = " acm01 " }));
        hit.RelationId.ShouldBe(_acme.Id);
        hit.Code.ShouldBe("ACM01");
        hit.Name.ShouldBe("ACME Transport");
        hit.Kind.ShouldBe(RelationMatchKind.Exact);
        hit.Score.ShouldBe(100);

        /* Code first: the VAT number and the name point elsewhere and are not consulted. */
        Single(await MatchAsync(new RelationMatchRequestDto { Code = "ACM01", VatNumber = "DE123456789", Name = "Müller" })).RelationId.ShouldBe(_acme.Id);

        /* An unknown code falls through to the next rule. */
        Single(await MatchAsync(new RelationMatchRequestDto { Code = "NOPE", VatNumber = "DE123456789" })).RelationId.ShouldBe(_muller.Id);
    }

    [Fact]
    public async Task An_exact_vat_number_matches_after_normalisation_and_before_the_chamber_of_commerce_number()
    {
        using var _ = InTenantA();
        await SeedAsync();

        var hit = Single(await MatchAsync(new RelationMatchRequestDto { VatNumber = " nl 8012.34.567.b01 " }));
        hit.RelationId.ShouldBe(_acme.Id);
        hit.Kind.ShouldBe(RelationMatchKind.Exact);
        hit.Score.ShouldBe(100);

        Single(await MatchAsync(new RelationMatchRequestDto { VatNumber = "DE 123 456 789", ChamberOfCommerceNumber = "12345678" })).RelationId.ShouldBe(_muller.Id);
        Single(await MatchAsync(new RelationMatchRequestDto { VatNumber = "NL000000000B00", ChamberOfCommerceNumber = "12345678" })).RelationId.ShouldBe(_acme.Id);
    }

    [Fact]
    public async Task An_exact_chamber_of_commerce_number_matches_after_normalisation()
    {
        using var _ = InTenantA();
        await SeedAsync();

        var hit = Single(await MatchAsync(new RelationMatchRequestDto { ChamberOfCommerceNumber = " 12-34-56-78 " }));
        hit.RelationId.ShouldBe(_acme.Id);
        hit.Kind.ShouldBe(RelationMatchKind.Exact);
        Single(await MatchAsync(new RelationMatchRequestDto { ChamberOfCommerceNumber = "hrb 98765" })).RelationId.ShouldBe(_muller.Id);
        (await MatchAsync(new RelationMatchRequestDto { ChamberOfCommerceNumber = "00000000" })).Matches.ShouldBeEmpty();
    }

    // ── Name and postcode ────────────────────────────────────────────────────

    [Fact]
    public async Task Name_and_postcode_in_the_country_hit_one_relation_through_any_of_its_addresses()
    {
        using var _ = InTenantA();
        await SeedAsync();

        var hit = Single(await MatchAsync(new RelationMatchRequestDto { Name = "acme", PostalCode = " 1234 ab ", CountryId = _nl }));
        hit.RelationId.ShouldBe(_acme.Id);
        hit.Kind.ShouldBe(RelationMatchKind.NameAndPostalCode);
        hit.Score.ShouldBe(80);

        /* The postal address counts too. */
        Single(await MatchAsync(new RelationMatchRequestDto { Name = "ACME", PostalCode = "1000AA", CountryId = _nl })).RelationId.ShouldBe(_acme.Id);

        /* Without a country the Belgian twin at the same postcode is a candidate as well. */
        var noCountry = await MatchAsync(new RelationMatchRequestDto { Name = "transport", PostalCode = "1234AB" });
        noCountry.Matches.ShouldHaveSingleItem().RelationId.ShouldBe(_acme.Id, "Müller does not carry 'transport'");
        (await MatchAsync(new RelationMatchRequestDto { Name = "logistik", PostalCode = "1234 AB" })).Matches.ShouldHaveSingleItem().RelationId.ShouldBe(_muller.Id);

        /* The wrong country or an unknown postcode falls back to name only. */
        var wrongCountry = await MatchAsync(new RelationMatchRequestDto { Name = "ACME Transport", PostalCode = "1234AB", CountryId = _be });
        wrongCountry.Matches.ShouldAllBe(m => m.Kind == RelationMatchKind.NameOnly);
        wrongCountry.Matches.Select(m => m.RelationId).ShouldBe([_acme.Id, _acmeNoord.Id]);
        (await MatchAsync(new RelationMatchRequestDto { Name = "Acme", PostalCode = "0000ZZ", CountryId = _nl })).Matches.ShouldAllBe(m => m.Kind == RelationMatchKind.NameOnly);
    }

    // ── Name only ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Name_only_returns_the_containing_relations_shortest_key_first_with_a_score_between_40_and_60()
    {
        using var _ = InTenantA();
        await SeedAsync();

        var result = await MatchAsync(new RelationMatchRequestDto { Name = " acme " });

        result.Matches.Select(m => m.RelationId).ShouldBe([_acmeShort.Id, _acme.Id, _acmeNoord.Id]);
        result.Matches.ShouldAllBe(m => m.Kind == RelationMatchKind.NameOnly);
        result.Matches.ShouldAllBe(m => m.Score >= 40 && m.Score <= 60);
        result.Matches[0].Score.ShouldBeGreaterThan(result.Matches[2].Score, "the closest name scores highest");
        result.Matches.ShouldNotContain(m => m.Code == "ACM99");

        /* Diacritics and case fold on both sides; a code fragment matches through the key as well. */
        Single(await MatchAsync(new RelationMatchRequestDto { Name = "MULLER" })).RelationId.ShouldBe(_muller.Id);
        Single(await MatchAsync(new RelationMatchRequestDto { Name = "müller logistik" })).RelationId.ShouldBe(_muller.Id);
        Single(await MatchAsync(new RelationMatchRequestDto { Name = "acm02" })).RelationId.ShouldBe(_acmeNoord.Id);
        (await MatchAsync(new RelationMatchRequestDto { Name = "Jansen" })).Matches.ShouldBeEmpty();
    }

    [Fact]
    public async Task Name_only_returns_at_most_ten_candidates()
    {
        using var _ = InTenantA();
        for (var i = 1; i <= MasterDataConsts.MaxMatchCandidates + 3; i++)
        {
            await CreateAsync($"CAN{i:D2}", $"Candidate {i:D2} {new string('x', i)}");
        }

        var result = await MatchAsync(new RelationMatchRequestDto { Name = "candidate" });

        result.Matches.Count.ShouldBe(MasterDataConsts.MaxMatchCandidates);
        result.Matches.Select(m => m.Code).ShouldBe(Enumerable.Range(1, MasterDataConsts.MaxMatchCandidates).Select(i => $"CAN{i:D2}"), "the shortest keys first");
    }

    // ── Roles, archived, empty ───────────────────────────────────────────────

    [Fact]
    public async Task The_role_filter_narrows_every_rule_and_archived_relations_never_match()
    {
        using var _ = InTenantA();
        await SeedAsync();

        (await MatchAsync(new RelationMatchRequestDto { Code = "ACM01", Roles = RelationRoles.Carrier })).Matches.ShouldHaveSingleItem();
        (await MatchAsync(new RelationMatchRequestDto { Code = "ACM01", Roles = RelationRoles.Depot })).Matches.ShouldBeEmpty();
        (await MatchAsync(new RelationMatchRequestDto { VatNumber = "NL801234567B01", Roles = RelationRoles.Depot | RelationRoles.Creditor })).Matches.ShouldBeEmpty();
        (await MatchAsync(new RelationMatchRequestDto { Name = "acme", PostalCode = "1234AB", CountryId = _nl, Roles = RelationRoles.Depot })).Matches.ShouldHaveSingleItem().RelationId.ShouldBe(_acmeShort.Id, "no postcode hit among depots, so name only");
        (await MatchAsync(new RelationMatchRequestDto { Name = "acme", Roles = RelationRoles.Carrier })).Matches.Select(m => m.RelationId).ShouldBe([_acme.Id]);
        (await MatchAsync(new RelationMatchRequestDto { Name = "acme", Roles = RelationRoles.Carrier | RelationRoles.Depot })).Matches.Select(m => m.RelationId).ShouldBe([_acmeShort.Id, _acme.Id]);
        (await MatchAsync(new RelationMatchRequestDto { Name = "acme", Roles = RelationRoles.None })).Matches.Count.ShouldBe(3, "None is no filter");

        /* The archived relation matches on none of its keys. */
        (await MatchAsync(new RelationMatchRequestDto { Code = "ACM99" })).Matches.ShouldBeEmpty();
        (await MatchAsync(new RelationMatchRequestDto { VatNumber = "NL999999999B01" })).Matches.ShouldBeEmpty();
        (await MatchAsync(new RelationMatchRequestDto { ChamberOfCommerceNumber = "99999999" })).Matches.ShouldBeEmpty();
        (await MatchAsync(new RelationMatchRequestDto { Name = "archived", PostalCode = "1234AB", CountryId = _nl })).Matches.ShouldBeEmpty();
        (await MatchAsync(new RelationMatchRequestDto { Name = "archived" })).Matches.ShouldBeEmpty();

        /* Unarchiving brings it back. */
        var archived = (await _relations.GetListAsync(new RelationListRequestDto { IncludeArchived = true, WildcardSearch = "ACM99" })).Rows.ShouldHaveSingleItem();
        await _relations.UnarchiveAsync(archived.Id);
        Single(await MatchAsync(new RelationMatchRequestDto { Code = "ACM99" })).RelationId.ShouldBe(archived.Id);
    }

    [Fact]
    public async Task An_empty_request_or_one_without_a_name_beyond_the_exact_keys_gives_no_match()
    {
        using var _ = InTenantA();
        await SeedAsync();

        (await MatchAsync(new RelationMatchRequestDto())).Matches.ShouldBeEmpty();
        (await MatchAsync(new RelationMatchRequestDto { Code = " ", VatNumber = "", Name = "  ", PostalCode = "1234AB" })).Matches.ShouldBeEmpty();
        (await MatchAsync(new RelationMatchRequestDto { PostalCode = "1234AB", CountryId = _nl })).Matches.ShouldBeEmpty("a postcode alone is not a match");
        (await MatchAsync(new RelationMatchRequestDto { Email = "planning@acme.nl" })).Matches.ShouldBeEmpty("e-mail is carried but not a rule in this run");
        (await MatchAsync(new RelationMatchRequestDto { Name = "&&&" })).Matches.ShouldBeEmpty("a name that folds to nothing");
    }

    [Fact]
    public async Task The_service_endpoint_delegates_to_the_matcher()
    {
        using var _ = InTenantA();
        await SeedAsync();

        var viaService = await _relations.MatchAsync(new RelationMatchRequestDto { Name = "acme" });
        var direct = await MatchAsync(new RelationMatchRequestDto { Name = "acme" });

        viaService.Matches.Select(m => (m.RelationId, m.Kind, m.Score)).ShouldBe(direct.Matches.Select(m => (m.RelationId, m.Kind, m.Score)));
    }

    // ── Tenant isolation (Critical) ───────────────────────────────────────────

    [Fact]
    public async Task A_request_from_tenant_B_never_sees_tenant_A_on_any_rule()
    {
        using (InTenantA())
        {
            await SeedAsync();
        }

        using (InTenantB())
        {
            var matcher = GetRequiredService<IRelationMatcher>();
            (await WithUnitOfWorkAsync(() => matcher.MatchAsync(new RelationMatchRequestDto { Code = "ACM01" }))).Matches.ShouldBeEmpty();
            (await WithUnitOfWorkAsync(() => matcher.MatchAsync(new RelationMatchRequestDto { VatNumber = "NL801234567B01" }))).Matches.ShouldBeEmpty();
            (await WithUnitOfWorkAsync(() => matcher.MatchAsync(new RelationMatchRequestDto { ChamberOfCommerceNumber = "12345678" }))).Matches.ShouldBeEmpty();
            (await WithUnitOfWorkAsync(() => matcher.MatchAsync(new RelationMatchRequestDto { Name = "acme", PostalCode = "1234AB", CountryId = _nl }))).Matches.ShouldBeEmpty();
            (await WithUnitOfWorkAsync(() => matcher.MatchAsync(new RelationMatchRequestDto { Name = "acme", PostalCode = "1234AB" }))).Matches.ShouldBeEmpty();
            (await WithUnitOfWorkAsync(() => matcher.MatchAsync(new RelationMatchRequestDto { Name = "acme" }))).Matches.ShouldBeEmpty();
            (await WithUnitOfWorkAsync(() => matcher.MatchAsync(new RelationMatchRequestDto { Name = "a" }))).Matches.ShouldBeEmpty();

            var ownAcme = await GetRequiredService<IRelationAppService>().CreateAsync(new RelationCreateDto { Code = "ACM01", Name = "ACME of B", Roles = RelationRoles.Customer, VatNumber = "NL801234567B01" });
            Single(await WithUnitOfWorkAsync(() => matcher.MatchAsync(new RelationMatchRequestDto { Code = "ACM01" }))).RelationId.ShouldBe(ownAcme.Id);
            Single(await WithUnitOfWorkAsync(() => matcher.MatchAsync(new RelationMatchRequestDto { Name = "acme" }))).RelationId.ShouldBe(ownAcme.Id);
        }

        using (InTenantA())
        {
            Single(await MatchAsync(new RelationMatchRequestDto { Code = "ACM01" })).RelationId.ShouldBe(_acme.Id);
        }
    }
}
