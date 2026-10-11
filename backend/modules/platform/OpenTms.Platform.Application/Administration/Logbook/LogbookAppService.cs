using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Permissions;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.AuditLogging;
using Volo.Abp.Domain.Entities;

namespace OpenTms.Platform.Administration.Logbook;

/* Read-only views on ABP's audit log tables. Exposed only through LogbookAdministrationController
 * (the conventional route is switched off). Every query runs through the audit-log repository of
 * the current tenant's database, and AuditLog / EntityChange are IMultiTenant, so the ABP data
 * filter scopes every row (constitution rule 1): a host admin sees the host logbooks, a tenant
 * admin only the tenant's. */
[RemoteService(IsEnabled = false)]
public class LogbookAppService : PlatformAppServiceBase, ILogbookAppService
{
    private readonly IAuditLogRepository _auditLogRepository;

    public LogbookAppService(IAuditLogRepository auditLogRepository)
    {
        _auditLogRepository = auditLogRepository;
    }

    // ── Technical logbook (requests) ──────────────────────────────────────────

    [Authorize(PlatformPermissions.Administration.Logbook.Technical)]
    public async Task<GridResponse<TechnicalLogbookRowDto>> GetTechnicalListAsync(GridRequest request, TechnicalLogbookFilterDto filter)
    {
        var all = await _auditLogRepository.GetQueryableAsync();
        var filtered = ApplyTechnicalFilters(all, request, filter);

        var totalCount = await AsyncExecuter.LongCountAsync(all);
        var filteredCount = ReferenceEquals(all, filtered) ? totalCount : await AsyncExecuter.LongCountAsync(filtered);

        var page = ApplyTechnicalSorting(filtered, request)
            .Skip(request.StartRow)
            .Take(request.EndRow - request.StartRow)
            .Select(l => new TechnicalLogbookRowDto
            {
                Id = l.Id,
                ExecutionTime = l.ExecutionTime,
                ExecutionDuration = l.ExecutionDuration,
                UserId = l.UserId,
                UserName = l.UserName,
                HttpMethod = l.HttpMethod,
                Url = l.Url,
                HttpStatusCode = l.HttpStatusCode,
                ClientIpAddress = l.ClientIpAddress,
                ApplicationName = l.ApplicationName,
                CorrelationId = l.CorrelationId,
                HasException = l.Exceptions != null && l.Exceptions != "",
                EntityChangeCount = l.EntityChanges.Count
            });

        return new GridResponse<TechnicalLogbookRowDto>
        {
            Rows = await AsyncExecuter.ToListAsync(page),
            TotalCount = totalCount,
            FilteredCount = filteredCount
        };
    }

    [Authorize(PlatformPermissions.Administration.Logbook.Technical)]
    public async Task<TechnicalLogbookDetailDto> GetTechnicalAsync(Guid id)
    {
        var log = await _auditLogRepository.FindAsync(id, includeDetails: true);
        if (log == null)
            throw new EntityNotFoundException(typeof(AuditLog), id);

        return new TechnicalLogbookDetailDto
        {
            Id = log.Id,
            ExecutionTime = log.ExecutionTime,
            ExecutionDuration = log.ExecutionDuration,
            UserId = log.UserId,
            UserName = log.UserName,
            HttpMethod = log.HttpMethod,
            Url = log.Url,
            HttpStatusCode = log.HttpStatusCode,
            ClientIpAddress = log.ClientIpAddress,
            ApplicationName = log.ApplicationName,
            CorrelationId = log.CorrelationId,
            HasException = !string.IsNullOrEmpty(log.Exceptions),
            EntityChangeCount = log.EntityChanges.Count,
            ClientName = log.ClientName,
            ClientId = log.ClientId,
            BrowserInfo = log.BrowserInfo,
            ImpersonatorUserName = log.ImpersonatorUserName,
            Exceptions = log.Exceptions,
            Comments = log.Comments,
            Actions = log.Actions
                .OrderBy(a => a.ExecutionTime)
                .Select(a => new TechnicalLogbookActionDto
                {
                    Id = a.Id,
                    ServiceName = a.ServiceName,
                    MethodName = a.MethodName,
                    Parameters = a.Parameters,
                    ExecutionTime = a.ExecutionTime,
                    ExecutionDuration = a.ExecutionDuration
                })
                .ToList(),
            EntityChanges = log.EntityChanges
                .OrderBy(c => c.ChangeTime)
                .Select(c => MapDetail(c, log))
                .ToList()
        };
    }

    // ── Functional logbook (entity changes) ───────────────────────────────────

    [Authorize(PlatformPermissions.Administration.Logbook.Functional)]
    public async Task<GridResponse<FunctionalLogbookRowDto>> GetFunctionalListAsync(GridRequest request, FunctionalLogbookFilterDto filter)
    {
        var logs = await _auditLogRepository.GetQueryableAsync();

        /* The user is on the request, the change on the entity-change row: one join, projected to
         * the row shape in SQL so paging and sorting stay server-side. */
        var all = logs.SelectMany(l => l.EntityChanges, (l, c) => new EntityChangeSource
        {
            Change = c,
            UserId = l.UserId,
            UserName = l.UserName
        });
        var filtered = ApplyFunctionalFilters(all, request, filter);

        var totalCount = await AsyncExecuter.LongCountAsync(all);
        var filteredCount = ReferenceEquals(all, filtered) ? totalCount : await AsyncExecuter.LongCountAsync(filtered);

        var page = ApplyFunctionalSorting(filtered, request)
            .Skip(request.StartRow)
            .Take(request.EndRow - request.StartRow);

        var rows = (await AsyncExecuter.ToListAsync(page))
            .Select(s => MapRow(s.Change, s.UserId, s.UserName))
            .ToList();

        return new GridResponse<FunctionalLogbookRowDto>
        {
            Rows = rows,
            TotalCount = totalCount,
            FilteredCount = filteredCount
        };
    }

    [Authorize(PlatformPermissions.Administration.Logbook.Functional)]
    public async Task<FunctionalLogbookDetailDto> GetFunctionalAsync(Guid id)
    {
        var logs = await _auditLogRepository.WithDetailsAsync();
        var log = await AsyncExecuter.FirstOrDefaultAsync(logs.Where(l => l.EntityChanges.Any(c => c.Id == id)));
        var change = log?.EntityChanges.FirstOrDefault(c => c.Id == id);
        if (log == null || change == null)
            throw new EntityNotFoundException(typeof(EntityChange), id);

        return MapDetail(change, log);
    }

    // ── Filters ───────────────────────────────────────────────────────────────

    private static IQueryable<AuditLog> ApplyTechnicalFilters(IQueryable<AuditLog> query, GridRequest request, TechnicalLogbookFilterDto filter)
    {
        var result = query;

        if (filter.From != null)
            result = result.Where(l => l.ExecutionTime >= filter.From.Value);
        if (filter.To != null)
            result = result.Where(l => l.ExecutionTime < filter.To.Value);

        var userName = Normalize(filter.UserName);
        if (userName != null)
            result = result.Where(l => l.UserName != null && l.UserName.ToLower().Contains(userName));

        var httpMethod = Normalize(filter.HttpMethod);
        if (httpMethod != null)
            result = result.Where(l => l.HttpMethod != null && l.HttpMethod.ToLower() == httpMethod);

        var url = Normalize(filter.Url);
        if (url != null)
            result = result.Where(l => l.Url != null && l.Url.ToLower().Contains(url));

        if (filter.HttpStatusCode != null)
            result = result.Where(l => l.HttpStatusCode == filter.HttpStatusCode);

        if (filter.HasException == true)
            result = result.Where(l => l.Exceptions != null && l.Exceptions != "");
        else if (filter.HasException == false)
            result = result.Where(l => l.Exceptions == null || l.Exceptions == "");

        var correlationId = Normalize(filter.CorrelationId);
        if (correlationId != null)
            result = result.Where(l => l.CorrelationId != null && l.CorrelationId.ToLower() == correlationId);

        var search = Normalize(request.WildcardSearch);
        if (search != null)
        {
            result = result.Where(l =>
                (l.UserName != null && l.UserName.ToLower().Contains(search)) ||
                (l.Url != null && l.Url.ToLower().Contains(search)) ||
                (l.ClientIpAddress != null && l.ClientIpAddress.ToLower().Contains(search)) ||
                (l.CorrelationId != null && l.CorrelationId.ToLower().Contains(search)));
        }

        return result;
    }

    private static IQueryable<EntityChangeSource> ApplyFunctionalFilters(IQueryable<EntityChangeSource> query, GridRequest request, FunctionalLogbookFilterDto filter)
    {
        var result = query;

        if (filter.From != null)
            result = result.Where(s => s.Change.ChangeTime >= filter.From.Value);
        if (filter.To != null)
            result = result.Where(s => s.Change.ChangeTime < filter.To.Value);

        var changeType = ParseChangeType(filter.ChangeType);
        if (changeType != null)
            result = result.Where(s => s.Change.ChangeType == changeType.Value);

        var entityType = Normalize(filter.EntityTypeFullName);
        if (entityType != null)
            result = result.Where(s => s.Change.EntityTypeFullName.ToLower().Contains(entityType));

        if (!string.IsNullOrWhiteSpace(filter.EntityId))
            result = result.Where(s => s.Change.EntityId == filter.EntityId);

        var userName = Normalize(filter.UserName);
        if (userName != null)
            result = result.Where(s => s.UserName != null && s.UserName.ToLower().Contains(userName));

        var search = Normalize(request.WildcardSearch);
        if (search != null)
        {
            result = result.Where(s =>
                s.Change.EntityTypeFullName.ToLower().Contains(search) ||
                (s.Change.EntityId != null && s.Change.EntityId.ToLower().Contains(search)) ||
                (s.UserName != null && s.UserName.ToLower().Contains(search)));
        }

        return result;
    }

    // ── Sorting: single column (UI-GRID-012); newest first when the grid has no sort ──

    private static IQueryable<AuditLog> ApplyTechnicalSorting(IQueryable<AuditLog> query, GridRequest request)
    {
        var sort = request.SortModels.FirstOrDefault();
        var desc = sort == null || IsDescending(sort);

        var ordered = sort?.ColId switch
        {
            "executionDuration" => OrderBy(query, l => l.ExecutionDuration, desc),
            "userName" => OrderBy(query, l => l.UserName, desc),
            "httpMethod" => OrderBy(query, l => l.HttpMethod, desc),
            "url" => OrderBy(query, l => l.Url, desc),
            "httpStatusCode" => OrderBy(query, l => l.HttpStatusCode, desc),
            "clientIpAddress" => OrderBy(query, l => l.ClientIpAddress, desc),
            "applicationName" => OrderBy(query, l => l.ApplicationName, desc),
            "entityChangeCount" => OrderBy(query, l => l.EntityChanges.Count, desc),
            _ => OrderBy(query, l => l.ExecutionTime, desc)
        };

        return ordered.ThenByDescending(l => l.Id);
    }

    private static IQueryable<EntityChangeSource> ApplyFunctionalSorting(IQueryable<EntityChangeSource> query, GridRequest request)
    {
        var sort = request.SortModels.FirstOrDefault();
        var desc = sort == null || IsDescending(sort);

        var ordered = sort?.ColId switch
        {
            "changeType" => OrderBy(query, s => s.Change.ChangeType, desc),
            "entityTypeFullName" or "entityTypeName" => OrderBy(query, s => s.Change.EntityTypeFullName, desc),
            "entityId" => OrderBy(query, s => s.Change.EntityId, desc),
            "userName" => OrderBy(query, s => s.UserName, desc),
            _ => OrderBy(query, s => s.Change.ChangeTime, desc)
        };

        return ordered.ThenByDescending(s => s.Change.Id);
    }

    private static IOrderedQueryable<T> OrderBy<T, TKey>(IQueryable<T> query, Expression<Func<T, TKey>> key, bool descending) =>
        descending ? query.OrderByDescending(key) : query.OrderBy(key);

    private static bool IsDescending(SortModel sort) =>
        string.Equals(sort.Sort, "desc", StringComparison.OrdinalIgnoreCase);

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static FunctionalLogbookRowDto MapRow(EntityChange change, Guid? userId, string? userName) => new()
    {
        Id = change.Id,
        AuditLogId = change.AuditLogId,
        ChangeTime = change.ChangeTime,
        ChangeType = change.ChangeType.ToString(),
        EntityTypeFullName = change.EntityTypeFullName,
        EntityTypeName = ShortTypeName(change.EntityTypeFullName),
        EntityId = change.EntityId,
        UserId = userId,
        UserName = userName
    };

    private static FunctionalLogbookDetailDto MapDetail(EntityChange change, AuditLog log) => new()
    {
        Id = change.Id,
        AuditLogId = change.AuditLogId,
        ChangeTime = change.ChangeTime,
        ChangeType = change.ChangeType.ToString(),
        EntityTypeFullName = change.EntityTypeFullName,
        EntityTypeName = ShortTypeName(change.EntityTypeFullName),
        EntityId = change.EntityId,
        UserId = log.UserId,
        UserName = log.UserName,
        PropertyChanges = change.PropertyChanges
            .OrderBy(p => p.PropertyName)
            .Select(p => new LogbookPropertyChangeDto
            {
                Id = p.Id,
                PropertyName = p.PropertyName,
                PropertyTypeFullName = p.PropertyTypeFullName,
                OriginalValue = p.OriginalValue,
                NewValue = p.NewValue
            })
            .ToList()
    };

    private static string ShortTypeName(string fullName)
    {
        var lastDot = fullName.LastIndexOf('.');
        return lastDot < 0 ? fullName : fullName[(lastDot + 1)..];
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static EntityChangeType? ParseChangeType(string? value) =>
        Enum.TryParse<EntityChangeType>(value, ignoreCase: true, out var parsed) ? parsed : null;

    /// <summary>The functional row as the join produces it; mapped to the DTO after materialising.</summary>
    private sealed class EntityChangeSource
    {
        public EntityChange Change { get; init; } = default!;
        public Guid? UserId { get; init; }
        public string? UserName { get; init; }
    }
}
