using System;
using System.Collections.Generic;
using OpenTms.MasterData;
using OpenTms.Platform.Grid;
using Volo.Abp.MultiTenancy;

namespace OpenTms.EntityFrameworkCore.MasterData;

/// <summary>
/// Shared plumbing for the Master Data application-service tests on SQLite: two tenants (every test
/// runs inside tenant A; tenant B is the "other tenant" of the isolation tests), DTO builders and a
/// culture scope. Every test class gets its own in-memory database (one ABP application per test).
/// </summary>
public abstract class MasterDataAppServiceTestBase : OpenTmsEntityFrameworkCoreTestBase
{
    protected ICurrentTenant CurrentTenant { get; }

    protected Guid TenantA { get; } = Guid.NewGuid();

    protected Guid TenantB { get; } = Guid.NewGuid();

    protected MasterDataAppServiceTestBase()
    {
        CurrentTenant = GetRequiredService<ICurrentTenant>();
    }

    protected IDisposable InTenantA() => CurrentTenant.Change(TenantA);

    protected IDisposable InTenantB() => CurrentTenant.Change(TenantB);

    protected static CultureScope UiCulture(string culture) => new(culture);

    protected static LocalizedNameDto Names(string en, string? nl = null)
    {
        var names = new LocalizedNameDto { ["en"] = en };
        if (nl != null)
        {
            names["nl"] = nl;
        }

        return names;
    }

    protected static LocalizedNameDto Copy(LocalizedNameDto names)
    {
        var copy = new LocalizedNameDto();
        foreach (var (culture, text) in names)
        {
            copy[culture] = text;
        }

        return copy;
    }

    protected static TRequest List<TRequest>(int start = 0, int end = 100, string? search = null, string? sortBy = null, string? sortDir = null)
        where TRequest : MasterDataListRequestDto, new()
        => new() { StartRow = start, EndRow = end, WildcardSearch = search, SortBy = sortBy, SortDir = sortDir };

    protected static MasterDataListRequestDto List(int start = 0, int end = 100, string? search = null, string? sortBy = null, string? sortDir = null)
        => List<MasterDataListRequestDto>(start, end, search, sortBy, sortDir);

    protected static GridSelectionDto Explicit(params Guid[] ids) => new() { Mode = "Explicit", ExplicitIds = [.. ids] };

    protected static GridSelectionDto FilterBased(string? search = null, params Guid[] excludedIds) => new()
    {
        Mode = "FilterBased",
        FilterRequest = new GridRequest { StartRow = 0, EndRow = 100, WildcardSearch = search },
        ExcludedIds = [.. excludedIds]
    };

    protected static List<T> ListOf<T>(params T[] items) => [.. items];
}
