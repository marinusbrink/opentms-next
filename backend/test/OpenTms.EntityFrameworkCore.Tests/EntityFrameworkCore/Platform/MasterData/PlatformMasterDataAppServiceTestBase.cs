using System;
using System.Threading.Tasks;
using OpenTms.Platform;
using OpenTms.Platform.Grid;
using Shouldly;
using Volo.Abp;
using Volo.Abp.MultiTenancy;

namespace OpenTms.EntityFrameworkCore.Platform.MasterData;

/// <summary>
/// Shared plumbing for the Platform master-data application-service tests on SQLite (design 49): two tenants (tests run
/// inside tenant A; tenant B is the other tenant), list and selection builders and the business-error assertion. Every
/// test gets its own in-memory database (one ABP application per test). The SQLite module runs without transactions, so
/// INumberGenerator refuses to run here (design 49, assumption 8); the generator is covered on PostgreSQL.
/// </summary>
public abstract class PlatformMasterDataAppServiceTestBase : OpenTmsEntityFrameworkCoreTestBase
{
    protected ICurrentTenant CurrentTenant { get; }

    protected Guid TenantA { get; } = Guid.NewGuid();

    protected Guid TenantB { get; } = Guid.NewGuid();

    protected PlatformMasterDataAppServiceTestBase()
    {
        CurrentTenant = GetRequiredService<ICurrentTenant>();
    }

    protected IDisposable InTenantA() => CurrentTenant.Change(TenantA);

    protected IDisposable InTenantB() => CurrentTenant.Change(TenantB);

    protected static CultureScope UiCulture(string culture) => new(culture);

    protected static TRequest List<TRequest>(int start = 0, int end = 100, string? search = null, string? sortBy = null, string? sortDir = null)
        where TRequest : PlatformMasterDataListRequestDto, new()
        => new() { StartRow = start, EndRow = end, WildcardSearch = search, SortBy = sortBy, SortDir = sortDir };

    protected static GridSelectionDto Explicit(params Guid[] ids) => new() { Mode = "Explicit", ExplicitIds = [.. ids] };

    protected static GridSelectionDto FilterBased(string? search = null, params Guid[] excludedIds) => new()
    {
        Mode = "FilterBased",
        FilterRequest = new GridRequest { StartRow = 0, EndRow = 100, WildcardSearch = search },
        ExcludedIds = [.. excludedIds]
    };

    protected static async Task<BusinessException> ShouldFailAsync(Func<Task> action, string code)
    {
        var ex = await Should.ThrowAsync<BusinessException>(action);
        ex.Code.ShouldBe(code, ex.Message);
        return ex;
    }

    protected static PlatformLocalizedNameDto Names(string? en, string? nl = null)
    {
        var names = new PlatformLocalizedNameDto();
        if (en != null)
        {
            names["en"] = en;
        }

        if (nl != null)
        {
            names["nl"] = nl;
        }

        return names;
    }
}
