using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTms.Platform.RecordUpdates;
using Volo.Abp;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.ExceptionHandling;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace OpenTms.Platform.DerivedValues;

/// <summary>
/// Flow 2 C and D (design 49): recalculates one record for one version in its tenant's own transaction, keeps the result
/// only if no newer save arrived meanwhile (the guarded completion), then publishes "recalculated" and tells the
/// screens. On a calculator failure the old values stay, the state says Failed, and the exception goes back to Hangfire
/// for a retry.
/// </summary>
public class DerivedValueCalculationJob : AsyncBackgroundJob<DerivedValueCalculationArgs>, ITransientDependency
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IDerivedValueStateStore _store;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ICurrentTenant _currentTenant;
    private readonly IDistributedEventBus _eventBus;
    private readonly IRecordUpdateNotifier _notifier;
    private readonly DerivedValueOptions _options;

    public DerivedValueCalculationJob(
        IServiceProvider serviceProvider,
        IDerivedValueStateStore store,
        IUnitOfWorkManager unitOfWorkManager,
        ICurrentTenant currentTenant,
        IDistributedEventBus eventBus,
        IRecordUpdateNotifier notifier,
        IOptions<DerivedValueOptions> options)
    {
        _serviceProvider = serviceProvider;
        _store = store;
        _unitOfWorkManager = unitOfWorkManager;
        _currentTenant = currentTenant;
        _eventBus = eventBus;
        _notifier = notifier;
        _options = options.Value;
    }

    public override async Task ExecuteAsync(DerivedValueCalculationArgs args)
    {
        var calculatorType = _options.Find(args.TargetType)?.CalculatorType;
        if (calculatorType == null)
        {
            /* A type that lost its calculator (a module removed it): nothing can be calculated; do not retry forever. */
            Logger.LogWarning("Derived values of {TargetType} {TargetId}: no calculator is registered; the job is dropped.", args.TargetType, args.TargetId);
            return;
        }

        /* The job trusts only ids from its arguments and runs explicitly in the requesting tenant (constitution rule 1). */
        using (_currentTenant.Change(args.TenantId))
        {
            Guid? stateId = null;
            try
            {
                using var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);

                var state = await _store.FindAsync(args.TargetType, args.TargetId);
                if (state == null || state.RequestedVersion != args.Version || state.CalculatedVersion >= args.Version)
                {
                    Logger.LogDebug("Derived values of {TargetType} {TargetId} v{Version}: superseded or done; skipped.", args.TargetType, args.TargetId, args.Version);
                    await unitOfWork.CompleteAsync();
                    return;
                }

                stateId = state.Id;
                var calculator = (IDerivedValueCalculator)_serviceProvider.GetRequiredService(calculatorType);
                await calculator.CalculateAsync(new DerivedValueCalculationContext(args.TargetType, args.TargetId, args.Version, default));

                /* Flush the calculator's writes first, so a failing write fails here and not after the guard. */
                await unitOfWork.SaveChangesAsync();
                if (!await _store.TryCompleteAsync(state.Id, args.Version))
                {
                    /* A save moved the record on while calculating: discard everything; the newer job writes (flow 2 C). */
                    await unitOfWork.RollbackAsync();
                    Logger.LogInformation("Derived values of {TargetType} {TargetId} v{Version}: superseded during calculation; discarded.", args.TargetType, args.TargetId, args.Version);
                    return;
                }

                await _eventBus.PublishAsync(new DerivedValuesRecalculatedEto { TargetType = args.TargetType, TargetId = args.TargetId, Version = args.Version });
                await _notifier.NotifyAsync(args.TargetType, args.TargetId, args.Version, DerivedValueStatus.UpToDate);
                await unitOfWork.CompleteAsync();
                Logger.LogInformation("Derived values of {TargetType} {TargetId} recalculated for v{Version}.", args.TargetType, args.TargetId, args.Version);
            }
            catch (Exception ex) when (stateId != null)
            {
                await MarkFailedAsync(args, stateId.Value, ex);
                throw;
            }
        }
    }

    private async Task MarkFailedAsync(DerivedValueCalculationArgs args, Guid stateId, Exception ex)
    {
        var code = ex is IHasErrorCode { Code: { Length: > 0 } errorCode } ? errorCode : ex.GetType().Name;
        Logger.LogWarning("Derived values of {TargetType} {TargetId} v{Version} failed with {ErrorCode}; Hangfire retries.", args.TargetType, args.TargetId, args.Version, code);

        using var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);
        if (await _store.TryFailAsync(stateId, args.Version, code))
        {
            await _notifier.NotifyAsync(args.TargetType, args.TargetId, args.Version, DerivedValueStatus.Failed);
        }

        await unitOfWork.CompleteAsync();
    }
}
