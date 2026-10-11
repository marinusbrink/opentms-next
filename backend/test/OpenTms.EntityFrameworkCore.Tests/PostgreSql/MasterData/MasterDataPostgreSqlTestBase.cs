using System;
using System.Threading.Tasks;
using OpenTms.MasterData;
using OpenTms.Platform.Grid;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace OpenTms.PostgreSql.MasterData;

/// <summary>
/// Shared plumbing for the Master Data tests against real PostgreSQL (PostgreSqlTestDatabases: host
/// database, tenant A with its own database, shared-tier tenant in the host database). Writes run
/// inside an explicit transaction that is rolled back at the end, so the seeded state every test
/// starts from stays intact for the next one.
/// </summary>
public abstract class MasterDataPostgreSqlTestBase : OpenTmsPostgreSqlTestBase
{
    protected ICurrentTenant CurrentTenant { get; }

    protected IUnitOfWorkManager UnitOfWorkManager { get; }

    protected static Guid TenantA => PostgreSqlTestDatabases.TenantAId;

    protected static Guid SharedTenant => PostgreSqlTestDatabases.SharedTenantId;

    protected MasterDataPostgreSqlTestBase()
    {
        CurrentTenant = GetRequiredService<ICurrentTenant>();
        UnitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();
    }

    /// <summary>
    /// Runs <paramref name="action"/> in <paramref name="tenantId"/> inside one transactional unit of work
    /// that is always rolled back. Application services called inside join it; their writes without
    /// autoSave stay pending until <see cref="FlushAsync"/>.
    /// </summary>
    protected async Task InRolledBackTransactionAsync(Guid? tenantId, Func<Task> action)
    {
        using (CurrentTenant.Change(tenantId))
        using (var unitOfWork = UnitOfWorkManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true))
        {
            try
            {
                await action();
            }
            finally
            {
                await unitOfWork.RollbackAsync();
            }
        }
    }

    /// <summary>Sends the pending changes of the ambient unit of work to the (open) transaction.</summary>
    protected Task FlushAsync() => UnitOfWorkManager.Current!.SaveChangesAsync();

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

    protected static TRequest List<TRequest>(int start = 0, int end = 100, string? search = null, string? sortBy = null, string? sortDir = null)
        where TRequest : MasterDataListRequestDto, new()
        => new() { StartRow = start, EndRow = end, WildcardSearch = search, SortBy = sortBy, SortDir = sortDir };

    protected static GridSelectionDto Explicit(params Guid[] ids) => new() { Mode = "Explicit", ExplicitIds = [.. ids] };

    protected static GridSelectionDto FilterBased(string? search = null) => new()
    {
        Mode = "FilterBased",
        FilterRequest = new GridRequest { StartRow = 0, EndRow = 100, WildcardSearch = search }
    };
}
