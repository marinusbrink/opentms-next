using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTms.MasterData.Features;
using OpenTms.MasterData.Permissions;
using OpenTms.MasterData.Relations;
using OpenTms.Platform.Grid;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;
using Volo.Abp.Identity;
using Volo.Abp.Localization;

namespace OpenTms.MasterData.Drivers;

[RequiresFeature(MasterDataFeatures.Relations)]
public class DriverAppService
    : MasterDataCrudAppServiceBase<Driver, DriverRowDto, DriverDto, DriverCreateDto, DriverUpdateDto, DriverListRequestDto>,
      IDriverAppService
{
    private const int ExpiryWindowDays = 90;

    private readonly IRepository<Relation, Guid> _relations;
    private readonly IRepository<DriverQualification, Guid> _qualifications;
    private readonly IIdentityUserLookupAppService _users;
    private readonly IReadOnlyCollection<string> _cultures;

    public DriverAppService(
        IRepository<Driver, Guid> repository,
        IRepository<Relation, Guid> relations,
        IRepository<DriverQualification, Guid> qualifications,
        IIdentityUserLookupAppService users,
        IOptions<AbpLocalizationOptions> localization)
        : base(repository)
    {
        _relations = relations;
        _qualifications = qualifications;
        _users = users;
        _cultures = localization.Value.Languages.Select(l => l.CultureName.ToLowerInvariant()).ToList();
    }

    [Authorize(MasterDataPermissions.Drivers.Default)]
    public Task<GridResponse<DriverRowDto>> GetListAsync(DriverListRequestDto input) => GetListInternalAsync(input);

    [Authorize(MasterDataPermissions.Drivers.Default)]
    public Task<DriverDto> GetAsync(Guid id) => GetInternalAsync(id);

    [Authorize(MasterDataPermissions.Drivers.Default)]
    public Task<List<MasterDataLookupDto>> GetLookupAsync(string? term = null, int take = MasterDataConsts.MaxLookupTake) => GetLookupInternalAsync(term, take);

    /// <summary>
    /// The tenant's users for the account picker, read through ABP's published <see cref="IIdentityUserLookupAppService"/>
    /// (design 43, cross-module references): the caller also needs its <c>AbpIdentity.UserLookup</c> permission.
    /// Behind the Update permission because it is only used to link one.
    /// </summary>
    [Authorize(MasterDataPermissions.Drivers.Update)]
    public async Task<DriverUserLookupPageDto> GetUserLookupPageAsync(string? term = null, int skip = 0, int take = MasterDataConsts.LookupPageSize)
    {
        var filter = string.IsNullOrWhiteSpace(term) ? null : term.Trim();
        var page = await _users.SearchAsync(new UserLookupSearchInputDto
        {
            Filter = filter,
            Sorting = "UserName",
            SkipCount = Math.Max(0, skip),
            MaxResultCount = Math.Clamp(take, 1, MasterDataConsts.LookupPageSize)
        });
        var total = await _users.GetCountAsync(new UserLookupCountInputDto { Filter = filter });
        return new DriverUserLookupPageDto
        {
            TotalCount = total,
            Items = page.Items.Select(u => new DriverUserLookupDto { Id = u.Id, UserName = u.UserName, Name = string.Join(" ", new[] { u.Name, u.Surname }.Where(p => !string.IsNullOrEmpty(p))), Email = u.Email }).ToList()
        };
    }

    [Authorize(MasterDataPermissions.Drivers.Create)]
    public Task<DriverDto> CreateAsync(DriverCreateDto input) => CreateInternalAsync(input);

    [Authorize(MasterDataPermissions.Drivers.Update)]
    public Task<DriverDto> UpdateAsync(Guid id, DriverUpdateDto input) => UpdateInternalAsync(id, input);

    [Authorize(MasterDataPermissions.Drivers.Delete)]
    public Task DeleteAsync(Guid id) => DeleteInternalAsync(id);

    [Authorize(MasterDataPermissions.Drivers.Delete)]
    public Task<MasterDataBulkDeleteResultDto> BulkDeleteAsync(DriverBulkDeleteRequestDto input)
        => BulkDeleteInternalAsync(input.Selection, q => ApplyFilters(q, input.IncludeArchived, input.EmployerRelationId));

    [Authorize(MasterDataPermissions.Drivers.Update)]
    public async Task ArchiveAsync(Guid id)
    {
        var driver = await Repository.GetAsync(id);
        driver.Archive();
        await Repository.UpdateAsync(driver, autoSave: true);
    }

    [Authorize(MasterDataPermissions.Drivers.Update)]
    public async Task UnarchiveAsync(Guid id)
    {
        var driver = await Repository.GetAsync(id);
        driver.Unarchive();
        await Repository.UpdateAsync(driver, autoSave: true);
    }

    [Authorize(MasterDataPermissions.Drivers.Anonymise)]
    public async Task AnonymiseAsync(Guid id)
    {
        var driver = await Repository.GetAsync(id, includeDetails: true);
        driver.Anonymise();
        await Repository.UpdateAsync(driver, autoSave: true);
        Logger.LogInformation("Driver {DriverId} anonymised (GDPR art. 17).", id);
    }

    // ── hooks ─────────────────────────────────────────────────────────────────

    protected override IQueryable<Driver> ApplyFilters(IQueryable<Driver> query, DriverListRequestDto request)
        => ApplyFilters(query, request.IncludeArchived, request.EmployerRelationId);

    private static IQueryable<Driver> ApplyFilters(IQueryable<Driver> query, bool includeArchived, Guid? employerRelationId)
    {
        if (!includeArchived)
        {
            query = query.Where(d => !d.IsArchived);
        }

        if (employerRelationId != null)
        {
            query = query.Where(d => d.EmployerRelationId == employerRelationId.Value);
        }

        return query;
    }

    protected override IQueryable<Driver> ApplySearch(IQueryable<Driver> query, string term)
    {
        var key = MasterDataValues.SearchKey(term);
        return query.Where(d => d.SearchKey.Contains(key) || (d.Email != null && d.Email.ToLower().Contains(term)));
    }

    protected override IOrderedQueryable<Driver> ApplySort(IQueryable<Driver> query, string? sortBy, bool descending)
        => (sortBy?.ToLowerInvariant()) switch
        {
            null or "" or "fullname" or "lastname" => descending ? query.OrderByDescending(d => d.LastName).ThenByDescending(d => d.FirstName) : query.OrderBy(d => d.LastName).ThenBy(d => d.FirstName),
            "firstname" => Order(query, d => d.FirstName, descending),
            "code" => Order(query, d => d.Code, descending),
            "mobile" => Order(query, d => d.Mobile, descending),
            "email" => Order(query, d => d.Email, descending),
            "isarchived" => Order(query, d => d.IsArchived, descending),
            _ => throw UnknownSortColumn(sortBy)
        };

    protected override IQueryable<Driver> ApplyLookupFilter(IQueryable<Driver> query) => query.Where(d => !d.IsArchived);

    protected override async Task<List<DriverRowDto>> ToRowsAsync(List<Driver> entities)
    {
        var ids = entities.Select(d => d.Id).ToList();
        var employerIds = entities.Where(d => d.EmployerRelationId != null).Select(d => d.EmployerRelationId!.Value).Distinct().ToList();
        var employers = employerIds.Count == 0 ? new Dictionary<Guid, string>() : (await _relations.GetListAsync(r => employerIds.Contains(r.Id))).ToDictionary(r => r.Id, r => r.Name);
        var limit = DateOnly.FromDateTime(Clock.Now).AddDays(ExpiryWindowDays);
        var expiring = ids.Count == 0
            ? new Dictionary<Guid, int>()
            : (await _qualifications.GetListAsync(q => ids.Contains(q.DriverId) && q.ValidUntil != null && q.ValidUntil <= limit))
                .GroupBy(q => q.DriverId)
                .ToDictionary(g => g.Key, g => g.Count());

        return entities.Select(d => new DriverRowDto
        {
            Id = d.Id,
            Code = d.Code,
            FirstName = d.FirstName,
            LastName = d.LastName,
            FullName = d.FullName,
            EmployerRelationId = d.EmployerRelationId,
            EmployerName = d.EmployerRelationId == null ? null : employers.GetValueOrDefault(d.EmployerRelationId.Value),
            Mobile = d.Mobile,
            Email = d.Email,
            IsArchived = d.IsArchived,
            IsAnonymised = d.IsAnonymised,
            HasUser = d.UserId != null,
            QualificationsExpiring = expiring.GetValueOrDefault(d.Id)
        }).ToList();
    }

    protected override async Task<DriverDto> ToDtoAsync(Driver d)
    {
        var employer = d.EmployerRelationId == null ? null : await _relations.FindAsync(d.EmployerRelationId.Value);
        var user = d.UserId == null ? null : await _users.FindByIdAsync(d.UserId.Value);
        var dto = new DriverDto
        {
            Code = d.Code,
            FirstName = d.FirstName,
            MiddleName = d.MiddleName,
            LastName = d.LastName,
            Initials = d.Initials,
            FullName = d.FullName,
            Phone = d.Phone,
            Mobile = d.Mobile,
            Email = d.Email,
            PreferredCulture = d.PreferredCulture,
            EmployerRelationId = d.EmployerRelationId,
            EmployerName = employer?.Name,
            UserId = d.UserId,
            UserName = user?.UserName,
            IsArchived = d.IsArchived,
            IsAnonymised = d.IsAnonymised,
            Qualifications = d.Qualifications.OrderBy(q => q.Kind).ThenBy(q => q.ValidUntil).Select(q => new DriverQualificationDto
            {
                Id = q.Id,
                Kind = q.Kind,
                Number = q.Number,
                ValidFrom = q.ValidFrom,
                ValidUntil = q.ValidUntil,
                Description = q.Description
            }).ToList()
        };
        MapAudit(d, dto);
        return dto;
    }

    protected override MasterDataLookupDto ToLookup(Driver d) => new() { Id = d.Id, Name = d.FullName, Code = d.Code };

    protected override string DisplayNameOf(Driver d) => d.FullName;

    protected override async Task<Driver> CreateEntityAsync(DriverCreateDto input)
    {
        var code = MasterDataCodes.NormalizeRequired(input.Code, MasterDataConsts.MaxCodeLength, "code");
        await EnsureUniqueAsync(d => d.Code == code, "code", code);
        var driver = new Driver(GuidGenerator.Create(), CurrentTenant.Id, code, input.FirstName, input.LastName);
        await ApplyAsync(driver, input);
        return driver;
    }

    protected override async Task ApplyUpdateAsync(Driver driver, DriverUpdateDto input)
    {
        var code = MasterDataCodes.NormalizeRequired(input.Code, MasterDataConsts.MaxCodeLength, "code");
        if (code != driver.Code)
        {
            await EnsureUniqueAsync(d => d.Code == code && d.Id != driver.Id, "code", code);
            driver.SetCode(code);
        }

        await ApplyAsync(driver, input);
    }

    private async Task ApplyAsync(Driver driver, DriverCreateDto input)
    {
        driver.SetPerson(input.FirstName, input.MiddleName, input.LastName, input.Initials);
        driver.SetContact(input.Phone, input.Mobile, input.Email);
        driver.SetPreferredCulture(input.PreferredCulture, _cultures);
        driver.SetDescription(input.Description);

        if (input.EmployerRelationId != null)
        {
            var employer = await _relations.FindAsync(input.EmployerRelationId.Value);
            if (employer == null)
            {
                throw new BusinessException(MasterDataErrorCodes.ReferenceNotFound).WithData("field", "employerRelationId");
            }

            if (!employer.HasRole(RelationRoles.Carrier) && !employer.HasRole(RelationRoles.OwnCompany))
            {
                throw new BusinessException(MasterDataErrorCodes.EmployerRoleRequired);
            }
        }

        driver.SetEmployer(input.EmployerRelationId);

        if (input.UserId != null)
        {
            if (await _users.FindByIdAsync(input.UserId.Value) == null)
            {
                throw new BusinessException(MasterDataErrorCodes.UserNotFound);
            }

            var userId = input.UserId.Value;
            await EnsureUniqueAsync(d => d.UserId == userId && d.Id != driver.Id, "userId", userId.ToString());
        }

        driver.SetUser(input.UserId);
        if (input.IsArchived)
        {
            driver.Archive();
        }
        else
        {
            driver.Unarchive();
        }

        driver.SetQualifications(
            input.Qualifications.Select(q => new DriverQualificationInput(q.Id, q.Kind, q.Number, q.ValidFrom, q.ValidUntil, q.Description)),
            () => GuidGenerator.Create());
    }

    protected override Task BeforeDeleteAsync(Driver driver)
    {
        if (!driver.IsAnonymised)
        {
            driver.SetQualifications([], () => GuidGenerator.Create());
        }

        return Task.CompletedTask;
    }
}
