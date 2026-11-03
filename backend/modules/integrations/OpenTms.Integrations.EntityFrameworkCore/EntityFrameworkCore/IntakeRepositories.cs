using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OpenTms.Integrations.OrderIntake;
using OpenTms.Integrations.OrderIntake.Profiles;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace OpenTms.Integrations.EntityFrameworkCore;

/// <summary>See <see cref="IApiClientRepository"/>: an EF <c>ExecuteUpdate</c>, so the tenant and soft-delete filters apply.</summary>
public class EfCoreApiClientRepository : EfCoreRepository<IntegrationsDbContext, ApiClient, Guid>, IApiClientRepository
{
    public EfCoreApiClientRepository(IDbContextProvider<IntegrationsDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public virtual async Task<bool> RecordUseAsync(Guid id, DateTime at, DateTime staleBefore, CancellationToken cancellationToken = default)
    {
        var written = await (await GetDbSetAsync())
            .Where(c => c.Id == id && (c.LastUsedAt == null || c.LastUsedAt < staleBefore))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastUsedAt, at), GetCancellationToken(cancellationToken));
        return written > 0;
    }
}

/// <summary>See <see cref="IImportProfileRepository"/>: an EF <c>ExecuteUpdate</c>, so the tenant and soft-delete filters apply.</summary>
public class EfCoreImportProfileRepository : EfCoreRepository<IntegrationsDbContext, ImportProfile, Guid>, IImportProfileRepository
{
    public EfCoreImportProfileRepository(IDbContextProvider<IntegrationsDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public virtual async Task<bool> RecordUseAsync(Guid id, DateTime at, DateTime staleBefore, CancellationToken cancellationToken = default)
    {
        var written = await (await GetDbSetAsync())
            .Where(p => p.Id == id && (p.LastUsedAt == null || p.LastUsedAt < staleBefore))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.LastUsedAt, at), GetCancellationToken(cancellationToken));
        return written > 0;
    }
}

/// <summary>See <see cref="IInboundMessageRepository"/>.</summary>
public class EfCoreInboundMessageRepository : EfCoreRepository<IntegrationsDbContext, InboundMessage, Guid>, IInboundMessageRepository
{
    private const string SavepointName = "inbound_message_insert";

    public EfCoreInboundMessageRepository(IDbContextProvider<IntegrationsDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public virtual async Task<bool> TryInsertAsync(InboundMessage message, CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();
        var token = GetCancellationToken(cancellationToken);
        /* A unique violation aborts a PostgreSQL transaction: the savepoint keeps the caller's transaction usable. */
        var transaction = dbContext.Database.CurrentTransaction;
        if (transaction != null)
        {
            /* Whatever else is pending is written first, so the savepoint covers the message alone. */
            await dbContext.SaveChangesAsync(token);
            await transaction.CreateSavepointAsync(SavepointName, token);
        }

        try
        {
            await InsertAsync(message, autoSave: true, token);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            dbContext.Entry(message).State = EntityState.Detached;
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

    public virtual async Task<IQueryable<InboundMessage>> WhereSearchMatchesAsync(IQueryable<InboundMessage> query, string term)
    {
        var text = term.Trim();
        if (text.Length == 0)
        {
            return query;
        }

        if ((await GetDbContextAsync()).Database.IsNpgsql())
        {
            var pattern = IntegrationsSearchPattern.Contains(text);
            return query.Where(m =>
                EF.Functions.ILike(m.From!, pattern, IntegrationsSearchPattern.Escape) ||
                EF.Functions.ILike(m.Subject!, pattern, IntegrationsSearchPattern.Escape) ||
                EF.Functions.ILike(m.ExternalId, pattern, IntegrationsSearchPattern.Escape));
        }

        /* Any other provider (the SQLite unit tests): the portable lower-case contains. */
        var lowered = text.ToLowerInvariant();
        return query.Where(m =>
            (m.From != null && m.From.ToLower().Contains(lowered)) ||
            (m.Subject != null && m.Subject.ToLower().Contains(lowered)) ||
            m.ExternalId.ToLower().Contains(lowered));
    }

    /// <summary>PostgreSQL 23505; SQLite's "UNIQUE constraint failed" (error 19, the unit tests).</summary>
    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is DbException inner
           && (inner.SqlState == "23505" || inner.Message.Contains("UNIQUE constraint failed", StringComparison.Ordinal));
}

/// <summary>A <c>LIKE</c>/<c>ILIKE</c> "contains" pattern with the term's own wildcards matched literally.</summary>
internal static class IntegrationsSearchPattern
{
    public const string Escape = "\\";

    public static string Contains(string term)
        => "%" + term.Replace(Escape, Escape + Escape, StringComparison.Ordinal)
                     .Replace("%", Escape + "%", StringComparison.Ordinal)
                     .Replace("_", Escape + "_", StringComparison.Ordinal) + "%";
}
