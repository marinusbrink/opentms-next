using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OpenTms.Platform.Events.Outbox;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace OpenTms.Platform.Events;

/// <summary>
/// Risk class: medium — the worker is the only component that makes delivery happen in production.
/// The dispatcher and the tenant store are substituted, so no timing beyond "within a few seconds".
/// </summary>
public class OutboxDispatchWorkerTests : IAsyncLifetime
{
    private static readonly TimeSpan NoSweepDuringTest = TimeSpan.FromHours(1);

    private readonly Guid _ownDatabaseTenant = Guid.NewGuid();
    private readonly Guid _sharedTenant = Guid.NewGuid();
    private readonly Guid _secondOwnDatabaseTenant = Guid.NewGuid();

    private readonly IOutboxDispatcher _dispatcher = Substitute.For<IOutboxDispatcher>();
    private readonly List<Guid?> _dispatched = new();
    private readonly OutboxSignal _signal = new();
    private OutboxDispatchWorker _worker = default!;

    public Task InitializeAsync()
    {
        _dispatcher.DispatchAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                lock (_dispatched)
                {
                    _dispatched.Add(call.Arg<Guid?>());
                }

                return Task.FromResult(0);
            });

        var tenantStore = Substitute.For<ITenantStore>();
        tenantStore.GetListAsync(Arg.Any<bool>()).Returns(new List<TenantConfiguration>
        {
            Tenant(_ownDatabaseTenant, "Host=a;Database=a"),
            Tenant(_sharedTenant, null),
            Tenant(_secondOwnDatabaseTenant, "Host=b;Database=b"),
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(tenantStore);
        services.AddSingleton(_dispatcher);
        var provider = services.BuildServiceProvider();

        _worker = new OutboxDispatchWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            _signal,
            Options.Create(new OutboxOptions { SweepPeriod = NoSweepDuringTest }))
        {
            LazyServiceProvider = new AbpLazyServiceProvider(provider),
            ServiceProvider = provider,
        };

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _worker.StopAsync();
    }

    [Fact]
    public async Task Starting_sweeps_the_host_and_every_tenant_with_its_own_database_but_not_shared_tier_tenants()
    {
        await _worker.StartAsync();

        await WaitUntilAsync(() => Dispatched().Count >= 3);

        Dispatched().ShouldBe(new Guid?[] { null, _ownDatabaseTenant, _secondOwnDatabaseTenant });
        Dispatched().ShouldNotContain(_sharedTenant);
    }

    [Fact]
    public async Task A_commit_signal_dispatches_that_tenant_once()
    {
        await _worker.StartAsync();
        await WaitUntilAsync(() => Dispatched().Count >= 3);

        _signal.Notify(_sharedTenant);

        await WaitUntilAsync(() => Dispatched().Count >= 4);
        await Task.Delay(200); // nothing else should follow

        Dispatched().Skip(3).ShouldBe(new Guid?[] { _sharedTenant });
    }

    [Fact]
    public async Task The_sweep_continues_past_a_failing_tenant()
    {
        _dispatcher.DispatchAsync(_ownDatabaseTenant, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database unreachable"));

        await _worker.StartAsync();

        await WaitUntilAsync(() => Dispatched().Contains(_secondOwnDatabaseTenant));
        Dispatched().ShouldBe(new Guid?[] { null, _secondOwnDatabaseTenant });
    }

    [Fact]
    public async Task Stopping_ends_the_loop_promptly_and_no_dispatch_follows()
    {
        await _worker.StartAsync();
        await WaitUntilAsync(() => Dispatched().Count >= 3);

        var stop = _worker.StopAsync();
        (await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(5)))).ShouldBe(stop);

        var countAfterStop = Dispatched().Count;
        _signal.Notify(_ownDatabaseTenant);
        await Task.Delay(200);

        Dispatched().Count.ShouldBe(countAfterStop);
    }

    private List<Guid?> Dispatched()
    {
        lock (_dispatched)
        {
            return _dispatched.ToList();
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The worker did not reach the expected state within 5 seconds.");
            }

            await Task.Delay(20);
        }
    }

    private static TenantConfiguration Tenant(Guid id, string? defaultConnectionString)
    {
        var tenant = new TenantConfiguration(id, id.ToString("N"));
        if (defaultConnectionString != null)
        {
            tenant.ConnectionStrings!.Default = defaultConnectionString;
        }

        return tenant;
    }
}
