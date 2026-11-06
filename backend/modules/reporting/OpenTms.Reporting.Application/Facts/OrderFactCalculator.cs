using System;
using System.Threading.Tasks;
using OpenTms.Orders.Published;
using OpenTms.Platform.DerivedValues;
using OpenTms.Reporting.Features;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;

namespace OpenTms.Reporting.Facts;

/// <summary>
/// The calculator of <see cref="ReportingFactConsts.OrderFactTargetType"/> (design 75, flow 2): reads the order through Orders'
/// published reader and upserts its fact, or deletes the fact when the order is gone. Idempotent; runs in the job's transaction
/// under the record's tenant, and a newer save discards an older run.
/// </summary>
public class OrderFactCalculator : IDerivedValueCalculator, ITransientDependency
{
    private readonly IOrderFactReader _orders;
    private readonly IRepository<OrderFact, Guid> _facts;
    private readonly IFeatureChecker _features;
    private readonly ICurrentTenant _currentTenant;
    private readonly IClock _clock;

    public OrderFactCalculator(IOrderFactReader orders, IRepository<OrderFact, Guid> facts, IFeatureChecker features, ICurrentTenant currentTenant, IClock clock)
    {
        _orders = orders;
        _facts = facts;
        _features = features;
        _currentTenant = currentTenant;
        _clock = clock;
    }

    public async Task CalculateAsync(DerivedValueCalculationContext context)
    {
        /* A job requested just before the feature went off: without the feature no fact is written. */
        if (!await _features.IsEnabledAsync(ReportingFeatures.Metrics))
        {
            return;
        }

        var source = await _orders.GetAsync(context.TargetId, context.CancellationToken);
        var fact = await _facts.FindAsync(context.TargetId, cancellationToken: context.CancellationToken);
        if (source == null)
        {
            if (fact != null)
            {
                await _facts.DeleteAsync(fact, cancellationToken: context.CancellationToken);
            }

            return;
        }

        var isNew = fact == null;
        fact ??= new OrderFact(context.TargetId, _currentTenant.Id);
        var changed = fact.Apply(
            source.OrderDate,
            source.Status,
            source.CustomerRelationId,
            source.OrganizationUnitId,
            source.TransportOrderTypeId,
            source.CurrencyCode,
            source.RevenueAmount,
            source.CostOfRevenueAmount,
            source.MarginAmount,
            source.Co2Kg,
            _clock.Now);

        if (isNew)
        {
            await _facts.InsertAsync(fact, cancellationToken: context.CancellationToken);
        }
        else if (changed)
        {
            await _facts.UpdateAsync(fact, cancellationToken: context.CancellationToken);
        }
    }
}
