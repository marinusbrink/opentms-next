using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenTms.Platform.Features;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Threading;
using Volo.Abp.Uow;

namespace OpenTms.Orders.TransportOrders;

/// <summary>The template materialisation job's switches ("Orders:TemplateMaterialisation").</summary>
public class TemplateMaterialisationOptions
{
    public const string SectionName = "Orders:TemplateMaterialisation";

    /// <summary>The per-instance switch; every instance may run it (the work is idempotent on template + date + copy).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Hourly by design 58.</summary>
    public TimeSpan Period { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>
/// Materialises templates (design 58 "Templates and recurrence"): for one template and explicit dates (the wizard, "copy
/// order"), or for every active recurring template of the current tenant over its horizon (the hourly job). Each
/// (template, date, copy index) is created once: an order that exists is skipped, the unique index remains the truth.
/// </summary>
public class TemplateMaterialisationRunner : ITransientDependency
{
    private readonly IRepository<TransportOrderTemplate, Guid> _templates;
    private readonly IRepository<TransportOrder, Guid> _orders;
    private readonly TransportOrderCommands _commands;
    private readonly OrdersClock _clock;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IFeatureChecker _features;

    public ILogger<TemplateMaterialisationRunner> Logger { get; set; } = NullLogger<TemplateMaterialisationRunner>.Instance;

    public TemplateMaterialisationRunner(
        IRepository<TransportOrderTemplate, Guid> templates,
        IRepository<TransportOrder, Guid> orders,
        TransportOrderCommands commands,
        OrdersClock clock,
        IUnitOfWorkManager unitOfWorkManager,
        IFeatureChecker features)
    {
        _templates = templates;
        _orders = orders;
        _commands = commands;
        _clock = clock;
        _unitOfWorkManager = unitOfWorkManager;
        _features = features;
    }

    /// <summary>
    /// The features the hourly run needs for the current tenant (constitution rule 4, design 56 "Flag &amp; rollout"): the
    /// Transport app, its Orders node and the Templates node under it. Off means no new materialisation; the templates
    /// and the orders already created stay.
    /// </summary>
    public static readonly string[] RequiredFeatures = [AppFeatures.Transport, AppFeatures.TransportApp.Orders, AppFeatures.TransportApp.OrderTemplates];

    /// <summary>
    /// Creates the orders of <paramref name="dates"/> × <paramref name="copies"/> from the template; a stored template
    /// stamps the orders with its id (source Template) and records the last date, a transient one (copy order) leaves
    /// them without template (source Screen). Runs in the caller's unit of work.
    /// </summary>
    public async Task<MaterialisationResultDto> MaterialiseAsync(TransportOrderTemplate template, IReadOnlyList<DateOnly> dates, int copies, bool storedTemplate,
        IReadOnlyDictionary<(DateOnly Date, int CopyIndex), MaterialisationDetails>? details = null)
    {
        var result = new MaterialisationResultDto();
        var existing = storedTemplate
            ? (await _orders.GetListAsync(o => o.SourceTemplateId == template.Id && dates.Contains(o.OrderDate), includeDetails: false))
                .Select(o => (o.OrderDate, o.CopyIndex)).ToHashSet()
            : [];

        foreach (var date in dates)
        {
            for (var copy = 0; copy < copies; copy++)
            {
                if (existing.Contains((date, copy)))
                {
                    result.SkippedCount++;
                    continue;
                }

                var draft = TemplateMaterialiser.ToDraft(template, date, _clock.Zone);
                if (details != null && details.TryGetValue((date, copy), out var detail))
                {
                    draft = TemplateMaterialiser.WithDetails(draft, detail);
                }

                var order = await _commands.CreateAsync(draft, storedTemplate ? OrderSource.Template : OrderSource.Screen, null, storedTemplate ? template.Id : null, copy, confirm: true);
                result.CreatedOrderIds.Add(order.Id);
            }

            if (storedTemplate)
            {
                template.RecordMaterialised(date);
            }
        }

        return result;
    }

    /// <summary>
    /// The hourly run for the current tenant: every active recurring template, each in its own transaction. Nothing runs
    /// for a tenant whose <see cref="RequiredFeatures"/> are not all on (the worker has no user, so no attribute gates it).
    /// </summary>
    public async Task<int> RunForCurrentTenantAsync(CancellationToken cancellationToken = default)
    {
        if (!await _features.IsEnabledAsync(true, RequiredFeatures))
        {
            Logger.LogDebug("Template materialisation skipped: the Transport templates feature is off for this tenant.");
            return 0;
        }

        List<Guid> templateIds;
        using (var uow = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(), requiresNew: true))
        {
            templateIds = (await _templates.GetListAsync(t => t.IsActive && t.Schedule.Recurrence != TemplateRecurrence.None, includeDetails: false, cancellationToken))
                .Select(t => t.Id).ToList();
            await uow.CompleteAsync(cancellationToken);
        }

        var created = 0;
        var today = _clock.Today();
        foreach (var templateId in templateIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<DateOnly> dates = [];
            try
            {
                using var uow = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);
                var template = await _templates.GetAsync(templateId, includeDetails: true, cancellationToken);
                dates = template.Schedule.DueDates(today);
                if (dates.Count > 0)
                {
                    var result = await MaterialiseAsync(template, dates, 1, storedTemplate: true);
                    await _templates.UpdateAsync(template, autoSave: true, cancellationToken);
                    created += result.CreatedOrderIds.Count;
                }

                await uow.CompleteAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                /* One failing template never blocks the others (design 58). Ids only in the log. */
                if (ex is AbpDbConcurrencyException || await AllMaterialisedAsync(templateId, dates, cancellationToken))
                {
                    /* Another instance ran the same template at the same time and won (the unique index on template + date +
                     * copy, or the template's concurrency stamp): expected with several instances, not an error. The
                     * whole transaction rolled back, so the order number this attempt drew went back with it (the
                     * counter is gapless and commits with the order, design 50). */
                    Logger.LogInformation("Template {TemplateId} was materialised by another instance at the same time; this run's attempt was rolled back ({Error}).", templateId, ex.GetType().Name);
                }
                else
                {
                    Logger.LogError(ex, "Template {TemplateId} failed to materialise; the next run retries it.", templateId);
                }
            }
        }

        if (created > 0)
        {
            Logger.LogInformation("Template materialisation created {Count} orders.", created);
        }

        return created;
    }

    /// <summary>
    /// After a failed attempt: whether every due date of the template now has its order, i.e. the attempt lost the race to
    /// another instance. Read in a fresh unit of work; a failure here counts as "no" so the original error is logged.
    /// </summary>
    private async Task<bool> AllMaterialisedAsync(Guid templateId, IReadOnlyList<DateOnly> dates, CancellationToken cancellationToken)
    {
        if (dates.Count == 0)
        {
            return false;
        }

        try
        {
            using var uow = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(), requiresNew: true);
            var present = (await _orders.GetListAsync(o => o.SourceTemplateId == templateId && dates.Contains(o.OrderDate) && o.CopyIndex == 0, includeDetails: false, cancellationToken))
                .Select(o => o.OrderDate).ToHashSet();
            await uow.CompleteAsync(cancellationToken);
            return dates.All(present.Contains);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }
}

/// <summary>
/// The hourly job (design 58): visits every tenant and runs <see cref="TemplateMaterialisationRunner"/> for it. Registered
/// by <c>OrdersApplicationModule</c> when <c>Orders:TemplateMaterialisation:Enabled</c>; the host's background-worker
/// infrastructure (Hangfire-backed when configured) schedules it.
/// </summary>
public class TemplateMaterialisationWorker : AsyncPeriodicBackgroundWorkerBase
{
    public TemplateMaterialisationWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory, IOptions<TemplateMaterialisationOptions> options)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)Math.Clamp(options.Value.Period.TotalMilliseconds, 1_000, int.MaxValue);
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var tenantStore = workerContext.ServiceProvider.GetRequiredService<ITenantStore>();
        var currentTenant = workerContext.ServiceProvider.GetRequiredService<ICurrentTenant>();
        var tenants = await tenantStore.GetListAsync();
        foreach (var tenant in tenants)
        {
            workerContext.CancellationToken.ThrowIfCancellationRequested();
            try
            {
                using (currentTenant.Change(tenant.Id))
                {
                    var runner = workerContext.ServiceProvider.GetRequiredService<TemplateMaterialisationRunner>();
                    await runner.RunForCurrentTenantAsync(workerContext.CancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogError(ex, "Template materialisation failed for tenant {TenantId}; continuing with the remaining tenants.", tenant.Id);
            }
        }
    }
}
