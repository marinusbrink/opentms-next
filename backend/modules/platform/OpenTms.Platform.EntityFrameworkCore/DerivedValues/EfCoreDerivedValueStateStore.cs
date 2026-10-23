using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OpenTms.Platform.EntityFrameworkCore;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;

namespace OpenTms.Platform.DerivedValues;

/// <summary>
/// The state statements of design 49 (<see cref="IDerivedValueStateStore"/>): one atomic upsert for a request, one
/// guarded update to complete or fail an attempt, both on the Platform's own table in the current transaction
/// (<see cref="PlatformSqlCommands"/>). Reads go through EF with the tenant filter.
/// </summary>
public class EfCoreDerivedValueStateStore : IDerivedValueStateStore, ITransientDependency
{
    private readonly IDbContextProvider<PlatformDbContext> _dbContextProvider;
    private readonly ICurrentTenant _currentTenant;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IClock _clock;

    public EfCoreDerivedValueStateStore(
        IDbContextProvider<PlatformDbContext> dbContextProvider,
        ICurrentTenant currentTenant,
        IGuidGenerator guidGenerator,
        IClock clock)
    {
        _dbContextProvider = dbContextProvider;
        _currentTenant = currentTenant;
        _guidGenerator = guidGenerator;
        _clock = clock;
    }

    public virtual async Task<long> RequestAsync(string targetType, Guid targetId)
    {
        var db = await _dbContextProvider.GetDbContextAsync();
        var sql =
            $"INSERT INTO {PlatformSqlCommands.TableName<DerivedValueState>(db)} AS s " +
            "(\"Id\", \"TenantId\", \"TargetType\", \"TargetId\", \"RequestedVersion\", \"CalculatedVersion\", \"Status\", \"FailedAttempts\", \"RequestedAt\") " +
            "VALUES (@id, @tenantId, @type, @targetId, 1, 0, @pending, 0, @now) " +
            "ON CONFLICT (\"TenantId\", \"TargetType\", \"TargetId\") DO UPDATE SET " +
            "\"RequestedVersion\" = s.\"RequestedVersion\" + 1, \"Status\" = @pending, \"RequestedAt\" = @now " +
            "RETURNING \"RequestedVersion\"";

        await using var command = await PlatformSqlCommands.CreateAsync(db, sql);
        PlatformSqlCommands.Add(command, "@id", PlatformSqlCommands.GuidValue(db, _guidGenerator.Create()));
        PlatformSqlCommands.Add(command, "@tenantId", PlatformSqlCommands.GuidValue(db, _currentTenant.Id));
        PlatformSqlCommands.Add(command, "@type", targetType);
        PlatformSqlCommands.Add(command, "@targetId", PlatformSqlCommands.GuidValue(db, targetId));
        PlatformSqlCommands.Add(command, "@pending", (int)DerivedValueStatus.Pending);
        PlatformSqlCommands.Add(command, "@now", _clock.Now);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public virtual async Task<IReadOnlyDictionary<Guid, long>> RequestManyAsync(string targetType, IReadOnlyCollection<Guid> targetIds)
    {
        var ids = targetIds.Distinct().ToArray();
        var result = new Dictionary<Guid, long>(ids.Length);
        if (ids.Length == 0)
        {
            return result;
        }

        var db = await _dbContextProvider.GetDbContextAsync();
        if (PlatformSqlCommands.IsSqlite(db))
        {
            /* The unit-test provider has no arrays; one statement per id there. */
            foreach (var id in ids)
            {
                result[id] = await RequestAsync(targetType, id);
            }

            return result;
        }

        var sql =
            $"INSERT INTO {PlatformSqlCommands.TableName<DerivedValueState>(db)} AS s " +
            "(\"Id\", \"TenantId\", \"TargetType\", \"TargetId\", \"RequestedVersion\", \"CalculatedVersion\", \"Status\", \"FailedAttempts\", \"RequestedAt\") " +
            "SELECT n.row_id, @tenantId, @type, n.target_id, 1, 0, @pending, 0, @now FROM unnest(@rowIds, @targetIds) AS n(row_id, target_id) " +
            "ON CONFLICT (\"TenantId\", \"TargetType\", \"TargetId\") DO UPDATE SET " +
            "\"RequestedVersion\" = s.\"RequestedVersion\" + 1, \"Status\" = @pending, \"RequestedAt\" = @now " +
            "RETURNING \"TargetId\", \"RequestedVersion\"";

        await using var command = await PlatformSqlCommands.CreateAsync(db, sql);
        PlatformSqlCommands.Add(command, "@rowIds", ids.Select(_ => _guidGenerator.Create()).ToArray());
        PlatformSqlCommands.Add(command, "@targetIds", ids);
        PlatformSqlCommands.Add(command, "@tenantId", PlatformSqlCommands.GuidValue(db, _currentTenant.Id));
        PlatformSqlCommands.Add(command, "@type", targetType);
        PlatformSqlCommands.Add(command, "@pending", (int)DerivedValueStatus.Pending);
        PlatformSqlCommands.Add(command, "@now", _clock.Now);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result[reader.GetGuid(0)] = reader.GetInt64(1);
        }

        return result;
    }

    public virtual async Task<bool> TryCompleteAsync(Guid stateId, long version)
    {
        var db = await _dbContextProvider.GetDbContextAsync();
        var sql =
            $"UPDATE {PlatformSqlCommands.TableName<DerivedValueState>(db)} SET " +
            "\"CalculatedVersion\" = @version, \"Status\" = @upToDate, \"FailedAttempts\" = 0, \"LastErrorCode\" = NULL, \"CalculatedAt\" = @now " +
            "WHERE \"Id\" = @stateId AND \"RequestedVersion\" = @version";

        await using var command = await GuardedAsync(db, sql, stateId, version);
        PlatformSqlCommands.Add(command, "@upToDate", (int)DerivedValueStatus.UpToDate);
        PlatformSqlCommands.Add(command, "@now", _clock.Now);
        return await command.ExecuteNonQueryAsync() == 1;
    }

    public virtual async Task<bool> TryFailAsync(Guid stateId, long version, string errorCode)
    {
        var db = await _dbContextProvider.GetDbContextAsync();
        var sql =
            $"UPDATE {PlatformSqlCommands.TableName<DerivedValueState>(db)} SET " +
            "\"Status\" = @failed, \"FailedAttempts\" = \"FailedAttempts\" + 1, \"LastErrorCode\" = @code " +
            "WHERE \"Id\" = @stateId AND \"RequestedVersion\" = @version";

        await using var command = await GuardedAsync(db, sql, stateId, version);
        PlatformSqlCommands.Add(command, "@failed", (int)DerivedValueStatus.Failed);
        var code = errorCode.Length > DerivedValueConsts.MaxErrorCodeLength ? errorCode[..DerivedValueConsts.MaxErrorCodeLength] : errorCode;
        PlatformSqlCommands.Add(command, "@code", code);
        return await command.ExecuteNonQueryAsync() == 1;
    }

    public virtual async Task<DerivedValueState?> FindAsync(string targetType, Guid targetId)
    {
        var db = await _dbContextProvider.GetDbContextAsync();
        var tenantId = _currentTenant.Id;
        /* No tracking: the rows change through the statements above, never through EF. */
        return await db.DerivedValueStates.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.TargetType == targetType && s.TargetId == targetId);
    }

    public virtual async Task<List<DerivedValueState>> GetListAsync(string targetType, IReadOnlyCollection<Guid> targetIds)
    {
        if (targetIds.Count == 0)
        {
            return [];
        }

        var db = await _dbContextProvider.GetDbContextAsync();
        var tenantId = _currentTenant.Id;
        var ids = targetIds.ToList();
        return await db.DerivedValueStates.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.TargetType == targetType && ids.Contains(s.TargetId))
            .ToListAsync();
    }

    private static async Task<DbCommand> GuardedAsync(PlatformDbContext db, string sql, Guid stateId, long version)
    {
        var command = await PlatformSqlCommands.CreateAsync(db, sql);
        PlatformSqlCommands.Add(command, "@stateId", PlatformSqlCommands.GuidValue(db, stateId));
        PlatformSqlCommands.Add(command, "@version", version);
        return command;
    }
}
