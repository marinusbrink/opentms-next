using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;

namespace OpenTms.Orders.TransportOrders;

/// <summary>
/// The order list's filters, search and sort (design 58), shared by the Transport app's list and the Planboard's Orders
/// pane (design 70) so both read the same rows the same way.
/// </summary>
public static class TransportOrderListQuery
{
    public static IQueryable<TransportOrder> Filter(IQueryable<TransportOrder> query, List<TransportOrderStatus> status, TransportOrderView view, DateOnly? from, DateOnly? to, Guid? customerId, Guid? organizationUnitId, Guid? templateId, DateOnly today)
    {
        if (status.Count > 0)
        {
            query = query.Where(o => status.Contains(o.Status));
        }

        if (from != null)
        {
            query = query.Where(o => o.OrderDate >= from.Value);
        }

        if (to != null)
        {
            query = query.Where(o => o.OrderDate <= to.Value);
        }

        if (customerId != null)
        {
            query = query.Where(o => o.CustomerRelationId == customerId.Value);
        }

        if (organizationUnitId != null)
        {
            query = query.Where(o => o.OrganizationUnitId == organizationUnitId.Value);
        }

        if (templateId != null)
        {
            query = query.Where(o => o.SourceTemplateId == templateId.Value);
        }

        return view switch
        {
            TransportOrderView.Today => query.Where(o => o.OrderDate == today && (o.Status == TransportOrderStatus.Draft || o.Status == TransportOrderStatus.Open)),
            TransportOrderView.Tomorrow => query.Where(o => o.OrderDate == today.AddDays(1) && (o.Status == TransportOrderStatus.Draft || o.Status == TransportOrderStatus.Open)),
            TransportOrderView.ThisWeek => ThisWeek(query, today),
            TransportOrderView.Open => query.Where(o => o.Status == TransportOrderStatus.Open),
            TransportOrderView.Drafts => query.Where(o => o.Status == TransportOrderStatus.Draft),
            _ => query
        };
    }

    private static IQueryable<TransportOrder> ThisWeek(IQueryable<TransportOrder> query, DateOnly today)
    {
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var sunday = monday.AddDays(6);
        return query.Where(o => o.OrderDate >= monday && o.OrderDate <= sunday && (o.Status == TransportOrderStatus.Draft || o.Status == TransportOrderStatus.Open));
    }

    public static IQueryable<TransportOrder> Search(IQueryable<TransportOrder> query, string term)
        => query.Where(o =>
            (o.Number != null && o.Number.ToLower().Contains(term)) ||
            (o.CustomerReference != null && o.CustomerReference.ToLower().Contains(term)) ||
            o.CustomerName.ToLower().Contains(term) ||
            (o.FirstStopName != null && o.FirstStopName.ToLower().Contains(term)) ||
            (o.FirstStopCity != null && o.FirstStopCity.ToLower().Contains(term)) ||
            (o.LastStopName != null && o.LastStopName.ToLower().Contains(term)) ||
            (o.LastStopCity != null && o.LastStopCity.ToLower().Contains(term)) ||
            (o.TripNumbers != null && o.TripNumbers.ToLower().Contains(term)));

    public static IOrderedQueryable<TransportOrder> Sort(IQueryable<TransportOrder> query, string? sortBy, bool descending) => (sortBy?.ToLowerInvariant()) switch
    {
        null or "" => query.OrderByDescending(o => o.OrderDate).ThenByDescending(o => o.CreationTime),
        "number" => Order(query, o => o.Number, descending),
        "status" => Order(query, o => o.Status, descending),
        "orderdate" => Order(query, o => o.OrderDate, descending),
        "customername" => Order(query, o => o.CustomerName, descending),
        "customerreference" => Order(query, o => o.CustomerReference, descending),
        "firststopname" => Order(query, o => o.FirstStopName, descending),
        "firststopcity" => Order(query, o => o.FirstStopCity, descending),
        "firststopdate" => Order(query, o => o.FirstStopDate, descending),
        "laststopname" => Order(query, o => o.LastStopName, descending),
        "laststopcity" => Order(query, o => o.LastStopCity, descending),
        "laststopdate" => Order(query, o => o.LastStopDate, descending),
        "linecount" => Order(query, o => o.LineCount, descending),
        "revenueamount" => Order(query, o => o.RevenueAmount, descending),
        "costofrevenueamount" => Order(query, o => o.CostOfRevenueAmount, descending),
        "marginamount" => Order(query, o => o.MarginAmount, descending),
        "co2kg" => Order(query, o => o.Co2Kg, descending),
        "executionprogress" => Order(query, o => o.ExecutionProgress, descending),
        "tripnumbers" => Order(query, o => o.TripNumbers, descending),
        "source" => Order(query, o => o.Source, descending),
        "creationtime" => Order(query, o => o.CreationTime, descending),
        _ => throw new BusinessException(ReferenceDataErrorCodes.UnknownSortColumn).WithData("sortBy", sortBy)
    };

    private static IOrderedQueryable<TransportOrder> Order<TKey>(IQueryable<TransportOrder> query, Expression<Func<TransportOrder, TKey>> key, bool descending)
        => descending ? query.OrderByDescending(key) : query.OrderBy(key);
}
