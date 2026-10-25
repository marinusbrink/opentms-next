using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using OpenTms.Financial.Tariffs.Published;
using OpenTms.Orders.Permissions;
using OpenTms.Orders.TransportOrders.Approval;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;

namespace OpenTms.Orders.TransportOrders;

/// <summary>
/// The transport order's endpoints (design 58 "API contract"): the list with its views and filters, the editor's detail,
/// the saves per part (header, lines, stops, references, terms, trip request, attributes), the lifecycle commands, the
/// approval, the price preview and the stored summary. Every save ends with the matching event and a derived-value
/// request through <see cref="TransportOrderCommands"/>; legs and rate lines are read by the screen from their modules.
/// </summary>
[RequiresFeature(AppFeatures.Transport)]
public class TransportOrderAppService : OrdersAppServiceBase, ITransportOrderAppService
{
    private readonly IRepository<TransportOrder, Guid> _orders;
    private readonly TransportOrderCommands _commands;
    private readonly OrdersReferences _references;
    private readonly TransportOrderDtoMapper _mapper;
    private readonly PricingSnapshotBuilder _snapshots;
    private readonly IPricePreviewService _preview;
    private readonly ApprovalService _approval;
    private readonly OrdersClock _clock;

    public TransportOrderAppService(
        IRepository<TransportOrder, Guid> orders,
        TransportOrderCommands commands,
        OrdersReferences references,
        TransportOrderDtoMapper mapper,
        PricingSnapshotBuilder snapshots,
        IPricePreviewService preview,
        ApprovalService approval,
        OrdersClock clock)
    {
        _orders = orders;
        _commands = commands;
        _references = references;
        _mapper = mapper;
        _snapshots = snapshots;
        _preview = preview;
        _approval = approval;
        _clock = clock;
    }

    // ── reads ──────────────────────────────────────────────────────────────────

    [RequiresFeature(AppFeatures.TransportApp.Orders)]
    [Authorize(OrdersPermissions.TransportOrders.Default)]
    public virtual async Task<GridResponse<TransportOrderRowDto>> GetListAsync(TransportOrderListRequestDto input)
    {
        var filtered = ApplyFilters(await _orders.GetQueryableAsync(), input.Status, input.View, input.DateFrom, input.DateTo, input.CustomerRelationId, input.OrganizationUnitId, input.SourceTemplateId);
        var term = input.WildcardSearch?.Trim().ToLowerInvariant();
        var searched = string.IsNullOrEmpty(term) ? filtered : ApplySearch(filtered, term);

        var totalCount = await AsyncExecuter.LongCountAsync(filtered);
        var filteredCount = string.IsNullOrEmpty(term) ? totalCount : await AsyncExecuter.LongCountAsync(searched);
        var page = await AsyncExecuter.ToListAsync(ApplySort(searched, input.SortBy, input.Descending).Skip(input.StartRow).Take(input.EndRow - input.StartRow));

        return new GridResponse<TransportOrderRowDto> { Rows = await _mapper.ToRowsAsync(page), TotalCount = totalCount, FilteredCount = filteredCount };
    }

    [RequiresFeature(AppFeatures.TransportApp.Orders)]
    [Authorize(OrdersPermissions.TransportOrders.Default)]
    public virtual async Task<TransportOrderDto> GetAsync(Guid id) => await _mapper.ToDtoAsync(await LoadAsync(id));

    [RequiresFeature(AppFeatures.TransportApp.Orders)]
    [Authorize(OrdersPermissions.TransportOrders.Default)]
    public virtual async Task<TransportOrderSummaryDto> GetSummaryAsync(Guid id) => await _mapper.SummaryAsync(await _orders.GetAsync(id, includeDetails: false));

    // ── create and the saves per part ──────────────────────────────────────────

    [RequiresFeature(AppFeatures.TransportApp.Orders)]
    [Authorize(OrdersPermissions.TransportOrders.Create)]
    public virtual async Task<TransportOrderDto> CreateAsync(TransportOrderCreateDto input)
    {
        var header = TransportOrderInputMapper.Header(input, string.Empty, input.OrderDate ?? _clock.Today());
        var draft = new TransportOrderDraft(
            header,
            TransportOrderInputMapper.Terms(input.FinancialTerms),
            (input.DefaultStops ?? []).Select(TransportOrderInputMapper.DefaultStop).ToList(),
            input.Lines.Select(l => TransportOrderInputMapper.Line(l, null)).ToList(),
            input.References.Select(TransportOrderInputMapper.Reference).ToList(),
            TransportOrderInputMapper.TripRequest(input.TripRequest),
            input.Attributes);
        var order = await _commands.CreateAsync(draft, input.Source, input.DraftConfidence, null, 0, input.Confirm);
        return await ReloadAsync(order.Id);
    }

    [RequiresFeature(AppFeatures.TransportApp.Orders)]
    [Authorize(OrdersPermissions.TransportOrders.Update)]
    public virtual async Task<TransportOrderDto> UpdateAsync(Guid id, TransportOrderUpdateDto input)
    {
        var order = await LoadForUpdateAsync(id, input.ConcurrencyStamp);
        var type = await _references.GetOrderTypeAsync(input.TransportOrderTypeId);
        var customerName = await _references.RelationNameAsync(input.CustomerRelationId, "customerRelationId");
        if (input.CustomerContactPersonId != null)
        {
            await _references.NamesAsync(MasterData.Published.MasterDataReferenceKind.ContactPerson, [input.CustomerContactPersonId.Value], require: true, "customerContactPersonId");
        }

        var pricingChanged = order.CustomerRelationId != input.CustomerRelationId || order.OrderDate != (input.OrderDate ?? order.OrderDate);
        order.SetHeader(TransportOrderInputMapper.Header(input, customerName, input.OrderDate ?? order.OrderDate), type.Numbering);
        await _orders.UpdateAsync(order, autoSave: true);
        if (pricingChanged)
        {
            await _commands.PublishTermsChangedAsync(order);
        }
        else
        {
            await _commands.RequestSummaryAsync(order, "HeaderChanged");
        }

        return await ReloadAsync(id);
    }

    [RequiresFeature(AppFeatures.TransportApp.Orders)]
    [Authorize(OrdersPermissions.TransportOrders.Update)]
    public virtual async Task<TransportOrderDto> UpdateLinesAsync(Guid id, TransportOrderLinesUpdateDto input)
    {
        var order = await LoadForUpdateAsync(id, input.ConcurrencyStamp);
        var pattern = order.StopsFromPattern();
        var lines = input.Lines.Select(l => TransportOrderInputMapper.Line(l, l.Id == null ? pattern : null)).ToList();
        await ValidateLinesAsync(lines);
        var changes = order.SetLines(lines, GuidGenerator.Create);
        await _commands.EnsureStopsUnlockedAsync(order, changes.StopChangedLineIds.Concat(changes.RemovedLineIds));
        await _orders.UpdateAsync(order, autoSave: true);
        await _commands.PublishLinesChangedAsync(order, changes);
        return await ReloadAsync(id);
    }

    [RequiresFeature(AppFeatures.TransportApp.Orders)]
    [Authorize(OrdersPermissions.TransportOrders.Update)]
    public virtual async Task<TransportOrderDto> AddLineAsync(Guid id, TransportOrderLineInputDto input)
    {
        var order = await LoadAsync(id);
        order.EnsureEditable();
        input.Id = null;
        var line = TransportOrderInputMapper.Line(input, order.StopsFromPattern());
        await ValidateLinesAsync([line]);
        var added = order.AddLine(line, GuidGenerator.Create);
        await _orders.UpdateAsync(order, autoSave: true);
        var changes = new LineChanges();
        changes.LineIds.Add(added.Id);
        changes.StopChangedLineIds.Add(added.Id);
        await _commands.PublishLinesChangedAsync(order, changes);
        return await ReloadAsync(id);
    }

    [RequiresFeature(AppFeatures.TransportApp.Orders)]
    [Authorize(OrdersPermissions.TransportOrders.Update)]
    public virtual async Task<TransportOrderDto> UpdateLineAsync(Guid id, Guid lineId, TransportOrderLineInputDto input)
    {
        var order = await LoadAsync(id);
        order.EnsureEditable();
        var existing = order.GetLine(lineId);
        input.Id = lineId;
        var line = TransportOrderInputMapper.Line(input, existing.Stops.OrderBy(s => s.Position).Select(s => s.ToInput()).ToList());
        await ValidateLinesAsync([line]);
        var stopsChanged = existing.Set(existing.Position, line, GuidGenerator.Create);
        var changes = new LineChanges();
        changes.LineIds.Add(lineId);
        if (stopsChanged)
        {
            changes.StopChangedLineIds.Add(lineId);
            await _commands.EnsureStopsUnlockedAsync(order, [lineId]);
        }

        await _orders.UpdateAsync(order, autoSave: true);
        await _commands.PublishLinesChangedAsync(order, changes);
        return await ReloadAsync(id);
    }

    [RequiresFeature(AppFeatures.TransportApp.Orders)]
    [Authorize(OrdersPermissions.TransportOrders.Update)]
    public virtual async Task<TransportOrderDto> DeleteLineAsync(Guid id, Guid lineId)
    {
        var order = await LoadAsync(id);
        order.EnsureEditable();
        order.GetLine(lineId);
        await _commands.EnsureStopsUnlockedAsync(order, [lineId]);
        order.RemoveLine(lineId);
        await _orders.UpdateAsync(order, autoSave: true);
        var changes = new LineChanges();
        changes.RemovedLineIds.Add(lineId);
        await _commands.PublishLinesChangedAsync(order, changes);
        return await ReloadAsync(id);
    }

    [RequiresFeature(AppFeatures.TransportApp.Orders)]
    [Authorize(OrdersPermissions.TransportOrders.Update)]
    public virtual async Task<TransportOrderDto> UpdateDefaultStopsAsync(Guid id, TransportOrderDefaultStopsUpdateDto input)
    {
        var order = await LoadForUpdateAsync(id, input.ConcurrencyStamp);
        var stops = input.DefaultStops.Select(TransportOrderInputMapper.DefaultStop).ToList();
        await _references.ValidatePatternAsync(stops.Select(s => (s.WaypointTypeId, s.RelationId, s.RelationAddressId, s.TimeSlotId)));
        order.SetDefaultStops(stops);
        await _orders.UpdateAsync(order, autoSave: true);
        await _commands.RequestSummaryAsync(order, "PatternChanged");
        return await ReloadAsync(id);
    }

    [RequiresFeature(AppFeatures.TransportApp.Orders)]
    [Authorize(OrdersPermissions.TransportOrders.Update)]
    public virtual async Task<TransportOrderDto> UpdateReferencesAsync(Guid id, TransportOrderReferencesUpdateDto input)
    {
        var order = await LoadForUpdateAsync(id, input.ConcurrencyStamp);
        order.SetReferences(input.References.Select(TransportOrderInputMapper.Reference).ToList(), GuidGenerator.Create);
        await _orders.UpdateAsync(order, autoSave: true);
        await _commands.RequestSummaryAsync(order, "ReferencesChanged");
        return await ReloadAsync(id);
    }

    [RequiresFeature(AppFeatures.TransportApp.Orders)]
    [Authorize(OrdersPermissions.TransportOrders.Update)]
    public virtual async Task<TransportOrderDto> UpdateFinancialTermsAsync(Guid id, TransportOrderFinancialTermsUpdateDto input)
    {
        var order = await LoadForUpdateAsync(id, input.ConcurrencyStamp);
        var terms = TransportOrderInputMapper.Terms(input);
        await _references.EnsureFinancialTermsAsync(terms);
        order.SetFinancialTerms(terms, GuidGenerator.Create);
        await _orders.UpdateAsync(order, autoSave: true);
        await _commands.PublishTermsChangedAsync(order);
        return await ReloadAsync(id);
    }

    [RequiresFeature(AppFeatures.TransportApp.Orders)]
    [Authorize(OrdersPermissions.TransportOrders.Update)]
    public virtual async Task<TransportOrderDto> UpdateTripRequestAsync(Guid id, TransportOrderTripRequestUpdateDto input)
    {
        var order = await LoadForUpdateAsync(id, input.ConcurrencyStamp);
        var request = TransportOrderInputMapper.TripRequest(input.TripRequest);
        if (request != null)
        {
            await _references.ValidateTripRequestAsync(request.DepartureRelationId, request.DepartureAddressId, request.ArrivalRelationId, request.ArrivalAddressId,
                request.CarrierRelationId, request.VehicleCombinationId, request.VehicleIds, request.DriverIds);
        }

        order.SetTripRequest(request);
        await _orders.UpdateAsync(order, autoSave: true);
        await _commands.RequestSummaryAsync(order, "TripRequestChanged");
        return await ReloadAsync(id);
    }

    [RequiresFeature(AppFeatures.TransportApp.Orders)]
    [Authorize(OrdersPermissions.TransportOrders.Update)]
    public virtual async Task<TransportOrderDto> UpdateAttributesAsync(Guid id, TransportOrderAttributesUpdateDto input)
    {
        var order = await LoadForUpdateAsync(id, input.ConcurrencyStamp);
        await _references.EnsureAttributeKeysAsync(OrderAttributeTarget.Order, [input.Attributes]);
        order.SetAttributes(input.Attributes);
        await _orders.UpdateAsync(order, autoSave: true);
        await _commands.RequestSummaryAsync(order, "AttributesChanged");
        return await ReloadAsync(id);
    }

    // ── the lifecycle ──────────────────────────────────────────────────────────

    [RequiresFeature(AppFeatures.TransportApp.Orders)]
    [Authorize(OrdersPermissions.TransportOrders.Confirm)]
    public virtual async Task<TransportOrderDto> ConfirmAsync(Guid id)
    {
        var order = await LoadAsync(id);
        await _commands.ConfirmAsync(order);
        return await ReloadAsync(id);
    }

    [RequiresFeature(AppFeatures.TransportApp.Orders)]
    [Authorize(OrdersPermissions.TransportOrders.Cancel)]
    public virtual async Task<TransportOrderDto> CancelAsync(Guid id)
    {
        var order = await LoadAsync(id);
        await _commands.EnsureCancellableAsync(order);
        order.Cancel();
        await _orders.UpdateAsync(order, autoSave: true);
        await _commands.PublishCancelledAsync(order);
        return await ReloadAsync(id);
    }

    [RequiresFeature(AppFeatures.TransportApp.Orders)]
    [Authorize(OrdersPermissions.TransportOrders.Approve)]
    public virtual async Task<TransportOrderDto> ApproveAsync(Guid id, TransportOrderApproveDto input)
    {
        var order = await LoadAsync(id);
        var evaluation = (await _approval.EvaluateAsync([order])).Single();
        EnsureFresh([evaluation], input.Hash);
        await ApproveEvaluatedAsync(order, evaluation);
        return await ReloadAsync(id);
    }

    [RequiresFeature(AppFeatures.TransportApp.Orders)]
    [Authorize(OrdersPermissions.TransportOrders.Revise)]
    public virtual async Task<TransportOrderDto> ReviseAsync(Guid id, TransportOrderReviseDto input)
    {
        var order = await LoadAsync(id);
        order.Revise(input.Reason, CurrentUser.Id, Clock.Now, GuidGenerator.Create());
        await _orders.UpdateAsync(order, autoSave: true);
        await _commands.PublishStatusChangedAsync(order);
        return await ReloadAsync(id);
    }

    [RequiresFeature(AppFeatures.TransportApp.Orders)]
    [Authorize(OrdersPermissions.TransportOrders.Archive)]
    public virtual async Task<TransportOrderDto> ArchiveAsync(Guid id)
    {
        var order = await LoadAsync(id);
        order.Archive();
        await _orders.UpdateAsync(order, autoSave: true);
        await _commands.PublishStatusChangedAsync(order);
        return await ReloadAsync(id);
    }

    [RequiresFeature(AppFeatures.TransportApp.Orders)]
    [Authorize(OrdersPermissions.TransportOrders.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var order = await LoadAsync(id);
        await _commands.EnsureDeletableAsync(order);
        await _orders.DeleteAsync(order, autoSave: true);
        await _commands.PublishCancelledAsync(order);
    }

    [RequiresFeature(AppFeatures.TransportApp.Orders)]
    [Authorize(OrdersPermissions.TransportOrders.Delete)]
    public virtual async Task<ReferenceDataBulkDeleteResultDto> BulkDeleteAsync(TransportOrderBulkDeleteRequestDto input)
    {
        List<TransportOrder> targets;
        var selection = input.Selection;
        if (string.Equals(selection.Mode, "FilterBased", StringComparison.OrdinalIgnoreCase))
        {
            var query = ApplyFilters(await _orders.GetQueryableAsync(), input.Status, input.View, null, null, null, null, null);
            var term = selection.FilterRequest?.WildcardSearch?.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(term))
            {
                query = ApplySearch(query, term);
            }

            if (selection.ExcludedIds.Count > 0)
            {
                var excluded = selection.ExcludedIds;
                query = query.Where(o => !excluded.Contains(o.Id));
            }

            targets = await AsyncExecuter.ToListAsync(ApplySort(query, null, false).Take(ReferenceDataConsts.MaxBulkDeleteRows + 1));
        }
        else
        {
            var ids = selection.ExplicitIds.Distinct().ToList();
            if (ids.Count > ReferenceDataConsts.MaxBulkDeleteRows)
            {
                throw new BusinessException(ReferenceDataErrorCodes.TooManyRows).WithData("max", ReferenceDataConsts.MaxBulkDeleteRows);
            }

            targets = ids.Count == 0 ? [] : await _orders.GetListAsync(o => ids.Contains(o.Id), includeDetails: false);
        }

        if (targets.Count > ReferenceDataConsts.MaxBulkDeleteRows)
        {
            throw new BusinessException(ReferenceDataErrorCodes.TooManyRows).WithData("max", ReferenceDataConsts.MaxBulkDeleteRows);
        }

        var result = new ReferenceDataBulkDeleteResultDto();
        foreach (var order in targets)
        {
            try
            {
                await _commands.EnsureDeletableAsync(order);
            }
            catch (BusinessException ex)
            {
                result.SkippedRows.Add(new ReferenceDataSkippedRowDto { Id = order.Id, Name = order.Number ?? order.Id.ToString("N")[..8], Reason = L[ex.Code!] });
                continue;
            }

            await _orders.DeleteAsync(order);
            await _commands.PublishCancelledAsync(order);
            result.DeletedCount++;
        }

        Logger.LogInformation("Bulk delete of transport orders: {Deleted} deleted, {Skipped} skipped.", result.DeletedCount, result.SkippedRows.Count);
        return result;
    }

    // ── approval ───────────────────────────────────────────────────────────────

    [RequiresFeature(AppFeatures.TransportApp.Orders)]
    [Authorize(OrdersPermissions.TransportOrders.Approve)]
    public virtual async Task<ApprovalValidationDto> ValidateApprovalAsync(ApprovalValidateRequestDto input)
    {
        var orders = await LoadManyAsync(input.Ids);
        var evaluations = await _approval.EvaluateAsync(orders);
        return new ApprovalValidationDto { Orders = evaluations.Select(e => ToDto(e, orders)).ToList(), Hash = ApprovalEvaluator.Hash(evaluations) };
    }

    [RequiresFeature(AppFeatures.TransportApp.Orders)]
    [Authorize(OrdersPermissions.TransportOrders.Approve)]
    public virtual async Task<ApprovalOutcomeDto> ApproveManyAsync(ApprovalApproveRequestDto input)
    {
        var orders = await LoadManyAsync(input.Ids);
        var evaluations = await _approval.EvaluateAsync(orders);
        EnsureFresh(evaluations, input.Hash);

        var outcome = new ApprovalOutcomeDto();
        foreach (var evaluation in evaluations)
        {
            var order = orders.Single(o => o.Id == evaluation.OrderId);
            if (!evaluation.CanApprove || order.Status != TransportOrderStatus.Open)
            {
                outcome.Skipped.Add(ToDto(evaluation, orders));
                continue;
            }

            await ApproveEvaluatedAsync(order, evaluation);
            outcome.ApprovedIds.Add(order.Id);
        }

        return outcome;
    }

    private async Task ApproveEvaluatedAsync(TransportOrder order, OrderApproval evaluation)
    {
        if (!evaluation.CanApprove)
        {
            var error = evaluation.Results.First(r => r.Severity == ApprovalSeverity.Error);
            throw new BusinessException(OrdersErrorCodes.ApprovalHasErrors).WithData("rule", error.Rule.ToString());
        }

        order.Approve(evaluation.ProposedStatus);
        await _orders.UpdateAsync(order, autoSave: true);
        await _commands.PublishStatusChangedAsync(order);
    }

    private static void EnsureFresh(IReadOnlyList<OrderApproval> evaluations, string hash)
    {
        if (!string.Equals(ApprovalEvaluator.Hash(evaluations), hash?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException(OrdersErrorCodes.ApprovalResultsStale);
        }
    }

    private OrderApprovalDto ToDto(OrderApproval evaluation, IReadOnlyList<TransportOrder> orders) => new()
    {
        OrderId = evaluation.OrderId,
        Number = orders.FirstOrDefault(o => o.Id == evaluation.OrderId)?.Number,
        CurrentStatus = evaluation.CurrentStatus,
        ProposedStatus = evaluation.ProposedStatus,
        CanApprove = evaluation.CanApprove,
        Results = evaluation.Results.Select(r => new ApprovalResultDto
        {
            Rule = r.Rule,
            Severity = r.Severity,
            ProposedStatus = r.ProposedStatus,
            MessageKey = r.MessageKey,
            Message = r.Data.Aggregate(L[r.MessageKey].Value, (text, pair) => text.Replace("{" + pair.Key + "}", pair.Value)),
            Data = new Dictionary<string, string>(r.Data)
        }).ToList()
    };

    // ── preview ────────────────────────────────────────────────────────────────

    [RequiresFeature(AppFeatures.TransportApp.Orders)]
    [Authorize(OrdersPermissions.TransportOrders.Default)]
    public virtual async Task<PricePreviewDto> PreviewPricesAsync(Guid id, PricePreviewRequestDto input)
    {
        var order = await LoadAsync(id);
        var pattern = order.StopsFromPattern();
        var lines = input.Lines == null
            ? order.Lines.OrderBy(l => l.Position).Select(l => l.ToInput()).ToList()
            : input.Lines.Select(l => TransportOrderInputMapper.Line(l, pattern)).ToList();
        var terms = input.FinancialTerms == null ? order.FinancialTermsInput() : TransportOrderInputMapper.Terms(input.FinancialTerms);
        var header = order.HeaderFor() with { OrderDate = input.OrderDate ?? order.OrderDate, CustomerRelationId = input.CustomerRelationId ?? order.CustomerRelationId };
        var snapshot = await _snapshots.BuildAsync(order.Id, order.Status, header, terms, lines);
        return await _preview.PreviewAsync(snapshot);
    }

    // ── queries ────────────────────────────────────────────────────────────────

    private IQueryable<TransportOrder> ApplyFilters(IQueryable<TransportOrder> query, List<TransportOrderStatus> status, TransportOrderView view, DateOnly? from, DateOnly? to, Guid? customerId, Guid? organizationUnitId, Guid? templateId)
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

        var today = _clock.Today();
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

    private static IQueryable<TransportOrder> ApplySearch(IQueryable<TransportOrder> query, string term)
        => query.Where(o =>
            (o.Number != null && o.Number.ToLower().Contains(term)) ||
            (o.CustomerReference != null && o.CustomerReference.ToLower().Contains(term)) ||
            o.CustomerName.ToLower().Contains(term) ||
            (o.FirstStopName != null && o.FirstStopName.ToLower().Contains(term)) ||
            (o.FirstStopCity != null && o.FirstStopCity.ToLower().Contains(term)) ||
            (o.LastStopName != null && o.LastStopName.ToLower().Contains(term)) ||
            (o.LastStopCity != null && o.LastStopCity.ToLower().Contains(term)) ||
            (o.TripNumbers != null && o.TripNumbers.ToLower().Contains(term)));

    private static IOrderedQueryable<TransportOrder> ApplySort(IQueryable<TransportOrder> query, string? sortBy, bool descending) => (sortBy?.ToLowerInvariant()) switch
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

    // ── helpers ────────────────────────────────────────────────────────────────

    private Task<TransportOrder> LoadAsync(Guid id) => _orders.GetAsync(id, includeDetails: true);

    private async Task<TransportOrder> LoadForUpdateAsync(Guid id, string concurrencyStamp)
    {
        var order = await LoadAsync(id);
        order.EnsureEditable();
        /* ABP compares this stamp with the stored one on save and throws AbpDbConcurrencyException (409). */
        order.ConcurrencyStamp = concurrencyStamp;
        return order;
    }

    private async Task<List<TransportOrder>> LoadManyAsync(List<Guid> ids)
    {
        var keys = ids.Distinct().ToList();
        if (keys.Count > ReferenceDataConsts.MaxBulkDeleteRows)
        {
            throw new BusinessException(ReferenceDataErrorCodes.TooManyRows).WithData("max", ReferenceDataConsts.MaxBulkDeleteRows);
        }

        return keys.Count == 0 ? [] : await _orders.GetListAsync(o => keys.Contains(o.Id), includeDetails: true);
    }

    private async Task<TransportOrderDto> ReloadAsync(Guid id)
    {
        if (UnitOfWorkManager.Current != null)
        {
            await UnitOfWorkManager.Current.SaveChangesAsync();
        }

        return await _mapper.ToDtoAsync(await LoadAsync(id));
    }

    private Task ValidateLinesAsync(IReadOnlyList<LineInput> lines)
        => _references.ValidateLinesAsync(lines.Select(l => (l.Freight, l.Stops.Select(s => (s.WaypointTypeId, (Guid?)s.RelationId, (Guid?)s.RelationAddressId, s.TimeSlotId, s.ContactPersonId)).ToList(), l.RequiredTransportCharacteristicIds, l.Attributes)).ToList());
}
