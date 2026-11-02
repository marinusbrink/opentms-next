using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Platform.Administration.Maintenance;
using OpenTms.Platform.Grid;
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
        /* Problems first (docs/designs/maintenance-view.md, "Components"). */
        report.Entries[0].Name.ShouldBe("queue");
        report.Entries[0].Status.ShouldBe("Degraded");
        report.Entries[0].Error.ShouldBe("timeout");
        report.Entries[1].Tags.ShouldBe(["database", "ready"]);

        report.Process.MachineName.ShouldBe(Environment.MachineName);
        report.Process.StartedAt.ShouldBeLessThanOrEqualTo(DateTime.UtcNow);
        report.Process.UptimeSeconds.ShouldBeGreaterThanOrEqualTo(0);
        report.Process.RuntimeVersion.ShouldNotBeNullOrWhiteSpace();
        report.Process.ProcessorCount.ShouldBeGreaterThan(0);
        report.Process.WorkingSetBytes.ShouldBeGreaterThan(0);
    }

    private static HealthCheckEntry Entry(string name, string status, string[] tags, Dictionary<string, string>? data = null) =>
        new(name, status, name + " says " + status, TimeSpan.FromMilliseconds(3), tags, null, data);

    [Fact]
    public async Task The_facts_of_a_check_become_typed_fields_and_the_rest_stays_for_the_texts()
    {
        _provider.Report = new HealthReport("Degraded", TimeSpan.FromMilliseconds(3),
        [
            Entry("pdfgeneratorapi", "Degraded", [HealthCheckFacts.ExternalTag], new Dictionary<string, string>
            {
                [HealthCheckFacts.Reason] = HealthCheckFacts.Reasons.NotConfigured,
                [HealthCheckFacts.Settings] = "Integrations:PdfGeneratorApi:Key, Integrations:PdfGeneratorApi:Secret",
                [HealthCheckFacts.Target] = "us1.pdfgeneratorapi.com",
                ["status"] = "401"
            }),
            Entry("route-optimizer", "Healthy", [HealthCheckFacts.ExternalTag], new Dictionary<string, string>
            {
                [HealthCheckFacts.Reason] = HealthCheckFacts.Reasons.NotConfigured,
                [HealthCheckFacts.Optional] = "true"
            }),
            Entry("legacy", "Healthy", ["ready"])
        ]);

        var entries = (await _service.GetReportAsync()).Entries;

        var pdf = entries.Single(e => e.Name == "pdfgeneratorapi");
        pdf.Area.ShouldBe("External");
        pdf.Reason.ShouldBe(HealthCheckFacts.Reasons.NotConfigured);
        pdf.Settings.ShouldBe(["Integrations:PdfGeneratorApi:Key", "Integrations:PdfGeneratorApi:Secret"]);
        pdf.Target.ShouldBe("us1.pdfgeneratorapi.com");
        pdf.IsOptional.ShouldBeFalse();
        pdf.Data.ShouldBe(new Dictionary<string, string> { ["status"] = "401" });

        entries.Single(e => e.Name == "route-optimizer").IsOptional.ShouldBeTrue();

        var legacy = entries.Single(e => e.Name == "legacy");
        legacy.Area.ShouldBe("Core", "a check without facts is still listed");
        legacy.Reason.ShouldBeNull();
        legacy.Settings.ShouldBeEmpty();
        legacy.Data.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_components_list_puts_problems_first_then_the_system_before_external_services()
    {
        _provider.Report = new HealthReport("Unhealthy", TimeSpan.FromMilliseconds(3),
        [
            Entry("postmark", "Healthy", [HealthCheckFacts.ExternalTag]),
            Entry("routing", "Degraded", [HealthCheckFacts.ExternalTag]),
            Entry("cache", "Healthy", [HealthCheckFacts.CoreTag]),
            Entry("outbox", "Degraded", [HealthCheckFacts.CoreTag]),
            Entry("database", "Unhealthy", [HealthCheckFacts.CoreTag, "ready"])
        ]);

        var page = await _service.GetComponentsAsync(new GridRequest { StartRow = 0, EndRow = 50 });

        page.Rows.Select(r => r.Name).ShouldBe(["database", "outbox", "routing", "cache", "postmark"]);
        page.TotalCount.ShouldBe(5);
    }

    [Fact]
    public async Task The_components_list_pages_searches_and_sorts_in_memory()
    {
        _provider.Report = new HealthReport("Healthy", TimeSpan.FromMilliseconds(3),
        [
            Entry("postmark", "Healthy", [HealthCheckFacts.ExternalTag], new Dictionary<string, string> { [HealthCheckFacts.Target] = "api.postmarkapp.com" }),
            Entry("cache", "Healthy", [HealthCheckFacts.CoreTag]),
            Entry("outbox", "Healthy", [HealthCheckFacts.CoreTag])
        ]);

        var searched = await _service.GetComponentsAsync(new GridRequest { StartRow = 0, EndRow = 50, WildcardSearch = "postmarkapp" });
        searched.Rows.ShouldHaveSingleItem().Name.ShouldBe("postmark");
        searched.FilteredCount.ShouldBe(1);
        searched.TotalCount.ShouldBe(3);

        var sorted = await _service.GetComponentsAsync(new GridRequest
        {
            StartRow = 1,
            EndRow = 2,
            SortModels = [new SortModel("name", "desc")]
        });
        sorted.Rows.ShouldHaveSingleItem().Name.ShouldBe("outbox");
    }

    [Fact]
    public async Task A_tenant_cannot_read_the_components()
    {
        using (_currentTenant.Change(Guid.NewGuid()))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetComponentsAsync(new GridRequest { StartRow = 0, EndRow = 50 }));
        }
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
