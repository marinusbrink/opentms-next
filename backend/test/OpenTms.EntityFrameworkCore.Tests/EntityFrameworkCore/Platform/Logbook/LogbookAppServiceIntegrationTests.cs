using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using OpenTms.Platform.Administration.Logbook;
using OpenTms.Platform.Grid;
using Shouldly;
using Volo.Abp.Auditing;
using Volo.Abp.AuditLogging;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.Logbook;

/// <summary>
/// Integration tests for LogbookAppService against in-memory SQLite, through ABP's own audit-log
/// tables (the entries are written the way the runtime writes them: AuditLogInfo → converter →
/// repository).
/// Risk classification:
///   Critical — tenant isolation: a tenant never sees another tenant's or the host's logbooks
///   High     — filters, sorting and paging decide what an administrator finds
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class LogbookAppServiceIntegrationTests : OpenTmsEntityFrameworkCoreTestBase
{
    private readonly ILogbookAppService _logbook;
    private readonly ICurrentTenant _currentTenant;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IAuditLogInfoToAuditLogConverter _converter;

    public LogbookAppServiceIntegrationTests()
    {
        _logbook = GetRequiredService<ILogbookAppService>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _auditLogRepository = GetRequiredService<IAuditLogRepository>();
        _converter = GetRequiredService<IAuditLogInfoToAuditLogConverter>();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private sealed record Seeded(Guid AuditLogId, Guid EntityChangeId);

    /// <summary>Writes one request with one entity change (two property changes) and one action, as the runtime would.</summary>
    private async Task<Seeded> SeedAsync(
        Guid? tenantId,
        string userName = "alice",
        string url = "/api/platform/administration/users",
        string httpMethod = "POST",
        DateTime? executionTime = null,
        int executionDuration = 120,
        bool withException = false,
        EntityChangeType changeType = EntityChangeType.Updated,
        string entityTypeFullName = "Volo.Abp.Identity.IdentityUser",
        string? entityId = null)
    {
        var info = new AuditLogInfo
        {
            ApplicationName = "OpenTms",
            TenantId = tenantId,
            UserId = Guid.NewGuid(),
            UserName = userName,
            ExecutionTime = executionTime ?? DateTime.UtcNow,
            ExecutionDuration = executionDuration,
            ClientIpAddress = "10.0.0.1",
            CorrelationId = Guid.NewGuid().ToString("N"),
            HttpMethod = httpMethod,
            Url = url,
            HttpStatusCode = withException ? (int)HttpStatusCode.InternalServerError : (int)HttpStatusCode.OK,
            BrowserInfo = "test-agent"
        };
        if (withException)
            info.Exceptions.Add(new InvalidOperationException("boom"));

        info.Actions.Add(new AuditLogActionInfo
        {
            ServiceName = "OpenTms.Platform.Administration.UserAppService",
            MethodName = "UpdateAsync",
            Parameters = "{}",
            ExecutionTime = info.ExecutionTime,
            ExecutionDuration = executionDuration
        });

        var change = new EntityChangeInfo
        {
            ChangeTime = info.ExecutionTime,
            ChangeType = changeType,
            EntityTenantId = tenantId,
            EntityId = entityId ?? Guid.NewGuid().ToString(),
            EntityTypeFullName = entityTypeFullName,
            PropertyChanges =
            [
                new EntityPropertyChangeInfo { PropertyName = "Surname", PropertyTypeFullName = "System.String", OriginalValue = "\"Old\"", NewValue = "\"New\"" },
                new EntityPropertyChangeInfo { PropertyName = "Name", PropertyTypeFullName = "System.String", OriginalValue = "\"A\"", NewValue = "\"B\"" }
            ]
        };
        info.EntityChanges.Add(change);

        Guid auditLogId = default;
        Guid entityChangeId = default;
        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                var auditLog = await _converter.ConvertAsync(info);
                await _auditLogRepository.InsertAsync(auditLog, autoSave: true);
                auditLogId = auditLog.Id;
                entityChangeId = auditLog.EntityChanges.Single().Id;
            }
        });
        return new Seeded(auditLogId, entityChangeId);
    }

    private static GridRequest Page(int startRow = 0, int endRow = 50, string? sortBy = null, string sortDir = "asc", string? search = null) => new()
    {
        StartRow = startRow,
        EndRow = endRow,
        WildcardSearch = search,
        SortModels = sortBy == null ? new List<SortModel>() : new List<SortModel> { new(sortBy, sortDir) }
    };

    // ── Critical: tenant isolation ───────────────────────────────────────────

    [Fact]
    public async Task Technical_logbook_of_tenant_A_is_invisible_to_tenant_B_and_to_the_host()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var seeded = await SeedAsync(tenantA, url: "/api/isolation-a");

        using (_currentTenant.Change(tenantA))
        {
            var mine = await _logbook.GetTechnicalListAsync(Page(search: "isolation-a"), new TechnicalLogbookFilterDto());
            mine.Rows.ShouldContain(r => r.Id == seeded.AuditLogId);
        }

        using (_currentTenant.Change(tenantB))
        {
            var theirs = await _logbook.GetTechnicalListAsync(Page(), new TechnicalLogbookFilterDto());
            theirs.TotalCount.ShouldBe(0);
            theirs.Rows.ShouldBeEmpty();
            await Should.ThrowAsync<EntityNotFoundException>(() => _logbook.GetTechnicalAsync(seeded.AuditLogId));
        }

        using (_currentTenant.Change(null))
        {
            var host = await _logbook.GetTechnicalListAsync(Page(search: "isolation-a"), new TechnicalLogbookFilterDto());
            host.Rows.ShouldNotContain(r => r.Id == seeded.AuditLogId);
            await Should.ThrowAsync<EntityNotFoundException>(() => _logbook.GetTechnicalAsync(seeded.AuditLogId));
        }
    }

    [Fact]
    public async Task Functional_logbook_of_tenant_A_is_invisible_to_tenant_B_and_to_the_host()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var seeded = await SeedAsync(tenantA, entityTypeFullName: "OpenTms.Isolation.EntityA");

        using (_currentTenant.Change(tenantA))
        {
            var mine = await _logbook.GetFunctionalListAsync(Page(), new FunctionalLogbookFilterDto { EntityTypeFullName = "Isolation.EntityA" });
            mine.Rows.ShouldContain(r => r.Id == seeded.EntityChangeId);
        }

        using (_currentTenant.Change(tenantB))
        {
            var theirs = await _logbook.GetFunctionalListAsync(Page(), new FunctionalLogbookFilterDto());
            theirs.TotalCount.ShouldBe(0);
            theirs.Rows.ShouldBeEmpty();
            await Should.ThrowAsync<EntityNotFoundException>(() => _logbook.GetFunctionalAsync(seeded.EntityChangeId));
        }

        using (_currentTenant.Change(null))
        {
            var host = await _logbook.GetFunctionalListAsync(Page(), new FunctionalLogbookFilterDto { EntityTypeFullName = "Isolation.EntityA" });
            host.Rows.ShouldNotContain(r => r.Id == seeded.EntityChangeId);
            await Should.ThrowAsync<EntityNotFoundException>(() => _logbook.GetFunctionalAsync(seeded.EntityChangeId));
        }
    }

    // ── High: technical logbook ──────────────────────────────────────────────

    [Fact]
    public async Task Technical_list_is_newest_first_by_default_and_honours_an_explicit_sort()
    {
        var tenant = Guid.NewGuid();
        var older = await SeedAsync(tenant, executionTime: DateTime.UtcNow.AddMinutes(-10), executionDuration: 50);
        var newer = await SeedAsync(tenant, executionTime: DateTime.UtcNow.AddMinutes(-1), executionDuration: 500);

        using (_currentTenant.Change(tenant))
        {
            var byDefault = await _logbook.GetTechnicalListAsync(Page(), new TechnicalLogbookFilterDto());
            byDefault.Rows.Select(r => r.Id).ShouldBe([newer.AuditLogId, older.AuditLogId]);

            var byDuration = await _logbook.GetTechnicalListAsync(Page(sortBy: "executionDuration", sortDir: "asc"), new TechnicalLogbookFilterDto());
            byDuration.Rows.Select(r => r.Id).ShouldBe([older.AuditLogId, newer.AuditLogId]);
        }
    }

    [Fact]
    public async Task Technical_list_filters_on_errors_period_method_and_search()
    {
        var tenant = Guid.NewGuid();
        var ok = await SeedAsync(tenant, url: "/api/ok", httpMethod: "GET", executionTime: DateTime.UtcNow.AddDays(-2));
        var failed = await SeedAsync(tenant, url: "/api/failed", httpMethod: "POST", withException: true, executionTime: DateTime.UtcNow.AddMinutes(-5));

        using (_currentTenant.Change(tenant))
        {
            var errors = await _logbook.GetTechnicalListAsync(Page(), new TechnicalLogbookFilterDto { HasException = true });
            errors.Rows.Select(r => r.Id).ShouldBe([failed.AuditLogId]);
            errors.Rows[0].HasException.ShouldBeTrue();
            errors.FilteredCount.ShouldBe(1);
            errors.TotalCount.ShouldBe(2);

            var recent = await _logbook.GetTechnicalListAsync(Page(), new TechnicalLogbookFilterDto { From = DateTime.UtcNow.AddHours(-1) });
            recent.Rows.Select(r => r.Id).ShouldBe([failed.AuditLogId]);

            var gets = await _logbook.GetTechnicalListAsync(Page(), new TechnicalLogbookFilterDto { HttpMethod = "get" });
            gets.Rows.Select(r => r.Id).ShouldBe([ok.AuditLogId]);

            var searched = await _logbook.GetTechnicalListAsync(Page(search: "API/FAIL"), new TechnicalLogbookFilterDto());
            searched.Rows.Select(r => r.Id).ShouldBe([failed.AuditLogId]);
        }
    }

    [Fact]
    public async Task Technical_list_pages_within_the_requested_window()
    {
        var tenant = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
            await SeedAsync(tenant, executionTime: DateTime.UtcNow.AddMinutes(-i));

        using (_currentTenant.Change(tenant))
        {
            var page = await _logbook.GetTechnicalListAsync(Page(startRow: 2, endRow: 4), new TechnicalLogbookFilterDto());
            page.Rows.Count.ShouldBe(2);
            page.TotalCount.ShouldBe(5);
            page.FilteredCount.ShouldBe(5);
        }
    }

    [Fact]
    public async Task Technical_detail_carries_the_actions_and_the_entity_changes()
    {
        var tenant = Guid.NewGuid();
        var seeded = await SeedAsync(tenant, withException: true);

        using (_currentTenant.Change(tenant))
        {
            var detail = await _logbook.GetTechnicalAsync(seeded.AuditLogId);

            detail.Id.ShouldBe(seeded.AuditLogId);
            detail.HasException.ShouldBeTrue();
            detail.Exceptions.ShouldNotBeNullOrEmpty();
            detail.Actions.Count.ShouldBe(1);
            detail.Actions[0].MethodName.ShouldBe("UpdateAsync");
            detail.EntityChanges.Count.ShouldBe(1);
            detail.EntityChanges[0].Id.ShouldBe(seeded.EntityChangeId);
            detail.EntityChanges[0].PropertyChanges.Count.ShouldBe(2);
            detail.EntityChangeCount.ShouldBe(1);
        }
    }

    // ── High: functional logbook ─────────────────────────────────────────────

    [Fact]
    public async Task Functional_list_shows_the_user_of_the_request_and_is_newest_first()
    {
        var tenant = Guid.NewGuid();
        var older = await SeedAsync(tenant, userName: "bob", executionTime: DateTime.UtcNow.AddMinutes(-10));
        var newer = await SeedAsync(tenant, userName: "carol", executionTime: DateTime.UtcNow.AddMinutes(-1));

        using (_currentTenant.Change(tenant))
        {
            var rows = await _logbook.GetFunctionalListAsync(Page(), new FunctionalLogbookFilterDto());
            rows.Rows.Select(r => r.Id).ShouldBe([newer.EntityChangeId, older.EntityChangeId]);
            rows.Rows[0].UserName.ShouldBe("carol");
            rows.Rows[0].AuditLogId.ShouldBe(newer.AuditLogId);
            rows.Rows[0].EntityTypeName.ShouldBe("IdentityUser");
            rows.Rows[0].ChangeType.ShouldBe(LogbookChangeTypes.Updated);
        }
    }

    [Fact]
    public async Task Functional_list_filters_on_change_type_entity_type_entity_id_and_user()
    {
        var tenant = Guid.NewGuid();
        var created = await SeedAsync(tenant, userName: "dave", changeType: EntityChangeType.Created, entityTypeFullName: "OpenTms.Orders.Order", entityId: "order-1");
        var deleted = await SeedAsync(tenant, userName: "erin", changeType: EntityChangeType.Deleted, entityTypeFullName: "OpenTms.MasterData.Vehicle", entityId: "vehicle-9");

        using (_currentTenant.Change(tenant))
        {
            var byType = await _logbook.GetFunctionalListAsync(Page(), new FunctionalLogbookFilterDto { ChangeType = "deleted" });
            byType.Rows.Select(r => r.Id).ShouldBe([deleted.EntityChangeId]);
            byType.FilteredCount.ShouldBe(1);
            byType.TotalCount.ShouldBe(2);

            var byEntityType = await _logbook.GetFunctionalListAsync(Page(), new FunctionalLogbookFilterDto { EntityTypeFullName = "orders.order" });
            byEntityType.Rows.Select(r => r.Id).ShouldBe([created.EntityChangeId]);

            var byEntityId = await _logbook.GetFunctionalListAsync(Page(), new FunctionalLogbookFilterDto { EntityId = "vehicle-9" });
            byEntityId.Rows.Select(r => r.Id).ShouldBe([deleted.EntityChangeId]);

            var byUser = await _logbook.GetFunctionalListAsync(Page(), new FunctionalLogbookFilterDto { UserName = "DAV" });
            byUser.Rows.Select(r => r.Id).ShouldBe([created.EntityChangeId]);

            var searched = await _logbook.GetFunctionalListAsync(Page(search: "vehicle"), new FunctionalLogbookFilterDto());
            searched.Rows.Select(r => r.Id).ShouldBe([deleted.EntityChangeId]);
        }
    }

    [Fact]
    public async Task Functional_detail_lists_the_property_changes_in_name_order()
    {
        var tenant = Guid.NewGuid();
        var seeded = await SeedAsync(tenant, userName: "frank");

        using (_currentTenant.Change(tenant))
        {
            var detail = await _logbook.GetFunctionalAsync(seeded.EntityChangeId);

            detail.Id.ShouldBe(seeded.EntityChangeId);
            detail.AuditLogId.ShouldBe(seeded.AuditLogId);
            detail.UserName.ShouldBe("frank");
            detail.PropertyChanges.Select(p => p.PropertyName).ShouldBe(["Name", "Surname"]);
            detail.PropertyChanges[1].OriginalValue.ShouldBe("\"Old\"");
            detail.PropertyChanges[1].NewValue.ShouldBe("\"New\"");
        }
    }

    [Fact]
    public async Task Functional_filter_rejects_an_unknown_change_type()
    {
        var filter = new FunctionalLogbookFilterDto { ChangeType = "Exploded" };
        var results = filter.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(filter)).ToList();
        results.ShouldHaveSingleItem().MemberNames.ShouldContain(nameof(FunctionalLogbookFilterDto.ChangeType));
    }
}
