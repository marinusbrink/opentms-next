using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp.Modularity;
using Xunit;

namespace OpenTms.Platform.DocumentRendering;

/// <summary>
/// The interactive command budget's configuration (docs/designs/50-report-rendering.md, "Command budget"):
/// <c>Platform:DocumentRendering:InteractiveCommandBudget</c>, default 20 s, bound by PlatformApplicationModule. The
/// service's handling of a zero or negative value is exercised in ReportDefinitionCommandBudgetTests. Risk class: High
/// (part of the design's resilience row).
/// </summary>
public class DocumentRenderingOptionsTests
{
    [Fact]
    public void The_default_budget_is_20_seconds_under_Platform_DocumentRendering()
    {
        DocumentRenderingOptions.SectionName.ShouldBe("Platform:DocumentRendering");
        DocumentRenderingOptions.DefaultInteractiveCommandBudget.ShouldBe(TimeSpan.FromSeconds(20));
        new DocumentRenderingOptions().InteractiveCommandBudget.ShouldBe(TimeSpan.FromSeconds(20));
    }

    [Theory]
    [InlineData(null, 20_000)]
    [InlineData("00:00:05", 5_000)]
    [InlineData("00:00:00.200", 200)]
    public void The_Platform_application_module_binds_the_host_configuration(string? configured, int expectedMilliseconds)
    {
        var values = new Dictionary<string, string?>();
        if (configured != null)
        {
            values["Platform:DocumentRendering:InteractiveCommandBudget"] = configured;
        }

        var services = new ServiceCollection();
        services.AddOptions();
        services.ReplaceConfiguration(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        var module = new PlatformApplicationModule();
        typeof(AbpModule).GetProperty("ServiceConfigurationContext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .SetValue(module, new ServiceConfigurationContext(services));
        module.ConfigureServices(new ServiceConfigurationContext(services));

        services.BuildServiceProvider().GetRequiredService<IOptions<DocumentRenderingOptions>>().Value.InteractiveCommandBudget
            .ShouldBe(TimeSpan.FromMilliseconds(expectedMilliseconds));
    }
}
