using System;
using System.Threading.Tasks;
using OpenTms.Platform.Administration.Maintenance;
using OpenTms.Platform.Maintenance;
using OpenTms.Platform.Maintenance.Health;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.Maintenance;

/// <summary>
/// Integration tests for the Health tab's application service over the fake report provider.
/// Risk class: High — host-only, and the mapping decides what an operator reads during an incident.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class HealthMaintenanceAppServiceTests : OpenTmsEntityFrameworkCoreTestBase
{
    private readonly IHealthMaintenanceAppService _service;
    private readonly FakeHealthReportProvider _provider;
    private readonly ICurrentTenant _currentTenant;

    public HealthMaintenanceAppServiceTests()
    {
        _service = GetRequiredService<IHealthMaintenanceAppService>();
        _provider = GetRequiredService<FakeHealthReportProvider>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
    }

    [Fact]
    public async Task The_report_maps_every_check_and_adds_the_process_facts()
    {
        _provider.Report = new HealthReport(
            "Degraded",
            TimeSpan.FromMilliseconds(40),
            [
                new HealthCheckEntry("db", "Healthy", "ok", TimeSpan.FromMilliseconds(10), ["database", "ready"], null),
                new HealthCheckEntry("queue", "Degraded", "slow", TimeSpan.FromMilliseconds(30), ["ready"], "timeout")
            ]);

        var report = await _service.GetReportAsync();

        report.Status.ShouldBe("Degraded");
        report.TotalDurationMilliseconds.ShouldBe(40);
        report.Entries.Count.ShouldBe(2);
        report.Entries[1].Name.ShouldBe("queue");
        report.Entries[1].Status.ShouldBe("Degraded");
        report.Entries[1].Error.ShouldBe("timeout");
        report.Entries[0].Tags.ShouldBe(["database", "ready"]);

        report.Process.MachineName.ShouldBe(Environment.MachineName);
        report.Process.StartedAt.ShouldBeLessThanOrEqualTo(DateTime.UtcNow);
        report.Process.UptimeSeconds.ShouldBeGreaterThanOrEqualTo(0);
        report.Process.RuntimeVersion.ShouldNotBeNullOrWhiteSpace();
        report.Process.ProcessorCount.ShouldBeGreaterThan(0);
        report.Process.WorkingSetBytes.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_tenant_cannot_read_the_health_report()
    {
        using (_currentTenant.Change(Guid.NewGuid()))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetReportAsync());
        }
    }
}
