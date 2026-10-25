using System;
using System.Threading.Tasks;
using OpenTms.Platform;
using OpenTms.PostgreSql.MasterData;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Uow;

namespace OpenTms.PostgreSql.Platform;

/// <summary>
/// Shared plumbing for the Platform master-data tests against real PostgreSQL (design 54): the rolled-back transaction of
/// <see cref="MasterDataPostgreSqlTestBase"/> and the shared request and name helpers.
/// </summary>
public abstract class PlatformMasterDataPostgreSqlTestBase : MasterDataPostgreSqlTestBase
{
    protected static readonly AbpUnitOfWorkOptions Transactional = new() { IsTransactional = true };

    /// <summary>The database a tenant's rows live in: tenant A its own, the shared tenant and the host the host database.</summary>
    protected static string DatabaseOf(Guid? tenant) => tenant == TenantA ? PostgreSqlTestDatabases.TenantADatabase : PostgreSqlTestDatabases.HostDatabase;

    protected static string NameFor(Guid? tenant) => tenant == null ? "host" : tenant == TenantA ? "tenant A (own database)" : "shared tenant (host database)";

    protected static PlatformLocalizedNameDto PlatformNames(string en, string? nl = null)
    {
        var names = new PlatformLocalizedNameDto { ["en"] = en };
        if (nl != null)
        {
            names["nl"] = nl;
        }

        return names;
    }

    protected static TRequest PlatformList<TRequest>(string? search = null, int start = 0, int end = 100, string? sortBy = null, string? sortDir = null)
        where TRequest : PlatformMasterDataListRequestDto, new()
        => new() { StartRow = start, EndRow = end, WildcardSearch = search, SortBy = sortBy, SortDir = sortDir };

    protected static async Task<BusinessException> ShouldFailAsync(Func<Task> action, string code)
    {
        var ex = await Should.ThrowAsync<BusinessException>(action);
        ex.Code.ShouldBe(code, ex.Message);
        return ex;
    }
}
