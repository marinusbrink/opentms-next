using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace OpenTms.PostgreSql.ReferenceData;

/// <summary>
/// Shared plumbing for the reference-data tests of Orders, Financial and Planning &amp; Execution against real PostgreSQL
/// (designs 51–53): tenant A with its own database, the shared-tier tenant in the host database, writes rolled back.
/// </summary>
public abstract class ReferenceDataPostgreSqlTestBase : OpenTmsPostgreSqlTestBase
{
    protected ICurrentTenant CurrentTenant { get; }

    protected IUnitOfWorkManager UnitOfWorkManager { get; }

    protected static Guid TenantA => PostgreSqlTestDatabases.TenantAId;

    protected static Guid SharedTenant => PostgreSqlTestDatabases.SharedTenantId;

    protected ReferenceDataPostgreSqlTestBase()
    {
        CurrentTenant = GetRequiredService<ICurrentTenant>();
        UnitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();
    }

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

    protected Task FlushAsync() => UnitOfWorkManager.Current!.SaveChangesAsync();

    protected static async Task<PostgresException> ShouldViolateUniqueIndexAsync(Func<Task> insert, string indexName)
    {
        var ex = await Should.ThrowAsync<DbUpdateException>(insert);
        var postgres = ex.InnerException.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        postgres.ConstraintName.ShouldBe(indexName);
        return postgres;
    }

    protected static LocalizedNameDto Names(string en, string? nl = null)
    {
        var names = new LocalizedNameDto { ["en"] = en };
        if (nl != null)
        {
            names["nl"] = nl;
        }

        return names;
    }

    protected static ArchivableListRequestDto List(bool includeArchived = false) => new() { StartRow = 0, EndRow = 100, IncludeArchived = includeArchived };

    protected static NumberSequenceFormatDto Numbering(string? prefix = "X", bool year = false) => new() { Prefix = prefix, IncludeYear = year, Separator = "-", Digits = 5 };
}
