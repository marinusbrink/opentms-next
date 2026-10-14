using OpenTms.Platform.Features;
using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Features;
using OpenTms.MasterData;
using OpenTms.MasterData.Drivers;
using OpenTms.MasterData.PersonalData;
using OpenTms.MasterData.Relations;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Validation;
using Volo.Abp.Timing;
using Xunit;

namespace OpenTms.EntityFrameworkCore.MasterData;

/// <summary>
/// DriverAppService and the art. 15 PersonalDataSource on SQLite (design 43). Risk class: Critical for
/// tenant isolation, the user link (one account per driver, existence in the tenant), anonymise and the
/// personal-data selector, the feature flag; High for the employer role rule, the qualifications and
/// the expiry count, the list and the bulk delete.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class DriverAppServiceTests : MasterDataAppServiceTestBase
{
    private readonly IDriverAppService _service;
    private readonly IRelationAppService _relations;
    private readonly IPersonalDataSource _personalData;
    private readonly IdentityUserManager _userManager;
    private readonly IRepository<DriverQualification, Guid> _qualificationRows;
    private readonly IClock _clock;

    public DriverAppServiceTests()
    {
        _service = GetRequiredService<IDriverAppService>();
        _relations = GetRequiredService<IRelationAppService>();
        _personalData = GetRequiredService<IPersonalDataSource>();
        _userManager = GetRequiredService<IdentityUserManager>();
        _qualificationRows = GetRequiredService<IRepository<DriverQualification, Guid>>();
        _clock = GetRequiredService<IClock>();
    }

    private DateOnly Today => DateOnly.FromDateTime(_clock.Now);

    // ── set-up ───────────────────────────────────────────────────────────────

    private Task<RelationDto> CreateRelationAsync(string code, string name, RelationRoles roles)
        => _relations.CreateAsync(new RelationCreateDto { Code = code, Name = name, Roles = roles });

    private async Task<IdentityUser> CreateUserAsync(string userName, string email)
    {
        var user = new IdentityUser(Guid.NewGuid(), userName, email, CurrentTenant.Id);
        await WithUnitOfWorkAsync(async () =>
        {
            var result = await _userManager.CreateAsync(user);
            result.Succeeded.ShouldBeTrue(string.Join("; ", result.Errors.Select(e => e.Description)));
        });
        return user;
    }

    private static DriverQualificationDto Qualification(DriverQualificationKind kind, string? number = null, DateOnly? from = null, DateOnly? until = null, Guid? id = null)
        => new() { Id = id, Kind = kind, Number = number, ValidFrom = from, ValidUntil = until };

    private static DriverCreateDto Dto(string code, string first, string last, Guid? employerId = null, Guid? userId = null, Action<DriverCreateDto>? configure = null)
    {
        var dto = new DriverCreateDto { Code = code, FirstName = first, LastName = last, EmployerRelationId = employerId, UserId = userId };
        configure?.Invoke(dto);
        return dto;
    }

    private Task<DriverDto> CreateAsync(string code, string first, string last, Guid? employerId = null, Guid? userId = null, Action<DriverCreateDto>? configure = null)
        => _service.CreateAsync(Dto(code, first, last, employerId, userId, configure));

    private static DriverUpdateDto UpdateFrom(DriverDto dto) => new()
    {
        Code = dto.Code,
        FirstName = dto.FirstName,
        MiddleName = dto.MiddleName,
        LastName = dto.LastName,
        Initials = dto.Initials,
        Phone = dto.Phone,
        Mobile = dto.Mobile,
        Email = dto.Email,
        PreferredCulture = dto.PreferredCulture,
        Description = dto.Description,
        EmployerRelationId = dto.EmployerRelationId,
        UserId = dto.UserId,
        IsArchived = dto.IsArchived,
        Qualifications = dto.Qualifications.Select(q => new DriverQualificationDto { Id = q.Id, Kind = q.Kind, Number = q.Number, ValidFrom = q.ValidFrom, ValidUntil = q.ValidUntil, Description = q.Description }).ToList(),
        ConcurrencyStamp = dto.ConcurrencyStamp
    };

    private static DriverListRequestDto Request(string? search = null, string? sortBy = null, string? sortDir = null, bool includeArchived = false, Guid? employerId = null, int start = 0, int end = 100)
    {
        var request = List<DriverListRequestDto>(start, end, search, sortBy, sortDir);
        request.IncludeArchived = includeArchived;
        request.EmployerRelationId = employerId;
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
    public async Task Create_get_list_and_lookup_round_trip_with_employer_user_and_qualifications()
    {
        using var _ = InTenantA();
        var carrier = await CreateRelationAsync("CAR", "Carrier B.V.", RelationRoles.Carrier);
        var user = await CreateUserAsync("jan.jansen", "jan@carrier.nl");

        var created = await CreateAsync(" drv01 ", " Jan ", " Jansen ", carrier.Id, user.Id, dto =>
        {
            dto.MiddleName = "van der";
            dto.Initials = "J.P.";
            dto.Phone = "020-1";
            dto.Mobile = " 06-1 ";
            dto.Email = " jan@carrier.nl ";
            dto.PreferredCulture = "NL";
            dto.Description = "night shifts";
            dto.Qualifications =
            [
                Qualification(DriverQualificationKind.DrivingLicenceCE, "CE-1", Today.AddYears(-5), Today.AddDays(30)),
                Qualification(DriverQualificationKind.Code95, "95-1", until: Today.AddDays(-10)),
                Qualification(DriverQualificationKind.AdrCertificate, "ADR-1", until: Today.AddDays(365)),
                Qualification(DriverQualificationKind.TachographCard)
            ];
        });

        created.Code.ShouldBe("DRV01");
        created.FirstName.ShouldBe("Jan");
        created.MiddleName.ShouldBe("van der");
        created.LastName.ShouldBe("Jansen");
        created.FullName.ShouldBe("Jan van der Jansen");
        created.Initials.ShouldBe("J.P.");
        created.Mobile.ShouldBe("06-1");
        created.Email.ShouldBe("jan@carrier.nl");
        created.PreferredCulture.ShouldBe("nl");
        created.Description.ShouldBe("night shifts");
        created.EmployerRelationId.ShouldBe(carrier.Id);
        created.EmployerName.ShouldBe("Carrier B.V.");
        created.UserId.ShouldBe(user.Id);
        created.UserName.ShouldBe("jan.jansen");
        created.IsArchived.ShouldBeFalse();
        created.IsAnonymised.ShouldBeFalse();
        created.Qualifications.Count.ShouldBe(4);
        created.Qualifications.ShouldAllBe(q => q.Id != null);
        created.Qualifications.Select(q => q.Kind).ShouldBe([DriverQualificationKind.DrivingLicenceCE, DriverQualificationKind.Code95, DriverQualificationKind.AdrCertificate, DriverQualificationKind.TachographCard], "ordered by kind");
        var ce = created.Qualifications.Single(q => q.Kind == DriverQualificationKind.DrivingLicenceCE);
        ce.Number.ShouldBe("CE-1");
        ce.ValidFrom.ShouldBe(Today.AddYears(-5));
        ce.ValidUntil.ShouldBe(Today.AddDays(30));

        var fetched = await _service.GetAsync(created.Id);
        fetched.Qualifications.Count.ShouldBe(4);
        fetched.EmployerName.ShouldBe("Carrier B.V.");
        fetched.UserName.ShouldBe("jan.jansen");

        var row = (await _service.GetListAsync(Request())).Rows.ShouldHaveSingleItem();
        row.FullName.ShouldBe("Jan van der Jansen");
        row.EmployerRelationId.ShouldBe(carrier.Id);
        row.EmployerName.ShouldBe("Carrier B.V.");
        row.HasUser.ShouldBeTrue();
        row.Mobile.ShouldBe("06-1");
        row.QualificationsExpiring.ShouldBe(2, "within 90 days or already expired; the far one and the undated one do not count");

        var lookup = (await _service.GetLookupAsync("jans")).ShouldHaveSingleItem();
        lookup.Code.ShouldBe("DRV01");
        lookup.Name.ShouldBe("Jan van der Jansen");
    }

    [Fact]
    public async Task A_driver_without_employer_user_or_qualifications_is_allowed()
    {
        using var _ = InTenantA();

        var created = await CreateAsync("D1", "Piet", "Pietersen");

        created.EmployerRelationId.ShouldBeNull();
        created.EmployerName.ShouldBeNull();
        created.UserId.ShouldBeNull();
        created.UserName.ShouldBeNull();
        created.Qualifications.ShouldBeEmpty();
        var row = (await _service.GetListAsync(Request())).Rows.ShouldHaveSingleItem();
        row.HasUser.ShouldBeFalse();
        row.EmployerName.ShouldBeNull();
        row.QualificationsExpiring.ShouldBe(0);
    }

    [Fact]
    public async Task Update_replaces_qualifications_by_id_and_checks_the_stamp()
    {
        using var _ = InTenantA();
        var created = await CreateAsync("D1", "Jan", "Jansen", configure: dto => dto.Qualifications = [Qualification(DriverQualificationKind.DrivingLicenceC, "C-1"), Qualification(DriverQualificationKind.AdrCertificate, "ADR-1")]);
        var licenceId = created.Qualifications.Single(q => q.Kind == DriverQualificationKind.DrivingLicenceC).Id!.Value;

        var update = UpdateFrom(created);
        update.LastName = "de Vries";
        update.Qualifications = [Qualification(DriverQualificationKind.DrivingLicenceCE, "CE-1", until: Today.AddDays(10), id: licenceId), Qualification(DriverQualificationKind.Code95)];
        var updated = await _service.UpdateAsync(created.Id, update);

        updated.FullName.ShouldBe("Jan de Vries");
        updated.ConcurrencyStamp.ShouldNotBe(created.ConcurrencyStamp);
        updated.Qualifications.Count.ShouldBe(2);
        updated.Qualifications.Single(q => q.Id == licenceId).Kind.ShouldBe(DriverQualificationKind.DrivingLicenceCE);
        updated.Qualifications.ShouldNotContain(q => q.Number == "ADR-1");
        (await WithUnitOfWorkAsync(() => _qualificationRows.CountAsync(q => q.DriverId == created.Id))).ShouldBe(2);
        (await _service.GetListAsync(Request())).Rows.ShouldHaveSingleItem().QualificationsExpiring.ShouldBe(1);

        await Should.ThrowAsync<AbpDbConcurrencyException>(() => _service.UpdateAsync(created.Id, UpdateFrom(created)));
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.UpdateAsync(Guid.NewGuid(), UpdateFrom(updated)));
        await ShouldFailAsync(() => CreateAsync("D2", "X", "Y", configure: dto => dto.Qualifications = Enumerable.Range(0, MasterDataConsts.MaxQualificationsPerDriver + 1).Select(_ => Qualification(DriverQualificationKind.Other)).ToList()), MasterDataErrorCodes.TooManyChildren);
    }

    // ── Employer ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_employer_must_exist_in_the_tenant_and_carry_the_carrier_or_own_company_role()
    {
        using var _ = InTenantA();
        var customer = await CreateRelationAsync("CUS", "Customer", RelationRoles.Customer | RelationRoles.Depot);
        var carrier = await CreateRelationAsync("CAR", "Carrier", RelationRoles.Carrier);
        var own = await CreateRelationAsync("OWN", "Own", RelationRoles.OwnCompany);

        await ShouldFailAsync(() => CreateAsync("D1", "Jan", "Jansen", customer.Id), MasterDataErrorCodes.EmployerRoleRequired);
        await ShouldFailAsync(() => CreateAsync("D1", "Jan", "Jansen", Guid.NewGuid()), MasterDataErrorCodes.ReferenceNotFound, "employerRelationId");
        (await _service.GetListAsync(Request())).TotalCount.ShouldBe(0);

        var driver = await CreateAsync("D1", "Jan", "Jansen", carrier.Id);
        driver.EmployerName.ShouldBe("Carrier");
        var toOwn = UpdateFrom(driver);
        toOwn.EmployerRelationId = own.Id;
        (await _service.UpdateAsync(driver.Id, toOwn)).EmployerName.ShouldBe("Own");

        var toCustomer = UpdateFrom(await _service.GetAsync(driver.Id));
        toCustomer.EmployerRelationId = customer.Id;
        await ShouldFailAsync(() => _service.UpdateAsync(driver.Id, toCustomer), MasterDataErrorCodes.EmployerRoleRequired);
        (await _service.GetAsync(driver.Id)).EmployerRelationId.ShouldBe(own.Id, "a refused update changes nothing");

        var toNone = UpdateFrom(await _service.GetAsync(driver.Id));
        toNone.EmployerRelationId = null;
        (await _service.UpdateAsync(driver.Id, toNone)).EmployerRelationId.ShouldBeNull();
    }

    // ── User account ─────────────────────────────────────────────────────────

    [Fact]
    public async Task A_user_must_exist_in_the_tenant_and_may_be_linked_to_one_driver_only()
    {
        using var _ = InTenantA();
        var user = await CreateUserAsync("jan.jansen", "jan@carrier.nl");

        await ShouldFailAsync(() => CreateAsync("D1", "Jan", "Jansen", userId: Guid.NewGuid()), MasterDataErrorCodes.UserNotFound);

        var first = await CreateAsync("D1", "Jan", "Jansen", userId: user.Id);
        var duplicate = await ShouldFailAsync(() => CreateAsync("D2", "Piet", "Pietersen", userId: user.Id), MasterDataErrorCodes.DuplicateKey, "userId");
        duplicate.Data["value"].ShouldBe(user.Id.ToString());

        /* The same user on the same driver is not a conflict; a second driver gets it once the first lets go. */
        var same = UpdateFrom(first);
        same.Mobile = "06-2";
        (await _service.UpdateAsync(first.Id, same)).UserId.ShouldBe(user.Id);
        var second = await CreateAsync("D2", "Piet", "Pietersen");
        var steal = UpdateFrom(second);
        steal.UserId = user.Id;
        await ShouldFailAsync(() => _service.UpdateAsync(second.Id, steal), MasterDataErrorCodes.DuplicateKey, "userId");

        var release = UpdateFrom(await _service.GetAsync(first.Id));
        release.UserId = null;
        await _service.UpdateAsync(first.Id, release);
        steal.ConcurrencyStamp = (await _service.GetAsync(second.Id)).ConcurrencyStamp;
        (await _service.UpdateAsync(second.Id, steal)).UserName.ShouldBe("jan.jansen");
    }

    [Fact]
    public async Task The_user_lookup_page_lists_the_tenants_users_and_filters_on_a_term()
    {
        using var _ = InTenantA();
        await CreateUserAsync("jan.jansen", "jan@carrier.nl");
        await CreateUserAsync("piet.pietersen", "piet@carrier.nl");

        var all = await _service.GetUserLookupPageAsync();
        all.TotalCount.ShouldBe(2);
        all.Items.Select(u => u.UserName).ShouldBe(["jan.jansen", "piet.pietersen"]);
        all.Items[0].Email.ShouldBe("jan@carrier.nl");

        var filtered = await _service.GetUserLookupPageAsync("piet");
        filtered.TotalCount.ShouldBe(1);
        filtered.Items.ShouldHaveSingleItem().UserName.ShouldBe("piet.pietersen");
        (await _service.GetUserLookupPageAsync(skip: 1, take: 1)).Items.ShouldHaveSingleItem().UserName.ShouldBe("piet.pietersen");

        using (InTenantB())
        {
            (await GetRequiredService<IDriverAppService>().GetUserLookupPageAsync()).TotalCount.ShouldBe(0);
        }
    }

    // ── Code ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_duplicate_code_is_refused_and_freed_by_delete()
    {
        using var _ = InTenantA();
        var first = await CreateAsync("D1", "Jan", "Jansen");
        var second = await CreateAsync("D2", "Piet", "Pietersen");

        (await ShouldFailAsync(() => CreateAsync(" d1 ", "X", "Y"), MasterDataErrorCodes.DuplicateKey, "code")).Data["value"].ShouldBe("D1");
        var clash = UpdateFrom(second);
        clash.Code = "D1";
        await ShouldFailAsync(() => _service.UpdateAsync(second.Id, clash), MasterDataErrorCodes.DuplicateKey, "code");
        await Should.ThrowAsync<AbpValidationException>(() => CreateAsync("  ", "X", "Y"));

        await _service.DeleteAsync(first.Id);
        (await CreateAsync("D1", "Jan", "Jansen")).Id.ShouldNotBe(first.Id);
    }

    // ── Anonymise (GDPR art. 17, Critical) ───────────────────────────────────

    [Fact]
    public async Task Anonymise_removes_the_person_frees_the_user_keeps_id_and_code_and_makes_the_record_read_only()
    {
        using var _ = InTenantA();
        var carrier = await CreateRelationAsync("CAR", "Carrier", RelationRoles.Carrier);
        var user = await CreateUserAsync("jan.jansen", "jan@carrier.nl");
        var driver = await CreateAsync("D1", "Jan", "Jansen", carrier.Id, user.Id, dto =>
        {
            dto.MiddleName = "van";
            dto.Initials = "J.";
            dto.Phone = "020";
            dto.Mobile = "06";
            dto.Email = "jan@carrier.nl";
            dto.PreferredCulture = "nl";
            dto.Description = "remark";
            dto.Qualifications = [Qualification(DriverQualificationKind.Code95, "95-1", until: Today.AddDays(10))];
        });

        await _service.AnonymiseAsync(driver.Id);

        var anonymised = await _service.GetAsync(driver.Id);
        anonymised.Id.ShouldBe(driver.Id);
        anonymised.Code.ShouldBe("D1");
        anonymised.FirstName.ShouldBe(MasterDataConsts.AnonymisedText);
        anonymised.LastName.ShouldBe(MasterDataConsts.AnonymisedText);
        anonymised.MiddleName.ShouldBeNull();
        anonymised.Initials.ShouldBeNull();
        anonymised.Phone.ShouldBeNull();
        anonymised.Mobile.ShouldBeNull();
        anonymised.Email.ShouldBeNull();
        anonymised.PreferredCulture.ShouldBeNull();
        anonymised.Description.ShouldBeNull();
        anonymised.UserId.ShouldBeNull();
        anonymised.UserName.ShouldBeNull();
        anonymised.Qualifications.ShouldBeEmpty();
        anonymised.IsAnonymised.ShouldBeTrue();
        anonymised.IsArchived.ShouldBeTrue();
        anonymised.EmployerRelationId.ShouldBe(carrier.Id);
        (await WithUnitOfWorkAsync(() => _qualificationRows.CountAsync(q => q.DriverId == driver.Id))).ShouldBe(0, "the qualification rows are deleted, not only detached");

        /* Gone from the default list and the lookup; visible as anonymised when archived rows are shown. */
        (await _service.GetListAsync(Request())).TotalCount.ShouldBe(0);
        (await _service.GetLookupAsync()).ShouldBeEmpty();
        (await _service.GetListAsync(Request(search: "jansen", includeArchived: true))).Rows.ShouldBeEmpty("the search key no longer carries the name");
        var row = (await _service.GetListAsync(Request(includeArchived: true))).Rows.ShouldHaveSingleItem();
        row.IsAnonymised.ShouldBeTrue();
        row.FullName.ShouldBe("Anonymised Anonymised");
        row.HasUser.ShouldBeFalse();
        row.QualificationsExpiring.ShouldBe(0);

        /* The art. 15 selector no longer finds the person. */
        (await _personalData.FindAsync("jan@carrier.nl", user.Id)).ShouldBeEmpty();

        /* Read-only afterwards. */
        var update = UpdateFrom(anonymised);
        update.FirstName = "Jan";
        await ShouldFailAsync(() => _service.UpdateAsync(driver.Id, update), MasterDataErrorCodes.AnonymisedRecordIsReadOnly);
        (await _service.GetAsync(driver.Id)).FirstName.ShouldBe(MasterDataConsts.AnonymisedText);

        /* The user account is free for another driver. */
        (await CreateAsync("D2", "Piet", "Pietersen", userId: user.Id)).UserName.ShouldBe("jan.jansen");

        /* The employer relation stays referenced (trip history), so it still cannot be deleted. */
        await ShouldFailAsync(() => _relations.DeleteAsync(carrier.Id), MasterDataErrorCodes.InUse);
    }

    [Fact]
    public async Task Anonymise_twice_is_one_effect_and_needs_an_existing_driver()
    {
        using var _ = InTenantA();
        var driver = await CreateAsync("D1", "Jan", "Jansen", configure: dto => dto.Email = "jan@carrier.nl");

        await _service.AnonymiseAsync(driver.Id);
        var first = await _service.GetAsync(driver.Id);
        await _service.AnonymiseAsync(driver.Id);
        var second = await _service.GetAsync(driver.Id);

        second.FirstName.ShouldBe(first.FirstName);
        second.LastName.ShouldBe(first.LastName);
        second.IsAnonymised.ShouldBeTrue();
        second.Email.ShouldBeNull();
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.AnonymiseAsync(Guid.NewGuid()));

        /* An anonymised driver can still be deleted (code freed) and bulk-deleted. */
        await _service.DeleteAsync(driver.Id);
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(driver.Id));
    }

    // ── Archive, list, lookup ────────────────────────────────────────────────

    [Fact]
    public async Task List_filters_on_archived_and_employer_searches_name_and_email_and_sorts_on_the_whitelist()
    {
        using var _ = InTenantA();
        var carrier = await CreateRelationAsync("CAR", "Carrier", RelationRoles.Carrier);
        var own = await CreateRelationAsync("OWN", "Own", RelationRoles.OwnCompany);
        var jan = await CreateAsync("D3", "Jan", "Jansen", carrier.Id, configure: dto => dto.Email = "jan@carrier.nl");
        await CreateAsync("D1", "Zoe", "Bakker", own.Id, configure: dto => dto.Mobile = "06-1");
        await CreateAsync("D2", "Piet", "Vries", configure: dto => dto.Email = "piet@own.nl");

        var list = await _service.GetListAsync(Request());
        list.TotalCount.ShouldBe(3);
        list.Rows.Select(r => r.Code).ShouldBe(["D1", "D3", "D2"], "by last name, then first name");

        (await _service.GetListAsync(Request(search: "bakker"))).Rows.ShouldHaveSingleItem().Code.ShouldBe("D1");
        (await _service.GetListAsync(Request(search: "own.nl"))).Rows.ShouldHaveSingleItem().Code.ShouldBe("D2");
        (await _service.GetListAsync(Request(search: "d3"))).Rows.ShouldHaveSingleItem().Code.ShouldBe("D3");
        (await _service.GetListAsync(Request(employerId: carrier.Id))).Rows.ShouldHaveSingleItem().Code.ShouldBe("D3");
        (await _service.GetListAsync(Request(employerId: Guid.NewGuid()))).TotalCount.ShouldBe(0);

        (await _service.GetListAsync(Request(sortBy: "code", sortDir: "desc"))).Rows.Select(r => r.Code).ShouldBe(["D3", "D2", "D1"]);
        (await _service.GetListAsync(Request(sortBy: "firstName"))).Rows.Select(r => r.FirstName).ShouldBe(["Jan", "Piet", "Zoe"]);
        (await _service.GetListAsync(Request(sortBy: "fullName", sortDir: "desc"))).Rows.First().Code.ShouldBe("D2");
        (await _service.GetListAsync(Request(sortBy: "mobile", sortDir: "desc"))).Rows.First().Code.ShouldBe("D1");
        (await _service.GetListAsync(Request(sortBy: "email"))).Rows.Last().Code.ShouldBe("D2");
        await ShouldFailAsync(() => _service.GetListAsync(Request(sortBy: "employerName")), MasterDataErrorCodes.UnknownSortColumn);

        await _service.ArchiveAsync(jan.Id);
        (await _service.GetAsync(jan.Id)).IsArchived.ShouldBeTrue();
        (await _service.GetListAsync(Request())).Rows.Select(r => r.Code).ShouldBe(["D1", "D2"]);
        (await _service.GetListAsync(Request(includeArchived: true, sortBy: "isArchived", sortDir: "desc"))).Rows.First().Code.ShouldBe("D3");
        (await _service.GetLookupAsync()).Select(l => l.Code).ShouldBe(["D1", "D2"]);
        (await _service.GetLookupAsync("jans")).ShouldBeEmpty();
        await _service.UnarchiveAsync(jan.Id);
        (await _service.GetLookupAsync("jans")).ShouldHaveSingleItem().Code.ShouldBe("D3");

        var page = await _service.GetListAsync(Request(start: 2, end: 3));
        page.Rows.ShouldHaveSingleItem().Code.ShouldBe("D2");
        page.TotalCount.ShouldBe(3);
    }

    [Fact]
    public async Task Search_folds_diacritics_and_case_on_both_sides()
    {
        using var _ = InTenantA();
        await CreateAsync("D1", "Ängel", "Åberg");
        await CreateAsync("D2", "Jan", "Jansen");

        (await _service.GetListAsync(Request(search: "angel"))).Rows.ShouldHaveSingleItem().Code.ShouldBe("D1");
        (await _service.GetListAsync(Request(search: "ÅBERG"))).Rows.ShouldHaveSingleItem().Code.ShouldBe("D1");
        (await _service.GetListAsync(Request(search: "aberg"))).Rows.ShouldHaveSingleItem().Code.ShouldBe("D1");
        (await _service.GetLookupAsync("ängel")).ShouldHaveSingleItem().Name.ShouldBe("Ängel Åberg");
        (await _service.GetListAsync(Request(search: "nobody"))).Rows.ShouldBeEmpty();
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_and_bulk_delete_remove_the_qualifications_and_a_second_bulk_delete_removes_nothing()
    {
        using var _ = InTenantA();
        var carrier = await CreateRelationAsync("CAR", "Carrier", RelationRoles.Carrier);
        var a = await CreateAsync("D1", "A", "A", carrier.Id, configure: dto => dto.Qualifications = [Qualification(DriverQualificationKind.Code95)]);
        var b = await CreateAsync("D2", "B", "B", carrier.Id, configure: dto => dto.Qualifications = [Qualification(DriverQualificationKind.Code95)]);
        var c = await CreateAsync("D3", "C", "C", configure: dto => dto.IsArchived = true);
        var d = await CreateAsync("D4", "D", "D");

        await _service.DeleteAsync(a.Id);
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.DeleteAsync(a.Id));
        (await WithUnitOfWorkAsync(() => _qualificationRows.CountAsync(q => q.DriverId == a.Id))).ShouldBe(0);

        var request = new DriverBulkDeleteRequestDto { Selection = Explicit(b.Id, d.Id, Guid.NewGuid()) };
        var first = await _service.BulkDeleteAsync(request);
        first.DeletedCount.ShouldBe(2);
        first.SkippedRows.ShouldBeEmpty();
        (await _service.BulkDeleteAsync(request)).DeletedCount.ShouldBe(0);
        (await WithUnitOfWorkAsync(() => _qualificationRows.GetCountAsync())).ShouldBe(0);

        /* Filter-based honours the archived flag and the employer filter. */
        (await _service.BulkDeleteAsync(new DriverBulkDeleteRequestDto { Selection = FilterBased() })).DeletedCount.ShouldBe(0, "the remaining driver is archived");
        (await _service.BulkDeleteAsync(new DriverBulkDeleteRequestDto { Selection = FilterBased(), IncludeArchived = true, EmployerRelationId = carrier.Id })).DeletedCount.ShouldBe(0);
        (await _service.BulkDeleteAsync(new DriverBulkDeleteRequestDto { Selection = FilterBased(), IncludeArchived = true })).DeletedCount.ShouldBe(1);
        (await _service.GetListAsync(Request(includeArchived: true))).TotalCount.ShouldBe(0);
        c.Id.ShouldNotBe(Guid.Empty);

        /* With no driver left, the carrier can go. */
        await _relations.DeleteAsync(carrier.Id);
    }

    // ── Personal data (GDPR art. 15, Critical) ───────────────────────────────

    [Fact]
    public async Task PersonalDataSource_finds_contact_persons_and_drivers_by_email_or_user_id_and_nothing_for_strangers()
    {
        using var _ = InTenantA();
        var user = await CreateUserAsync("jan.jansen", "jan@carrier.nl");
        var acme = await _relations.CreateAsync(new RelationCreateDto
        {
            Code = "ACM", Name = "ACME", Roles = RelationRoles.Customer | RelationRoles.Carrier,
            ContactPersons = [new RelationContactPersonDto { FirstName = "Jan", LastName = "Jansen", Email = "Jan@ACME.nl", Role = ContactPersonRole.Sales, Phone = "020" }, new RelationContactPersonDto { FirstName = "Piet", LastName = "Pietersen", Email = "piet@acme.nl" }]
        });
        var driver = await CreateAsync("D1", "Jan", "Jansen", acme.Id, user.Id, dto =>
        {
            dto.Email = "jan@acme.nl";
            dto.Mobile = "06";
            dto.Qualifications = [Qualification(DriverQualificationKind.Code95, "95-1", new DateOnly(2024, 1, 1), new DateOnly(2029, 1, 1))];
        });
        await CreateAsync("D2", "Kees", "Klaassen", configure: dto => dto.Email = "kees@acme.nl");

        var byEmail = await _personalData.FindAsync(" JAN@acme.NL ", null);
        byEmail.Count.ShouldBe(2);
        var contact = byEmail.Single(r => r.Kind == "RelationContactPerson");
        contact.RelationId.ShouldBe(acme.Id);
        contact.Id.ShouldBe(acme.ContactPersons.Single(c => c.FirstName == "Jan").Id!.Value);
        contact.Fields["firstName"].ShouldBe("Jan");
        contact.Fields["lastName"].ShouldBe("Jansen");
        contact.Fields["email"].ShouldBe("Jan@ACME.nl");
        contact.Fields["role"].ShouldBe("Sales");
        contact.Fields["phone"].ShouldBe("020");
        var found = byEmail.Single(r => r.Kind == "Driver");
        found.Id.ShouldBe(driver.Id);
        found.RelationId.ShouldBe(acme.Id);
        found.Fields["code"].ShouldBe("D1");
        found.Fields["email"].ShouldBe("jan@acme.nl");
        found.Fields["mobile"].ShouldBe("06");
        found.Fields["userId"].ShouldBe(user.Id.ToString());
        found.Fields["employerRelationId"].ShouldBe(acme.Id.ToString());
        found.Fields["qualification1"].ShouldBe("Code95 95-1 2024-01-01..2029-01-01");

        var byUser = await _personalData.FindAsync(null, user.Id);
        byUser.ShouldHaveSingleItem().Id.ShouldBe(driver.Id);
        (await _personalData.FindAsync("jan@acme.nl", user.Id)).Count.ShouldBe(2, "the same driver is reported once");
        (await _personalData.FindAsync("piet@acme.nl", null)).ShouldHaveSingleItem().Kind.ShouldBe("RelationContactPerson");
        (await _personalData.FindAsync("nobody@acme.nl", null)).ShouldBeEmpty();
        (await _personalData.FindAsync(null, Guid.NewGuid())).ShouldBeEmpty();
        (await _personalData.FindAsync("", null)).ShouldBeEmpty();
        (await _personalData.FindAsync(null, null)).ShouldBeEmpty();
    }

    // ── Tenant isolation (Critical) ───────────────────────────────────────────

    [Fact]
    public async Task Rows_of_tenant_A_are_invisible_and_untouchable_from_tenant_B()
    {
        DriverDto driver;
        RelationDto carrier;
        IdentityUser user;
        using (InTenantA())
        {
            carrier = await CreateRelationAsync("CAR", "Carrier", RelationRoles.Carrier);
            user = await CreateUserAsync("jan.jansen", "jan@carrier.nl");
            driver = await CreateAsync("D1", "Jan", "Jansen", carrier.Id, user.Id, dto => dto.Email = "jan@carrier.nl");
        }

        using (InTenantB())
        {
            var service = GetRequiredService<IDriverAppService>();
            var personalData = GetRequiredService<IPersonalDataSource>();

            (await service.GetListAsync(Request(includeArchived: true))).TotalCount.ShouldBe(0);
            (await service.GetListAsync(Request(search: "jansen"))).Rows.ShouldBeEmpty();
            (await service.GetListAsync(Request(employerId: carrier.Id))).Rows.ShouldBeEmpty();
            (await service.GetLookupAsync()).ShouldBeEmpty();
            await Should.ThrowAsync<EntityNotFoundException>(() => service.GetAsync(driver.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => service.UpdateAsync(driver.Id, UpdateFrom(driver)));
            await Should.ThrowAsync<EntityNotFoundException>(() => service.ArchiveAsync(driver.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => service.UnarchiveAsync(driver.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => service.AnonymiseAsync(driver.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => service.DeleteAsync(driver.Id));
            (await service.BulkDeleteAsync(new DriverBulkDeleteRequestDto { Selection = Explicit(driver.Id) })).DeletedCount.ShouldBe(0);
            (await service.BulkDeleteAsync(new DriverBulkDeleteRequestDto { Selection = FilterBased(), IncludeArchived = true })).DeletedCount.ShouldBe(0);
            (await personalData.FindAsync("jan@carrier.nl", user.Id)).ShouldBeEmpty();

            /* Tenant A's relation and user are not tenant B's. */
            await ShouldFailAsync(() => service.CreateAsync(Dto("D2", "X", "Y", employerId: carrier.Id)), MasterDataErrorCodes.ReferenceNotFound, "employerRelationId");
            await ShouldFailAsync(() => service.CreateAsync(Dto("D2", "X", "Y", userId: user.Id)), MasterDataErrorCodes.UserNotFound);

            /* Tenant B has its own key space. */
            var own = await service.CreateAsync(Dto("D1", "Jan", "Jansen"));
            own.Id.ShouldNotBe(driver.Id);
            (await service.GetListAsync(Request())).Rows.ShouldHaveSingleItem().Id.ShouldBe(own.Id);
        }

        using (InTenantA())
        {
            (await _service.GetListAsync(Request())).Rows.ShouldHaveSingleItem().Id.ShouldBe(driver.Id);
            (await _service.GetAsync(driver.Id)).IsAnonymised.ShouldBeFalse();
            (await _personalData.FindAsync(null, user.Id)).ShouldHaveSingleItem();
        }
    }

    // ── Feature flag (Critical) ───────────────────────────────────────────────

    [Fact]
    public async Task With_the_flag_off_every_driver_service_method_is_refused_before_touching_data()
    {
        using var _ = InTenantA();
        var driver = await CreateAsync("D1", "Jan", "Jansen");

        using (TestFeatureValueProvider.Override(AppFeatures.MasterData.Default, "false"))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetListAsync(Request()));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetAsync(driver.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetLookupAsync());
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetUserLookupPageAsync());
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.CreateAsync(Dto("D2", "X", "Y")));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.UpdateAsync(driver.Id, UpdateFrom(driver)));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.DeleteAsync(driver.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.BulkDeleteAsync(new DriverBulkDeleteRequestDto { Selection = Explicit(driver.Id) }));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.ArchiveAsync(driver.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.UnarchiveAsync(driver.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.AnonymiseAsync(driver.Id));
        }

        var intact = await _service.GetAsync(driver.Id);
        intact.FirstName.ShouldBe("Jan");
        intact.IsAnonymised.ShouldBeFalse();
        intact.IsArchived.ShouldBeFalse();
    }
}
