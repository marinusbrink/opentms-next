using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTms.Platform.Tenants;
using OpenTms.Platform.TestData;
using OpenTms.TestDoubles;
using Shouldly;
using Volo.Abp.Auditing;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Xunit;
using TenantCreateDto = OpenTms.Platform.Tenants.TenantCreateDto;

namespace OpenTms.EntityFrameworkCore.Platform.Tenants;

/// <summary>
/// The orchestration of <see cref="TestDatasetResetJob"/> (docs/designs/65-tenant-test-datasets.md, Behaviour 3 and
/// Guarantees) with recording contributors, a recording seeder and the small in-test address register: wipe in
/// descending Order, then the seed, then generate in ascending Order, all in the tenant's context with one shared run
/// context; Running → Succeeded; Failed with the message and not rethrown; a redelivered finished request does nothing;
/// a tenant that is gone is skipped. Risk class: Critical — the job deletes a tenant's data. The real contributors
/// against PostgreSQL: PostgreSql/TestData/TestDatasetResetPostgreSqlTests.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class TestDatasetResetJobTests : OpenTmsTestBase<TestDatasetJobTestModule>
{
    private readonly ITenantAdministrationAppService _service;
    private readonly TenantStateWriter _stateWriter;
    private readonly DatasetRunLog _log;
    private readonly InTestAddressRegister _addresses;
    private readonly FakeBackgroundJobManager _jobs;

    public TestDatasetResetJobTests()
    {
        _service = GetRequiredService<ITenantAdministrationAppService>();
        _stateWriter = GetRequiredService<TenantStateWriter>();
        _log = GetRequiredService<DatasetRunLog>();
        _addresses = GetRequiredService<InTestAddressRegister>();
        _jobs = GetRequiredService<FakeBackgroundJobManager>();
        _log.Clear();
    }

    private async Task<(Guid Id, TestDatasetResetArgs Args)> QueuedResetAsync(string dataset = TestDatasets.Average, string name = "acme")
    {
        var created = await _service.CreateAsync(new TenantCreateDto { Name = name, AdminEmailAddress = "admin@acme.test" });
        await _stateWriter.SetStateAsync(created.Id, TenantProvisioningState.Succeeded);
        _jobs.Clear();
        await _service.ResetToDatasetAsync(created.Id, new TenantResetToDatasetDto { Dataset = dataset, ConfirmationName = name });
        _log.Clear();
        return (created.Id, _jobs.JobsOf<TestDatasetResetArgs>().ShouldHaveSingleItem());
    }

    private Task RunAsync(TestDatasetResetArgs args) => GetRequiredService<TestDatasetResetJob>().ExecuteAsync(args);

    private async Task<Tenant> TenantAsync(Guid id) => (await _stateWriter.FindAsync(id)).ShouldNotBeNull();

    private static string? State(Tenant tenant) => tenant.GetProperty<string?>(TenantProperties.DatasetResetState);

    [Fact]
    public async Task Wipes_descending_then_seeds_then_generates_ascending()
    {
        var (_, args) = await QueuedResetAsync();

        await RunAsync(args);

        _log.Steps.ShouldBe(new[]
        {
            "wipe:300", "wipe:200", "wipe:100", "wipe:0",
            "seed",
            "generate:0", "generate:100", "generate:200", "generate:300"
        });
    }

    [Fact]
    public async Task Every_step_runs_in_the_tenants_context_and_the_seed_gets_the_tenant()
    {
        var (id, args) = await QueuedResetAsync();

        await RunAsync(args);

        _log.Tenants.ShouldAllBe(t => t == id);
        _log.Tenants.Count.ShouldBe(9);
        _log.SeedContexts.ShouldHaveSingleItem().TenantId.ShouldBe(id);
        GetRequiredService<ICurrentTenant>().Id.ShouldBeNull("the job leaves the host context as it found it");
    }

    [Theory]
    [InlineData(TestDatasets.Average)]
    [InlineData(TestDatasets.Large)]
    public async Task All_contributors_share_one_context_for_the_requested_profile(string dataset)
    {
        var (id, args) = await QueuedResetAsync(dataset);

        await RunAsync(args);

        var context = _log.Contexts.Distinct().ShouldHaveSingleItem();
        context.TenantId.ShouldBe(id);
        context.Profile.ShouldBe(TestDatasetProfile.Get(dataset));
        context.Addresses.Count("NL").ShouldBe(InTestAddressRegister.Build().Count(a => a.CountryCode == "NL"));
        context.Today.ShouldBe(context.Today.Date);
        _log.Contexts.Count.ShouldBe(8);
        _addresses.Loads.ShouldBe(1);
    }

    [Fact]
    public async Task A_later_contributor_reads_what_an_earlier_one_published()
    {
        var (_, args) = await QueuedResetAsync();

        await RunAsync(args);

        /* RecordingContributor(100) publishes; RecordingContributor(300) reads it. */
        _log.Read.ShouldBe(new[] { "published-by-100" });
    }

    [Fact]
    public async Task The_state_is_Running_during_the_run_and_Succeeded_after_it_with_the_request_id()
    {
        var (id, args) = await QueuedResetAsync();
        _log.Probe = async () => State(await TenantAsync(id));

        await RunAsync(args);

        _log.Probed.ShouldNotBeEmpty();
        _log.Probed.ShouldAllBe(s => s == TestDatasetResetState.Running);

        var tenant = await TenantAsync(id);
        State(tenant).ShouldBe(TestDatasetResetState.Succeeded);
        tenant.GetProperty<Guid?>(TenantProperties.DatasetResetRequestId).ShouldBe(args.RequestId);
        tenant.GetProperty<string?>(TenantProperties.DatasetResetError).ShouldBeNull();
        tenant.GetProperty<DateTime?>(TenantProperties.DatasetResetAt).ShouldNotBeNull();
        tenant.GetProperty<string?>(TenantProperties.DatasetName).ShouldBe(TestDatasets.Average);
        tenant.GetProperty<string?>(TenantProperties.ProvisioningState).ShouldBe(TenantProvisioningState.Succeeded);
    }

    [Theory]
    [InlineData("wipe:100", new[] { "wipe:300", "wipe:200", "wipe:100" })]
    [InlineData("generate:200", new[] { "wipe:300", "wipe:200", "wipe:100", "wipe:0", "seed", "generate:0", "generate:100", "generate:200" })]
    public async Task A_failing_step_records_Failed_with_the_message_stops_the_run_and_is_not_rethrown(string failingStep, string[] expectedSteps)
    {
        var (id, args) = await QueuedResetAsync();
        _log.FailOn = failingStep;

        await Should.NotThrowAsync(() => RunAsync(args));

        _log.Steps.ShouldBe(expectedSteps);
        var tenant = await TenantAsync(id);
        State(tenant).ShouldBe(TestDatasetResetState.Failed);
        tenant.GetProperty<string?>(TenantProperties.DatasetResetError).ShouldBe($"InvalidOperationException: boom at {failingStep}");
        tenant.GetProperty<Guid?>(TenantProperties.DatasetResetRequestId).ShouldBe(args.RequestId);
    }

    [Fact]
    public async Task A_failing_seed_or_address_register_records_Failed_too()
    {
        var (id, args) = await QueuedResetAsync();
        _addresses.Failure = new InvalidOperationException("Embedded resource missing");

        await Should.NotThrowAsync(() => RunAsync(args));

        _log.Steps.ShouldBeEmpty("nothing is wiped when the addresses cannot be loaded");
        var tenant = await TenantAsync(id);
        State(tenant).ShouldBe(TestDatasetResetState.Failed);
        tenant.GetProperty<string?>(TenantProperties.DatasetResetError).ShouldBe("InvalidOperationException: Embedded resource missing");

        _addresses.Failure = null;
        _log.FailOn = "seed";
        await _service.ResetToDatasetAsync(id, new TenantResetToDatasetDto { Dataset = TestDatasets.Average, ConfirmationName = "acme" });
        _log.Clear();
        _log.FailOn = "seed";
        await Should.NotThrowAsync(() => RunAsync(_jobs.JobsOf<TestDatasetResetArgs>().Last()));
        _log.Steps.ShouldNotContain("generate:0");
        State(await TenantAsync(id)).ShouldBe(TestDatasetResetState.Failed);
    }

    [Fact]
    public async Task After_a_failure_a_new_reset_can_be_requested_and_succeeds()
    {
        var (id, args) = await QueuedResetAsync();
        _log.FailOn = "generate:300";
        await RunAsync(args);
        _jobs.Clear();

        await _service.ResetToDatasetAsync(id, new TenantResetToDatasetDto { Dataset = TestDatasets.Large, ConfirmationName = "acme" });
        var retry = _jobs.JobsOf<TestDatasetResetArgs>().ShouldHaveSingleItem();
        retry.RequestId.ShouldNotBe(args.RequestId);
        _log.Clear();
        await RunAsync(retry);

        var tenant = await TenantAsync(id);
        State(tenant).ShouldBe(TestDatasetResetState.Succeeded);
        tenant.GetProperty<string?>(TenantProperties.DatasetResetError).ShouldBeNull();
        tenant.GetProperty<string?>(TenantProperties.DatasetName).ShouldBe(TestDatasets.Large);
        _log.Steps.Count.ShouldBe(9);
    }

    [Fact]
    public async Task A_job_for_a_dataset_that_no_longer_exists_records_Failed_and_does_not_rethrow()
    {
        /* Hangfire keeps queued arguments across deploys: a dataset renamed or removed meanwhile must not leave the tenant
         * Queued forever (the service and the UI refuse every new reset while it is Queued or Running). */
        var (id, args) = await QueuedResetAsync();
        args.Dataset = "Retired";

        await Should.NotThrowAsync(() => RunAsync(args));

        _log.Steps.ShouldBeEmpty();
        var tenant = await TenantAsync(id);
        State(tenant).ShouldBe(TestDatasetResetState.Failed);
        tenant.GetProperty<string?>(TenantProperties.DatasetResetError).ShouldNotBeNull().ShouldStartWith("ArgumentOutOfRangeException");
    }

    [Fact]
    public async Task A_redelivery_of_a_finished_request_does_nothing()
    {
        var (id, args) = await QueuedResetAsync();
        await RunAsync(args);
        var finishedAt = (await TenantAsync(id)).GetProperty<DateTime?>(TenantProperties.DatasetResetAt);
        _log.Clear();

        await RunAsync(args);

        _log.Steps.ShouldBeEmpty();
        _addresses.Loads.ShouldBe(1);
        var tenant = await TenantAsync(id);
        State(tenant).ShouldBe(TestDatasetResetState.Succeeded);
        tenant.GetProperty<DateTime?>(TenantProperties.DatasetResetAt).ShouldBe(finishedAt);
    }

    [Fact]
    public async Task A_redelivery_of_a_failed_request_does_nothing_either()
    {
        var (id, args) = await QueuedResetAsync();
        _log.FailOn = "wipe:300";
        await RunAsync(args);
        _log.Clear();

        await RunAsync(args);

        _log.Steps.ShouldBeEmpty();
        State(await TenantAsync(id)).ShouldBe(TestDatasetResetState.Failed);
    }

    [Fact]
    public async Task A_tenant_that_no_longer_exists_is_skipped()
    {
        await Should.NotThrowAsync(() => RunAsync(new TestDatasetResetArgs
        {
            TenantId = Guid.NewGuid(), Dataset = TestDatasets.Average, RequestId = Guid.NewGuid()
        }));

        _log.Steps.ShouldBeEmpty();
        _addresses.Loads.ShouldBe(0);
    }

    [Fact]
    public async Task A_tenant_deleted_after_the_request_is_skipped()
    {
        var (id, args) = await QueuedResetAsync();
        await _stateWriter.SetDatasetResetStateAsync(id, TestDatasetResetState.Failed); // a pending reset blocks nothing in Delete
        await _service.DeleteAsync(id);

        await Should.NotThrowAsync(() => RunAsync(args));

        _log.Steps.ShouldBeEmpty();
    }

    [Fact]
    public void The_job_and_the_module_contributors_are_excluded_from_auditing()
    {
        typeof(TestDatasetResetJob).GetCustomAttribute<DisableAuditingAttribute>().ShouldNotBeNull();
        foreach (var name in new[]
                 {
                     "OpenTms.MasterData.TestData.MasterDataTestDatasetContributor, OpenTms.MasterData.Domain",
                     "OpenTms.Financial.TestData.FinancialTestDatasetContributor, OpenTms.Financial.Application",
                     "OpenTms.Orders.TestData.OrdersTestDatasetContributor, OpenTms.Orders.Application",
                     "OpenTms.PlanningExecution.TestData.PlanningExecutionTestDatasetContributor, OpenTms.PlanningExecution.Application"
                 })
        {
            Type.GetType(name, throwOnError: true)!.GetCustomAttribute<DisableAuditingAttribute>().ShouldNotBeNull(name);
        }
    }
}

/// <summary>What the recording contributors and seeder saw, in order.</summary>
public sealed class DatasetRunLog
{
    private readonly object _gate = new();

    public List<string> Steps { get; } = new();

    public List<Guid?> Tenants { get; } = new();

    public List<TestDatasetContext> Contexts { get; } = new();

    public List<DataSeedContext> SeedContexts { get; } = new();

    public List<string> Read { get; } = new();

    public List<string?> Probed { get; } = new();

    /// <summary>The step (e.g. "wipe:100", "seed") that throws InvalidOperationException("boom at {step}").</summary>
    public string? FailOn { get; set; }

    /// <summary>Called inside every contributor step; its result is collected in <see cref="Probed"/>.</summary>
    public Func<Task<string?>>? Probe { get; set; }

    public void Clear()
    {
        lock (_gate)
        {
            Steps.Clear();
            Tenants.Clear();
            Contexts.Clear();
            SeedContexts.Clear();
            Read.Clear();
            Probed.Clear();
            FailOn = null;
            Probe = null;
        }
    }

    public async Task RecordAsync(string step, Guid? tenantId)
    {
        lock (_gate)
        {
            Steps.Add(step);
            Tenants.Add(tenantId);
        }

        if (Probe != null)
        {
            Probed.Add(await Probe());
        }

        if (FailOn == step)
        {
            throw new InvalidOperationException($"boom at {step}");
        }
    }
}

public sealed class PublishedByEarlierContributor
{
    public string Value { get; init; } = default!;
}

[DisableConventionalRegistration]
public sealed class RecordingContributor : ITestDatasetContributor
{
    private readonly DatasetRunLog _log;
    private readonly ICurrentTenant _currentTenant;

    public RecordingContributor(int order, DatasetRunLog log, ICurrentTenant currentTenant)
    {
        Order = order;
        _log = log;
        _currentTenant = currentTenant;
    }

    public int Order { get; }

    public async Task WipeAsync(TestDatasetContext context)
    {
        _log.Contexts.Add(context);
        await _log.RecordAsync($"wipe:{Order}", _currentTenant.Id);
    }

    public async Task GenerateAsync(TestDatasetContext context)
    {
        _log.Contexts.Add(context);
        await _log.RecordAsync($"generate:{Order}", _currentTenant.Id);
        if (Order == 100)
        {
            context.Publish(new PublishedByEarlierContributor { Value = "published-by-100" });
        }

        if (Order == 300)
        {
            _log.Read.Add(context.Get<PublishedByEarlierContributor>().Value);
        }
    }
}

[DisableConventionalRegistration]
public sealed class RecordingDataSeeder : IDataSeeder
{
    private readonly DatasetRunLog _log;
    private readonly ICurrentTenant _currentTenant;

    public RecordingDataSeeder(DatasetRunLog log, ICurrentTenant currentTenant)
    {
        _log = log;
        _currentTenant = currentTenant;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        _log.SeedContexts.Add(context);
        await _log.RecordAsync("seed", _currentTenant.Id);
    }
}

/// <summary>
/// <see cref="TenantAdministrationTestModule"/> with the module contributors replaced by four recording ones (registered
/// out of order on purpose), the seeder by a recording one and the embedded address register by the in-test one.
/// </summary>
[DependsOn(typeof(TenantAdministrationTestModule))]
public class TestDatasetJobTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var log = new DatasetRunLog();
        context.Services.AddSingleton(log);

        context.Services.RemoveAll<ITestDatasetContributor>();
        foreach (var order in new[] { 300, 0, 200, 100 })
        {
            context.Services.AddSingleton<ITestDatasetContributor>(sp =>
                new RecordingContributor(order, sp.GetRequiredService<DatasetRunLog>(), sp.GetRequiredService<ICurrentTenant>()));
        }

        context.Services.Replace(ServiceDescriptor.Singleton<IDataSeeder>(sp =>
            new RecordingDataSeeder(sp.GetRequiredService<DatasetRunLog>(), sp.GetRequiredService<ICurrentTenant>())));

        var addresses = new InTestAddressRegister();
        context.Services.AddSingleton(addresses);
        context.Services.Replace(ServiceDescriptor.Singleton<TestAddressRegister>(addresses));
    }
}
