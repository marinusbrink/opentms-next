using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OpenTms.MasterData;
using OpenTms.MasterData.EntityFrameworkCore;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Regions;
using OpenTms.MasterData.Relations;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Identity;
using Xunit;
using Xunit.Abstractions;

namespace OpenTms.PostgreSql.MasterData;

/// <summary>
/// Relation administration against real PostgreSQL (design 43, "Relation administration", PO 2026-10-13):
/// the relation detail loaded with split queries (one query per child collection) round-trips every child
/// collection with exact counts and keeps them on an unchanged update; the detail of a relation at the child
/// bounds stays within the interactive-read budget; the characteristics lookup page and the external-id
/// uniqueness check are tenant-scoped across the own-database tenant, the shared-tier tenant and the host.
/// Risk class: Critical for the tenant isolation (constitution rule 1); High for the split-query detail load.
/// Every test runs in one rolled-back transaction.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class RelationAdministrationPostgreSqlTests : MasterDataPostgreSqlTestBase
{
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(300);

    private readonly ITestOutputHelper _output;
    private readonly IRelationAppService _relations;
    private readonly ITransportCharacteristicsAppService _characteristics;
    private readonly IRelationCategoryAppService _categories;
    private readonly IGeographicalRegionAppService _regions;
    private readonly OrganizationUnitManager _organizationUnits;
    private readonly IRepository<Country, Guid> _countries;
    private readonly IRepository<AddressType, Guid> _addressTypes;
    private readonly IRepository<Relation, Guid> _relationRepository;
    private readonly IDbContextProvider<MasterDataDbContext> _contextProvider;

    public RelationAdministrationPostgreSqlTests(ITestOutputHelper output)
    {
        _output = output;
        _relations = GetRequiredService<IRelationAppService>();
        _characteristics = GetRequiredService<ITransportCharacteristicsAppService>();
        _categories = GetRequiredService<IRelationCategoryAppService>();
        _regions = GetRequiredService<IGeographicalRegionAppService>();
        _organizationUnits = GetRequiredService<OrganizationUnitManager>();
        _countries = GetRequiredService<IRepository<Country, Guid>>();
        _addressTypes = GetRequiredService<IRepository<AddressType, Guid>>();
        _relationRepository = GetRequiredService<IRepository<Relation, Guid>>();
        _contextProvider = GetRequiredService<IDbContextProvider<MasterDataDbContext>>();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>Flushes and forgets every tracked entity, so the next read comes from the database, not the change tracker.</summary>
    private async Task FlushAndForgetAsync()
    {
        await FlushAsync();
        (await _contextProvider.GetDbContextAsync()).ChangeTracker.Clear();
    }

    private async Task<Guid> CharacteristicAsync(string code, string name, string? description = null)
        => (await _characteristics.CreateAsync(new TransportCharacteristicsCreateDto { Code = code, Name = name, Description = description })).Id;

    private static RelationExternalIdentifierDto External(string source, string identifier, Guid? id = null) => new() { Id = id, Source = source, Identifier = identifier };

    /// <summary>A Dutch IBAN with the correct check digits for a 14-character BBAN.</summary>
    private static string ValidNlIban(string bban)
    {
        var remainder = 0;
        foreach (var ch in bban + "NL00")
        {
            var value = char.IsDigit(ch) ? ch - '0' : ch - 'A' + 10;
            remainder = value >= 10 ? (remainder * 100 + value) % 97 : (remainder * 10 + value) % 97;
        }

        return $"NL{98 - remainder:D2}{bban}";
    }

    /// <summary>The default address (no type) plus one address per seeded address type, up to <paramref name="typed"/>.</summary>
    private async Task<List<RelationAddressDto>> AddressesAsync(int typed)
    {
        var nl = await _countries.GetAsync(c => c.IsoAlpha2 == "NL");
        var types = (await _addressTypes.GetListAsync()).OrderBy(t => t.Kind).Take(typed).ToList();
        var addresses = new List<RelationAddressDto> { new() { AddressTypeId = null, CountryId = nl.Id, Street = "Default street", HouseNumber = "1", PostalCode = "1234 AB", City = "Utrecht" } };
        addresses.AddRange(types.Select((t, i) => new RelationAddressDto { AddressTypeId = t.Id, CountryId = nl.Id, Street = $"Street {i}", HouseNumber = $"{i + 2}", PostalCode = "1234 AB", City = "Utrecht" }));
        return addresses;
    }

    private static RelationUpdateDto UpdateFrom(RelationDto dto) => new()
    {
        Code = dto.Code,
        Name = dto.Name,
        FullName = dto.FullName,
        Roles = dto.Roles,
        IsArchived = dto.IsArchived,
        IsBlocked = dto.IsBlocked,
        BlockReason = dto.BlockReason,
        Phone = dto.Phone,
        Mobile = dto.Mobile,
        Email = dto.Email,
        Website = dto.Website,
        ChamberOfCommerceNumber = dto.ChamberOfCommerceNumber,
        VatNumber = dto.VatNumber,
        PreferredCulture = dto.PreferredCulture,
        DebtorNumber = dto.DebtorNumber,
        CreditorNumber = dto.CreditorNumber,
        CarrierCode = dto.CarrierCode,
        CurrencyId = dto.CurrencyId,
        ExternalIdentifiers = dto.ExternalIdentifiers.Select(e => new RelationExternalIdentifierDto { Id = e.Id, Source = e.Source, Identifier = e.Identifier }).ToList(),
        TransportCharacteristicIds = [.. dto.TransportCharacteristicIds],
        Description = dto.Description,
        Addresses = dto.Addresses.Select(a => new RelationAddressDto
        {
            Id = a.Id, AddressTypeId = a.AddressTypeId, CountryId = a.CountryId, Street = a.Street, HouseNumber = a.HouseNumber, HouseNumberSuffix = a.HouseNumberSuffix,
            AddressLine2 = a.AddressLine2, PostalCode = a.PostalCode, City = a.City, Latitude = a.Latitude, Longitude = a.Longitude, TimeZoneId = a.TimeZoneId
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

    private sealed record Counts(int Addresses, int Contacts, int Banks, int Categories, int Regions, int Units, int Externals, int Characteristics);

    private static void ShouldHaveExactly(RelationDto dto, Counts expected, string when)
    {
        dto.Addresses.Count.ShouldBe(expected.Addresses, when);
        dto.ContactPersons.Count.ShouldBe(expected.Contacts, when);
        dto.BankAccounts.Count.ShouldBe(expected.Banks, when);
        dto.CategoryIds.Count.ShouldBe(expected.Categories, when);
        dto.GeographicalRegionIds.Count.ShouldBe(expected.Regions, when);
        dto.OrganizationUnitIds.Count.ShouldBe(expected.Units, when);
        dto.ExternalIdentifiers.Count.ShouldBe(expected.Externals, when);
        dto.TransportCharacteristicIds.Count.ShouldBe(expected.Characteristics, when);
        dto.TransportCharacteristics.Count.ShouldBe(expected.Characteristics, when);

        /* No row twice: a cartesian product would repeat children. */
        dto.Addresses.Select(a => a.Id).Distinct().Count().ShouldBe(expected.Addresses, when);
        dto.ContactPersons.Select(c => c.Id).Distinct().Count().ShouldBe(expected.Contacts, when);
        dto.BankAccounts.Select(b => b.Id).Distinct().Count().ShouldBe(expected.Banks, when);
        dto.CategoryIds.Distinct().Count().ShouldBe(expected.Categories, when);
        dto.GeographicalRegionIds.Distinct().Count().ShouldBe(expected.Regions, when);
        dto.OrganizationUnitIds.Distinct().Count().ShouldBe(expected.Units, when);
        dto.ExternalIdentifiers.Select(e => e.Id).Distinct().Count().ShouldBe(expected.Externals, when);
        dto.TransportCharacteristicIds.Distinct().Count().ShouldBe(expected.Characteristics, when);
    }

    // ── Split-query detail load (High) ───────────────────────────────────────

    [Fact]
    public async Task The_detail_load_runs_as_split_queries()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var sql = (await _relationRepository.WithDetailsAsync()).Where(r => r.Id == Guid.Empty).ToQueryString();

            _output.WriteLine(sql);
            sql.ShouldContain("split-query", Case.Insensitive, "DefaultWithDetailsFunc must call AsSplitQuery()");
        });
    }

    [Fact]
    public async Task A_relation_with_several_rows_in_every_child_collection_round_trips_with_exact_counts_and_keeps_them_on_update()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var addresses = await AddressesAsync(typed: 2);
            var categories = new List<Guid>();
            for (var i = 0; i < 2; i++)
            {
                categories.Add((await _categories.CreateAsync(new RelationCategoryCreateDto { Name = $"QQ split category {i}" })).Id);
            }

            var regions = new List<Guid>();
            for (var i = 0; i < 3; i++)
            {
                regions.Add((await _regions.CreateAsync(new GeographicalRegionCreateDto { Name = $"QQ split region {i}" })).Id);
            }

            var units = new List<Guid>();
            for (var i = 0; i < 2; i++)
            {
                var unit = new OrganizationUnit(Guid.NewGuid(), $"QQ split unit {i}", null, TenantA);
                await _organizationUnits.CreateAsync(unit);
                units.Add(unit.Id);
            }

            var characteristics = new List<Guid>();
            for (var i = 0; i < 5; i++)
            {
                characteristics.Add(await CharacteristicAsync($"QQ-SPL{i}", $"QQ split characteristic {i}", i % 2 == 0 ? $"description {i}" : null));
            }

            await FlushAsync();

            var created = await _relations.CreateAsync(new RelationCreateDto
            {
                Code = "QQ-SPLIT", Name = "Split query probe", Roles = RelationRoles.Customer | RelationRoles.Carrier,
                Addresses = addresses,
                ContactPersons = Enumerable.Range(0, 4).Select(i => new RelationContactPersonDto { FirstName = $"First{i}", LastName = $"Last{i}" }).ToList(),
                BankAccounts = Enumerable.Range(0, 3).Select(i => new RelationBankAccountDto { Iban = ValidNlIban($"ABNA04171643{i:D2}"), IsDefault = i == 0 }).ToList(),
                CategoryIds = categories,
                GeographicalRegionIds = regions,
                OrganizationUnitIds = units,
                ExternalIdentifiers = [External("EDI", "QQ-1"), External("EDI", "QQ-2"), External("Exact", "QQ-1"), External("Portal", "QQ-P")],
                TransportCharacteristicIds = characteristics
            });
            var expected = new Counts(addresses.Count, 4, 3, 2, 3, 2, 4, 5);
            addresses.Count.ShouldBeGreaterThanOrEqualTo(2, "the seeded address types give at least the default plus one typed address");
            ShouldHaveExactly(created, expected, "create");

            await FlushAndForgetAsync();
            var loaded = await _relations.GetAsync(created.Id);
            ShouldHaveExactly(loaded, expected, "fresh load from the database");
            loaded.CategoryIds.ShouldBe(categories, ignoreOrder: true);
            loaded.GeographicalRegionIds.ShouldBe(regions, ignoreOrder: true);
            loaded.OrganizationUnitIds.ShouldBe(units, ignoreOrder: true);
            loaded.TransportCharacteristics.Select(c => c.Id).ShouldBe(characteristics, "ordered by name, which here is the creation order");
            loaded.ExternalIdentifiers.Select(e => (e.Source, e.Identifier)).ShouldBe([("EDI", "QQ-1"), ("EDI", "QQ-2"), ("Exact", "QQ-1"), ("Portal", "QQ-P")]);

            await FlushAndForgetAsync();
            var updated = await _relations.UpdateAsync(created.Id, UpdateFrom(loaded));
            ShouldHaveExactly(updated, expected, "update with the same DTO");

            await FlushAndForgetAsync();
            var reloaded = await _relations.GetAsync(created.Id);
            ShouldHaveExactly(reloaded, expected, "fresh load after the update");
            reloaded.Addresses.Select(a => a.Id).ShouldBe(loaded.Addresses.Select(a => a.Id), ignoreOrder: true, "rows kept by id, not recreated");
            reloaded.ContactPersons.Select(c => c.Id).ShouldBe(loaded.ContactPersons.Select(c => c.Id), ignoreOrder: true);
            reloaded.BankAccounts.Select(b => b.Id).ShouldBe(loaded.BankAccounts.Select(b => b.Id), ignoreOrder: true);
            reloaded.ExternalIdentifiers.Select(e => e.Id).ShouldBe(loaded.ExternalIdentifiers.Select(e => e.Id), ignoreOrder: true);
        });
    }

    /// <summary>
    /// VOLUME TEST at the child bounds (design 43 security quickscan: 100 contact persons, 10 bank accounts,
    /// 50 external ids, 50 transport characteristics; far above five times the expected 2-3 per collection):
    /// the detail read stays within the 300 ms interactive-read budget warm. Without split queries this load
    /// is a join of 100 × 10 × 50 × 50 rows.
    /// </summary>
    [Fact]
    public async Task The_detail_of_a_relation_at_the_child_bounds_loads_within_the_interactive_read_budget()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var characteristics = new List<Guid>();
            for (var i = 0; i < MasterDataConsts.MaxTransportCharacteristicsPerRelation; i++)
            {
                characteristics.Add(await CharacteristicAsync($"QQ-VOL{i:D2}", $"QQ volume characteristic {i:D2}", $"description {i}"));
            }

            var addresses = await AddressesAsync(typed: 10);
            var created = await _relations.CreateAsync(new RelationCreateDto
            {
                Code = "QQ-BOUNDS", Name = "Bounds probe", Roles = RelationRoles.Customer,
                Addresses = addresses,
                ContactPersons = Enumerable.Range(0, MasterDataConsts.MaxContactPersonsPerRelation).Select(i => new RelationContactPersonDto { FirstName = $"First{i}", LastName = $"Last{i:D3}" }).ToList(),
                BankAccounts = Enumerable.Range(0, MasterDataConsts.MaxBankAccountsPerRelation).Select(i => new RelationBankAccountDto { Iban = ValidNlIban($"ABNA04171643{i:D2}"), IsDefault = i == 0 }).ToList(),
                ExternalIdentifiers = Enumerable.Range(0, MasterDataConsts.MaxExternalIdentifiersPerRelation).Select(i => External(i % 2 == 0 ? "EDI" : "Exact", $"QQ-VOL-{i:D2}")).ToList(),
                TransportCharacteristicIds = characteristics
            });
            var expected = new Counts(addresses.Count, MasterDataConsts.MaxContactPersonsPerRelation, MasterDataConsts.MaxBankAccountsPerRelation, 0, 0, 0,
                MasterDataConsts.MaxExternalIdentifiersPerRelation, MasterDataConsts.MaxTransportCharacteristicsPerRelation);

            TimeSpan cold = default, warm = default;
            for (var run = 0; run < 3; run++)
            {
                await FlushAndForgetAsync();
                var stopwatch = Stopwatch.StartNew();
                var detail = await _relations.GetAsync(created.Id);
                stopwatch.Stop();
                ShouldHaveExactly(detail, expected, $"run {run}");
                if (run == 0)
                {
                    cold = stopwatch.Elapsed;
                }
                else
                {
                    warm = stopwatch.Elapsed;
                }
            }

            _output.WriteLine($"relation detail at the child bounds: cold {cold.TotalMilliseconds:F0} ms, warm {warm.TotalMilliseconds:F0} ms (budget {Budget.TotalMilliseconds} ms)");
            warm.ShouldBeLessThanOrEqualTo(Budget, $"warm detail read {warm.TotalMilliseconds:F0} ms over the {Budget.TotalMilliseconds} ms interactive-read budget");
        });
    }

    // ── Tenant isolation (Critical) ───────────────────────────────────────────

    [Fact]
    public async Task The_characteristics_lookup_page_of_tenant_A_is_invisible_and_uncounted_for_the_shared_tenant_and_the_host()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            long sharedBefore, hostBefore;
            using (CurrentTenant.Change(SharedTenant))
            {
                sharedBefore = (await _characteristics.GetLookupPageAsync()).TotalCount;
            }

            using (CurrentTenant.Change(null))
            {
                hostBefore = (await _characteristics.GetLookupPageAsync()).TotalCount;
            }

            var tail = await CharacteristicAsync("QQ-TC3", "QQ Beta tail lift", "Isolation description");
            var cool2 = await CharacteristicAsync("QQ-TC2", "QQ Alpha cooled");
            var cool1 = await CharacteristicAsync("QQ-TC1", "QQ Alpha cooled", "reefer");
            await FlushAsync();

            var own = await _characteristics.GetLookupPageAsync("qq-tc");
            own.TotalCount.ShouldBe(3);
            own.Items.Select(i => i.Id).ShouldBe([cool1, cool2, tail], "by name, then code, on PostgreSQL");
            (await _characteristics.GetLookupPageAsync("QQ ALPHA")).TotalCount.ShouldBe(2, "case-insensitive on PostgreSQL");
            (await _characteristics.GetLookupPageAsync("isolation desc")).Items.ShouldHaveSingleItem().Id.ShouldBe(tail);
            (await _characteristics.GetLookupPageAsync("qq-tc", skip: 1, take: 1)).Items.ShouldHaveSingleItem().Id.ShouldBe(cool2);

            using (CurrentTenant.Change(SharedTenant))
            {
                (await _characteristics.GetLookupPageAsync()).TotalCount.ShouldBe(sharedBefore, "tenant A's rows are not counted");
                (await _characteristics.GetLookupPageAsync("qq-tc")).TotalCount.ShouldBe(0);
                (await _characteristics.GetLookupPageAsync("isolation desc")).Items.ShouldBeEmpty();

                var sharedOwn = await CharacteristicAsync("QQ-TC1", "QQ Alpha cooled of the shared tenant");
                await FlushAsync();
                var page = await _characteristics.GetLookupPageAsync("qq-tc");
                page.TotalCount.ShouldBe(1);
                page.Items.ShouldHaveSingleItem().Id.ShouldBe(sharedOwn);

                /* A relation of the shared tenant cannot point at tenant A's characteristic. */
                var ex = await Should.ThrowAsync<BusinessException>(() => _relations.CreateAsync(new RelationCreateDto
                {
                    Code = "QQ-TCREF", Name = "Reaching into A", Roles = RelationRoles.Customer, TransportCharacteristicIds = [cool1]
                }));
                ex.Code.ShouldBe(MasterDataErrorCodes.ReferenceNotFound);
                ex.Data["field"].ShouldBe("transportCharacteristicIds");
            }

            using (CurrentTenant.Change(null))
            {
                (await _characteristics.GetLookupPageAsync()).TotalCount.ShouldBe(hostBefore, "neither tenant's rows are counted for the host");
                (await _characteristics.GetLookupPageAsync("qq-tc")).Items.ShouldBeEmpty();
            }

            (await _characteristics.GetLookupPageAsync("qq-tc")).Items.Select(i => i.Id).ShouldBe([cool1, cool2, tail], "tenant A still sees its own three only");
        });
    }

    [Fact]
    public async Task The_same_external_id_pair_in_two_tenants_of_one_database_is_accepted_and_each_tenant_checks_only_its_own()
    {
        /* The shared-tier tenant and the host live in the same (host) database: the unique index includes TenantId
         * and the service's one-query check runs through the tenant filter, so neither counts the other's rows. */
        await InRolledBackTransactionAsync(SharedTenant, async () =>
        {
            var shared = await _relations.CreateAsync(new RelationCreateDto
            {
                Code = "QQ-EXT", Name = "External id probe", Roles = RelationRoles.Customer, ExternalIdentifiers = [External("EDI", "QQ-8712")]
            });
            await FlushAsync();

            using (CurrentTenant.Change(null))
            {
                var host = await _relations.CreateAsync(new RelationCreateDto
                {
                    Code = "QQ-EXT", Name = "External id probe of the host", Roles = RelationRoles.Customer, ExternalIdentifiers = [External("EDI", "QQ-8712")]
                });
                await FlushAsync();
                host.ExternalIdentifiers.ShouldHaveSingleItem().Identifier.ShouldBe("QQ-8712");

                var resave = UpdateFrom(host);
                resave.Name = "Host again";
                (await _relations.UpdateAsync(host.Id, resave)).ExternalIdentifiers.ShouldHaveSingleItem().Source.ShouldBe("EDI");
                await FlushAsync();
            }

            using (CurrentTenant.Change(TenantA))
            {
                var inA = await _relations.CreateAsync(new RelationCreateDto
                {
                    Code = "QQ-EXT", Name = "External id probe of A", Roles = RelationRoles.Customer, ExternalIdentifiers = [External("EDI", "QQ-8712")]
                });
                await FlushAsync();
                inA.ExternalIdentifiers.ShouldHaveSingleItem();
            }

            /* Within the shared tenant the pair is still its own: re-saving is accepted, a second relation is refused. */
            var same = UpdateFrom(shared);
            same.Name = "External id probe again";
            (await _relations.UpdateAsync(shared.Id, same)).ExternalIdentifiers.ShouldHaveSingleItem().Identifier.ShouldBe("QQ-8712");
            var clash = await Should.ThrowAsync<BusinessException>(() => _relations.CreateAsync(new RelationCreateDto
            {
                Code = "QQ-EXT2", Name = "Second holder", Roles = RelationRoles.Customer, ExternalIdentifiers = [External("EDI", "QQ-8712")]
            }));
            clash.Code.ShouldBe(MasterDataErrorCodes.DuplicateKey);
            clash.Data["value"].ShouldBe("EDI QQ-8712");
        });
    }
}
