using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData;
using OpenTms.MasterData.Drivers;
using OpenTms.MasterData.PersonalData;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Relations;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Xunit;

namespace OpenTms.PostgreSql.MasterData;

/// <summary>
/// Tenant isolation of relations, their children, drivers, the lookup page, the matcher and the
/// personal-data selector against real PostgreSQL (design 43, test risk analysis, Critical): rows
/// written by tenant A (own database) are unreachable from the shared-tier tenant (host database)
/// and from the host through every operation; a reference to tenant A's country, address type,
/// relation or user is refused; the shared tenant has its own own-company slot. Constitution rule 1.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class RelationTenantIsolationTests : MasterDataPostgreSqlTestBase
{
    private readonly IRelationAppService _relations;
    private readonly IDriverAppService _drivers;
    private readonly IRelationMatcher _matcher;
    private readonly IPersonalDataSource _personalData;
    private readonly IRepository<Country, Guid> _countries;
    private readonly IRepository<AddressType, Guid> _addressTypes;
    private readonly IdentityUserManager _userManager;

    public RelationTenantIsolationTests()
    {
        _relations = GetRequiredService<IRelationAppService>();
        _drivers = GetRequiredService<IDriverAppService>();
        _matcher = GetRequiredService<IRelationMatcher>();
        _personalData = GetRequiredService<IPersonalDataSource>();
        _countries = GetRequiredService<IRepository<Country, Guid>>();
        _addressTypes = GetRequiredService<IRepository<AddressType, Guid>>();
        _userManager = GetRequiredService<IdentityUserManager>();
    }

    private static RelationListRequestDto Request(string? search = null, bool includeArchived = false)
    {
        var request = List<RelationListRequestDto>(search: search);
        request.IncludeArchived = includeArchived;
        return request;
    }

    private static RelationUpdateDto UpdateFrom(RelationDto dto) => new()
    {
        Code = dto.Code, Name = "Changed", Roles = dto.Roles, ConcurrencyStamp = dto.ConcurrencyStamp
    };

    [Fact]
    public async Task A_relation_with_children_of_tenant_A_is_unreachable_from_the_shared_tenant_and_the_host()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var nl = await _countries.GetAsync(c => c.IsoAlpha2 == "NL");
            var visiting = await _addressTypes.GetAsync(t => t.Kind == AddressTypeKind.Visiting);
            var probe = await _relations.CreateAsync(new RelationCreateDto
            {
                Code = "QQ-ISO", Name = "Isolation probe relation", Roles = RelationRoles.OwnCompany | RelationRoles.Carrier,
                VatNumber = "NL801234567B01", ChamberOfCommerceNumber = "12345678",
                Addresses = [new RelationAddressDto { AddressTypeId = visiting.Id, CountryId = nl.Id, Street = "Main", PostalCode = "1234 AB", City = "Isolationstad" }],
                ContactPersons = [new RelationContactPersonDto { FirstName = "Jan", LastName = "Jansen", Email = "jan@isolation.nl" }],
                BankAccounts = [new RelationBankAccountDto { Iban = "NL91ABNA0417164300" }]
            });
            await FlushAsync();

            foreach (var other in new Guid?[] { SharedTenant, null })
            {
                using (CurrentTenant.Change(other))
                {
                    (await _relations.GetListAsync(Request(includeArchived: true))).TotalCount.ShouldBe(0);
                    (await _relations.GetListAsync(Request(search: "isolation"))).Rows.ShouldBeEmpty();
                    (await _relations.GetListAsync(Request(search: "NL8012"))).Rows.ShouldBeEmpty();
                    (await _relations.GetListAsync(Request(search: "isolationstad"))).Rows.ShouldBeEmpty("the city of a child address");
                    (await _relations.GetLookupAsync()).ShouldBeEmpty();
                    (await _relations.GetLookupPageAsync("isolation")).TotalCount.ShouldBe(0);
                    (await _relations.GetLookupPageAsync(roles: RelationRoles.OwnCompany)).Items.ShouldBeEmpty();
                    await Should.ThrowAsync<EntityNotFoundException>(() => _relations.GetAsync(probe.Id));
                    await Should.ThrowAsync<EntityNotFoundException>(() => _relations.GetAddressesAsync(probe.Id));
                    await Should.ThrowAsync<EntityNotFoundException>(() => _relations.UpdateAsync(probe.Id, UpdateFrom(probe)));
                    await Should.ThrowAsync<EntityNotFoundException>(() => _relations.ArchiveAsync(probe.Id));
                    await Should.ThrowAsync<EntityNotFoundException>(() => _relations.DeleteAsync(probe.Id));
                    (await _relations.BulkDeleteAsync(new RelationBulkDeleteRequestDto { Selection = Explicit(probe.Id) })).DeletedCount.ShouldBe(0);
                    (await _relations.BulkDeleteAsync(new RelationBulkDeleteRequestDto { Selection = FilterBased(), IncludeArchived = true })).DeletedCount.ShouldBe(0);

                    /* The matcher on every rule. */
                    (await _matcher.MatchAsync(new RelationMatchRequestDto { Code = "QQ-ISO" })).Matches.ShouldBeEmpty();
                    (await _matcher.MatchAsync(new RelationMatchRequestDto { VatNumber = "NL801234567B01" })).Matches.ShouldBeEmpty();
                    (await _matcher.MatchAsync(new RelationMatchRequestDto { ChamberOfCommerceNumber = "12345678" })).Matches.ShouldBeEmpty();
                    (await _matcher.MatchAsync(new RelationMatchRequestDto { Name = "isolation", PostalCode = "1234AB" })).Matches.ShouldBeEmpty();
                    (await _matcher.MatchAsync(new RelationMatchRequestDto { Name = "isolation" })).Matches.ShouldBeEmpty();

                    /* The art. 15 selector. */
                    (await _personalData.FindAsync("jan@isolation.nl", null)).ShouldBeEmpty();

                    /* Tenant A's reference ids are refused. */
                    var ex = await Should.ThrowAsync<BusinessException>(() => _relations.CreateAsync(new RelationCreateDto
                    {
                        Code = "QQ-REF", Name = "Reaching into A", Roles = RelationRoles.Customer,
                        Addresses = [new RelationAddressDto { AddressTypeId = visiting.Id, CountryId = nl.Id, Street = "Main", PostalCode = "1234 AB", City = "X" }]
                    }));
                    ex.Code.ShouldBe(MasterDataErrorCodes.ReferenceNotFound);
                    await FlushAsync();
                }
            }

            /* The shared tenant has its own own-company slot and key space in the host database, invisible to the host. */
            using (CurrentTenant.Change(SharedTenant))
            {
                var own = await _relations.CreateAsync(new RelationCreateDto { Code = "QQ-ISO", Name = "Isolation probe relation", Roles = RelationRoles.OwnCompany, VatNumber = "NL801234567B01" });
                own.Id.ShouldNotBe(probe.Id);
                (await _relations.GetListAsync(Request(search: "isolation probe"))).Rows.ShouldHaveSingleItem().Id.ShouldBe(own.Id);
                (await _relations.GetLookupPageAsync("isolation")).Items.ShouldHaveSingleItem().Id.ShouldBe(own.Id);
                (await _matcher.MatchAsync(new RelationMatchRequestDto { Code = "QQ-ISO" })).Matches.ShouldHaveSingleItem().RelationId.ShouldBe(own.Id);
                await FlushAsync();
            }

            using (CurrentTenant.Change(null))
            {
                (await _relations.GetListAsync(Request(search: "isolation probe"))).Rows.ShouldBeEmpty();
                (await _matcher.MatchAsync(new RelationMatchRequestDto { Code = "QQ-ISO" })).Matches.ShouldBeEmpty();
            }

            /* Tenant A still sees its own row with its children. */
            var mine = await _relations.GetAsync(probe.Id);
            mine.Addresses.ShouldHaveSingleItem().City.ShouldBe("Isolationstad");
            mine.ContactPersons.ShouldHaveSingleItem();
            mine.BankAccounts.ShouldHaveSingleItem();
            (await _matcher.MatchAsync(new RelationMatchRequestDto { Code = "QQ-ISO" })).Matches.ShouldHaveSingleItem().RelationId.ShouldBe(probe.Id);
            (await _personalData.FindAsync("jan@isolation.nl", null)).ShouldHaveSingleItem().Kind.ShouldBe("RelationContactPerson");
        });
    }

    [Fact]
    public async Task A_driver_of_tenant_A_is_unreachable_from_the_shared_tenant_and_the_host_and_its_employer_and_user_cannot_be_borrowed()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var carrier = await _relations.CreateAsync(new RelationCreateDto { Code = "QQ-CAR", Name = "Isolation carrier", Roles = RelationRoles.Carrier });
            var user = new IdentityUser(Guid.NewGuid(), "qq.isolation", "qq.isolation@test.nl", TenantA);
            (await _userManager.CreateAsync(user)).Succeeded.ShouldBeTrue();
            var probe = await _drivers.CreateAsync(new DriverCreateDto
            {
                Code = "QQ-DRV", FirstName = "Jan", LastName = "Isolation", EmployerRelationId = carrier.Id, UserId = user.Id, Email = "jan@isolation.nl",
                Qualifications = [new DriverQualificationDto { Kind = DriverQualificationKind.Code95, ValidUntil = new DateOnly(2030, 1, 1) }]
            });
            await FlushAsync();
            var update = new DriverUpdateDto { Code = "QQ-DRV", FirstName = "Changed", LastName = "Isolation", ConcurrencyStamp = probe.ConcurrencyStamp };

            foreach (var other in new Guid?[] { SharedTenant, null })
            {
                using (CurrentTenant.Change(other))
                {
                    (await _drivers.GetListAsync(new DriverListRequestDto { IncludeArchived = true })).TotalCount.ShouldBe(0);
                    (await _drivers.GetListAsync(new DriverListRequestDto { WildcardSearch = "isolation" })).Rows.ShouldBeEmpty();
                    (await _drivers.GetListAsync(new DriverListRequestDto { EmployerRelationId = carrier.Id })).Rows.ShouldBeEmpty();
                    (await _drivers.GetLookupAsync()).ShouldBeEmpty();
                    (await _drivers.GetUserLookupPageAsync("qq.isolation")).TotalCount.ShouldBe(0);
                    await Should.ThrowAsync<EntityNotFoundException>(() => _drivers.GetAsync(probe.Id));
                    await Should.ThrowAsync<EntityNotFoundException>(() => _drivers.UpdateAsync(probe.Id, update));
                    await Should.ThrowAsync<EntityNotFoundException>(() => _drivers.ArchiveAsync(probe.Id));
                    await Should.ThrowAsync<EntityNotFoundException>(() => _drivers.AnonymiseAsync(probe.Id));
                    await Should.ThrowAsync<EntityNotFoundException>(() => _drivers.DeleteAsync(probe.Id));
                    (await _drivers.BulkDeleteAsync(new DriverBulkDeleteRequestDto { Selection = Explicit(probe.Id) })).DeletedCount.ShouldBe(0);
                    (await _drivers.BulkDeleteAsync(new DriverBulkDeleteRequestDto { Selection = FilterBased(), IncludeArchived = true })).DeletedCount.ShouldBe(0);
                    (await _personalData.FindAsync("jan@isolation.nl", user.Id)).ShouldBeEmpty();

                    var employer = await Should.ThrowAsync<BusinessException>(() => _drivers.CreateAsync(new DriverCreateDto { Code = "QQ-X", FirstName = "X", LastName = "Y", EmployerRelationId = carrier.Id }));
                    employer.Code.ShouldBe(MasterDataErrorCodes.ReferenceNotFound);
                    var account = await Should.ThrowAsync<BusinessException>(() => _drivers.CreateAsync(new DriverCreateDto { Code = "QQ-X", FirstName = "X", LastName = "Y", UserId = user.Id }));
                    account.Code.ShouldBe(MasterDataErrorCodes.UserNotFound);
                    await FlushAsync();
                }
            }

            using (CurrentTenant.Change(SharedTenant))
            {
                var own = await _drivers.CreateAsync(new DriverCreateDto { Code = "QQ-DRV", FirstName = "Jan", LastName = "Isolation", Email = "jan@isolation.nl" });
                own.Id.ShouldNotBe(probe.Id);
                (await _drivers.GetListAsync(new DriverListRequestDto { WildcardSearch = "isolation" })).Rows.ShouldHaveSingleItem().Id.ShouldBe(own.Id);
                (await _personalData.FindAsync("jan@isolation.nl", null)).ShouldHaveSingleItem().Id.ShouldBe(own.Id);
                await FlushAsync();
            }

            using (CurrentTenant.Change(null))
            {
                (await _drivers.GetListAsync(new DriverListRequestDto { WildcardSearch = "isolation" })).Rows.ShouldBeEmpty();
                (await _personalData.FindAsync("jan@isolation.nl", null)).ShouldBeEmpty();
            }

            var mine = await _drivers.GetAsync(probe.Id);
            mine.FirstName.ShouldBe("Jan");
            mine.Qualifications.ShouldHaveSingleItem();
            mine.UserName.ShouldBe("qq.isolation");
            (await _personalData.FindAsync(null, user.Id)).ShouldHaveSingleItem().Id.ShouldBe(probe.Id);
        });
    }
}
