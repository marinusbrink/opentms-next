using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Platform.Grid;
using Volo.Abp.Authorization;

namespace OpenTms.Platform.Administration.Maintenance;

/* Shared plumbing of the Maintenance application services: the host-side guard and in-memory
 * paging of the small, host-owned lists these tabs show (databases, outboxes). Those lists are
 * built from the tenant store, not from tenant data, so paging them in memory is deliberate. */
public abstract class MaintenanceAppServiceBase : PlatformAppServiceBase
{
    /// <summary>
    /// Host-only tabs act on the process or on host-owned stores. The permission definitions already
    /// keep them off the tenant side; this guard holds even where authorization is relaxed (tests).
    /// </summary>
    protected void EnsureHostSide()
    {
        if (CurrentTenant.IsAvailable)
        {
            throw new AbpAuthorizationException("This maintenance action is available on the host side only.");
        }
    }

    protected static GridResponse<T> Page<T>(IReadOnlyList<T> all, IReadOnlyList<T> filtered, GridRequest request)
    {
        var take = Math.Max(0, request.EndRow - request.StartRow);
        return new GridResponse<T>
        {
            Rows = filtered.Skip(request.StartRow).Take(take).ToList(),
            TotalCount = all.Count,
            FilteredCount = filtered.Count
        };
    }

    protected static IReadOnlyList<T> Sort<T, TKey>(IReadOnlyList<T> rows, Func<T, TKey> key, string direction) =>
        string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase)
            ? rows.OrderByDescending(key).ToList()
            : rows.OrderBy(key).ToList();

    protected static bool Matches(string? value, string search) =>
        value != null && value.Contains(search, StringComparison.OrdinalIgnoreCase);
}
