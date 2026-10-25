using System;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.MultiTenancy;

namespace OpenTms.EntityFrameworkCore;

/// <summary>
/// Shared plumbing for the reference-data service tests of Orders, Financial and Planning &amp; Execution on SQLite
/// (designs 50–53), the counterpart of MasterDataAppServiceTestBase for the Platform base's DTOs.
/// </summary>
public abstract class ReferenceDataAppServiceTestBase : OpenTmsEntityFrameworkCoreTestBase
{
    protected ICurrentTenant CurrentTenant { get; }

    protected Guid TenantA { get; } = Guid.NewGuid();

    protected Guid TenantB { get; } = Guid.NewGuid();

    protected ReferenceDataAppServiceTestBase()
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

    protected static ArchivableListRequestDto List(int start = 0, int end = 100, string? search = null, string? sortBy = null, string? sortDir = null, bool includeArchived = false)
        => new() { StartRow = start, EndRow = end, WildcardSearch = search, SortBy = sortBy, SortDir = sortDir, IncludeArchived = includeArchived };

    protected static NumberSequenceFormatDto Numbering(string? prefix = "X", bool year = false, bool month = false, string separator = "-", int digits = 5)
        => new() { Prefix = prefix, IncludeYear = year, IncludeMonth = month, Separator = separator, Digits = digits };

    protected static GridSelectionDto Explicit(params Guid[] ids) => new() { Mode = "Explicit", ExplicitIds = [.. ids] };

    protected static GridSelectionDto FilterBased(string? search = null, params Guid[] excludedIds) => new()
    {
        Mode = "FilterBased",
        FilterRequest = new GridRequest { StartRow = 0, EndRow = 100, WildcardSearch = search },
        ExcludedIds = [.. excludedIds]
    };
}
