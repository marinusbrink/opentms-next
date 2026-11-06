using System;
using OpenTms.Orders.TransportOrders;
using OpenTms.Reporting.Metrics;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Reporting.Facts;

/// <summary>
/// One order's numbers, prepared for grouping (design 75, AD-23): a derived record owned by Reporting, written only by its
/// calculator from Orders' published reader. Its id is the order's id. It holds ids and numbers, no names.
/// </summary>
public class OrderFact : BasicAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public DateOnly OrderDate { get; private set; }

    public DateOnly OrderWeek { get; private set; }

    public DateOnly OrderMonth { get; private set; }

    public TransportOrderStatus Status { get; private set; }

    public Guid CustomerRelationId { get; private set; }

    public Guid? OrganizationUnitId { get; private set; }

    public Guid TransportOrderTypeId { get; private set; }

    public string CurrencyCode { get; private set; } = string.Empty;

    public decimal? RevenueAmount { get; private set; }

    public decimal? CostOfRevenueAmount { get; private set; }

    public decimal? MarginAmount { get; private set; }

    public decimal? Co2Kg { get; private set; }

    public DateTime FactUpdatedAt { get; private set; }

    protected OrderFact()
    {
    }

    public OrderFact(Guid id, Guid? tenantId)
        : base(id)
    {
        TenantId = tenantId;
    }

    /// <summary>
    /// Writes the source's current values; returns whether anything changed, and only then moves <see cref="FactUpdatedAt"/>,
    /// so a duplicate event changes nothing (constitution rule 3).
    /// </summary>
    public bool Apply(
        DateOnly orderDate,
        TransportOrderStatus status,
        Guid customerRelationId,
        Guid? organizationUnitId,
        Guid transportOrderTypeId,
        string currencyCode,
        decimal? revenueAmount,
        decimal? costOfRevenueAmount,
        decimal? marginAmount,
        decimal? co2Kg,
        DateTime now)
    {
        if (FactUpdatedAt != default
            && OrderDate == orderDate
            && Status == status
            && CustomerRelationId == customerRelationId
            && OrganizationUnitId == organizationUnitId
            && TransportOrderTypeId == transportOrderTypeId
            && CurrencyCode == currencyCode
            && RevenueAmount == revenueAmount
            && CostOfRevenueAmount == costOfRevenueAmount
            && MarginAmount == marginAmount
            && Co2Kg == co2Kg)
        {
            return false;
        }

        OrderDate = orderDate;
        OrderWeek = MetricPeriods.WeekOf(orderDate);
        OrderMonth = MetricPeriods.MonthOf(orderDate);
        Status = status;
        CustomerRelationId = customerRelationId;
        OrganizationUnitId = organizationUnitId;
        TransportOrderTypeId = transportOrderTypeId;
        CurrencyCode = currencyCode;
        RevenueAmount = revenueAmount;
        CostOfRevenueAmount = costOfRevenueAmount;
        MarginAmount = marginAmount;
        Co2Kg = co2Kg;
        FactUpdatedAt = now;
        return true;
    }
}
