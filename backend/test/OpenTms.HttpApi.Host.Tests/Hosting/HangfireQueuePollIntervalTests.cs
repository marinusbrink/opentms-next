using System;
using System.Collections.Generic;
using System.Reflection;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTms.HostIntegration;
using Shouldly;
using Volo.Abp.Modularity;
using Xunit;

namespace OpenTms.Hosting;

/// <summary>
/// Design 74 §9: the host binds <c>Hangfire:QueuePollInterval</c>, defaults to one second and enables long polling. Runs the host module's
/// own configuration against the host test database, because Hangfire's storage connects when it is built. Risk class: Medium.
/// </summary>
[Collection(HostIntegrationCollection.Name)]
public class HangfireQueuePollIntervalTests
{
    private static PostgreSqlStorageOptions OptionsFor(params (string Key, string Value)[] settings)
    {
        var values = new Dictionary<string, string?> { ["ConnectionStrings:Default"] = HostIntegrationFixture.ConnectionStringFor(HostIntegrationFixture.HostDatabase) };
        foreach (var (key, value) in settings)
        {
            values[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        typeof(OpenTmsHttpApiHostModule).GetMethod("ConfigureHangfire", BindingFlags.Instance | BindingFlags.NonPublic).ShouldNotBeNull()
            .Invoke(new OpenTmsHttpApiHostModule(), [new ServiceConfigurationContext(services), configuration]);

        var previous = JobStorage.Current;
        using var provider = services.BuildServiceProvider();
        var storage = provider.GetRequiredService<JobStorage>().ShouldBeOfType<PostgreSqlStorage>();
        JobStorage.Current = previous;
        return (PostgreSqlStorageOptions)typeof(PostgreSqlStorage).GetProperty("Options", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).ShouldNotBeNull().GetValue(storage).ShouldNotBeNull();
    }

    [Fact]
    public void Without_a_setting_the_queue_polls_every_second_with_long_polling()
    {
        var options = OptionsFor();
        options.QueuePollInterval.ShouldBe(TimeSpan.FromSeconds(1));
        options.EnableLongPolling.ShouldBeTrue();
    }

    [Fact]
    public void The_setting_overrides_the_interval()
    {
        OptionsFor(("Hangfire:QueuePollInterval", "00:00:05")).QueuePollInterval.ShouldBe(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void The_shipped_appsettings_say_one_second()
    {
        var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "OpenTms.slnx")))
        {
            directory = directory.Parent;
        }

        var configuration = new ConfigurationBuilder().AddJsonFile(System.IO.Path.Combine(directory.ShouldNotBeNull().FullName, "src", "OpenTms.HttpApi.Host", "appsettings.json")).Build();
        configuration.GetValue<TimeSpan?>("Hangfire:QueuePollInterval").ShouldBe(TimeSpan.FromSeconds(1));
    }
}
