using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace OpenTms.Integrations.OrderIntake.Ai;

/// <summary>
/// Meters one language-model call (design 72 "Metering", AP-05): an <see cref="AiUsageRecord"/> in the <b>host</b>
/// database, written with the host as the current tenant (the record is not multi-tenant and names its tenant itself) in
/// its own unit of work, so the figures survive a rolled-back pipeline run. Returns the figures for the AiCompleted step.
/// </summary>
public class AiUsageMeter : ITransientDependency
{
    private readonly IRepository<AiUsageRecord, Guid> _records;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ICurrentTenant _currentTenant;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IClock _clock;
    private readonly IOptions<OrderIntakeAiPriceOptions> _prices;

    public AiUsageMeter(
        IRepository<AiUsageRecord, Guid> records,
        IUnitOfWorkManager unitOfWorkManager,
        ICurrentTenant currentTenant,
        IGuidGenerator guidGenerator,
        IClock clock,
        IOptions<OrderIntakeAiPriceOptions> prices)
    {
        _records = records;
        _unitOfWorkManager = unitOfWorkManager;
        _currentTenant = currentTenant;
        _guidGenerator = guidGenerator;
        _clock = clock;
        _prices = prices;
    }

    public async Task<StepAiUsage> RecordAsync(string agent, LanguageModelResponse response, string requestedModelId, Guid? inboundDocumentId)
    {
        var modelId = Cut(string.IsNullOrWhiteSpace(response.ModelId) ? requestedModelId : response.ModelId, OrderIntakeConsts.MaxModelIdLength);
        var cost = _prices.Value.CostMicroUsd(modelId, response.Usage);
        var tenantId = _currentTenant.Id;

        /* Host database by design (design 72 "Tenant dimension", AP-05): the record carries the tenant id itself. */
        using (_currentTenant.Change(null))
        {
            using var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);
            await _records.InsertAsync(new AiUsageRecord(
                _guidGenerator.Create(), tenantId, agent, modelId, response.Usage.InputTokens, response.Usage.OutputTokens,
                response.Usage.CacheReadTokens, response.Usage.CacheWriteTokens, cost, inboundDocumentId, _clock.Now));
            await unitOfWork.CompleteAsync();
        }

        return new StepAiUsage(modelId, response.Usage.InputTokens + response.Usage.CacheReadTokens + response.Usage.CacheWriteTokens, response.Usage.OutputTokens, cost);
    }

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..max];
}
