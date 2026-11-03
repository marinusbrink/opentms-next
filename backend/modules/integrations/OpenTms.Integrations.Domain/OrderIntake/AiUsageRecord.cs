using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities;

namespace OpenTms.Integrations.OrderIntake;

/// <summary>
/// One metered language-model call (design 72 "Metering", AP-05): integrations."AiUsageRecords" in the <b>host</b>
/// database, readable only by host operators. Deliberately not <c>IMultiTenant</c>: the row names the tenant it was
/// spent for in <see cref="TenantId"/> but is written with the host as the current tenant, so per-tenant costs can be
/// summed across tenants without disabling the tenant filter. Ids and figures only (constitution rule 5).
/// </summary>
public class AiUsageRecord : Entity<Guid>
{
    /// <summary>The tenant the call was made for (null for a host-side call).</summary>
    public Guid? TenantId { get; private set; }

    /// <summary><see cref="OrderIntakeConsts.LearnAgent"/>, <see cref="OrderIntakeConsts.ExtractAgent"/> or <see cref="OrderIntakeConsts.RepairAgent"/>.</summary>
    public string Agent { get; private set; } = default!;

    public string ModelId { get; private set; } = default!;

    public int InputTokens { get; private set; }

    public int OutputTokens { get; private set; }

    public int CacheReadTokens { get; private set; }

    public int CacheWriteTokens { get; private set; }

    /// <summary>From the price table in <c>Integrations:OrderIntake:Ai:Prices</c>, in millionths of a US dollar.</summary>
    public long CostMicroUsd { get; private set; }

    public Guid? InboundDocumentId { get; private set; }

    public DateTime At { get; private set; }

    protected AiUsageRecord()
    {
    }

    public AiUsageRecord(
        Guid id, Guid? tenantId, string agent, string modelId, int inputTokens, int outputTokens, int cacheReadTokens,
        int cacheWriteTokens, long costMicroUsd, Guid? inboundDocumentId, DateTime at)
        : base(id)
    {
        TenantId = tenantId;
        Agent = Check.NotNullOrWhiteSpace(agent, nameof(agent), OrderIntakeConsts.MaxAgentLength);
        ModelId = Check.NotNullOrWhiteSpace(modelId, nameof(modelId), OrderIntakeConsts.MaxModelIdLength);
        InputTokens = Check.Range(inputTokens, nameof(inputTokens), 0, int.MaxValue);
        OutputTokens = Check.Range(outputTokens, nameof(outputTokens), 0, int.MaxValue);
        CacheReadTokens = Check.Range(cacheReadTokens, nameof(cacheReadTokens), 0, int.MaxValue);
        CacheWriteTokens = Check.Range(cacheWriteTokens, nameof(cacheWriteTokens), 0, int.MaxValue);
        CostMicroUsd = Check.Range(costMicroUsd, nameof(costMicroUsd), 0, long.MaxValue);
        InboundDocumentId = inboundDocumentId;
        At = at;
    }
}
