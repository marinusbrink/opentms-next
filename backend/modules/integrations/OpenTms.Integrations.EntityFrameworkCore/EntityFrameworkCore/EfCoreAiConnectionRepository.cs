using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OpenTms.Integrations.AiConnector;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace OpenTms.Integrations.EntityFrameworkCore;

/// <summary>See <see cref="IAiConnectionRepository"/>: an EF <c>ExecuteUpdate</c>, so the tenant filter applies.</summary>
public class EfCoreAiConnectionRepository : EfCoreRepository<IntegrationsDbContext, AiConnection, Guid>, IAiConnectionRepository
{
    private const string SavepointName = "ai_connection_insert";

    public EfCoreAiConnectionRepository(IDbContextProvider<IntegrationsDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public virtual async Task RecordUseAsync(Guid id, DateTime at, DateTime staleBefore, CancellationToken cancellationToken = default)
    {
        await (await GetDbSetAsync())
            .Where(c => c.Id == id && (c.LastUsedAt == null || c.LastUsedAt < staleBefore))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastUsedAt, at), GetCancellationToken(cancellationToken));
    }

    public virtual async Task<bool> TryInsertAsync(AiConnection connection, CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();
        var token = GetCancellationToken(cancellationToken);
        /* A unique violation aborts a PostgreSQL transaction: the savepoint keeps the caller's transaction usable. */
        var transaction = dbContext.Database.CurrentTransaction;
        if (transaction != null)
        {
            await transaction.CreateSavepointAsync(SavepointName, token);
        }

        try
        {
            await InsertAsync(connection, autoSave: true, token);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            dbContext.Entry(connection).State = EntityState.Detached;
            if (transaction != null)
            {
                await transaction.RollbackToSavepointAsync(SavepointName, token);
            }

            return false;
        }

        if (transaction != null)
        {
            await transaction.ReleaseSavepointAsync(SavepointName, token);
        }

        return true;
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
        => exception.InnerException is DbException inner
           && (inner.SqlState == "23505" || inner.Message.Contains("UNIQUE constraint failed", StringComparison.Ordinal));
}
