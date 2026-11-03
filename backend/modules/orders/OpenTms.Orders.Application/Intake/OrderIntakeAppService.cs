using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using OpenTms.MasterData.Published;
using OpenTms.Orders.Events;
using OpenTms.Orders.Permissions;
using OpenTms.Orders.TransportOrders;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Features;

namespace OpenTms.Orders.Intake;

/// <summary>
/// The review queue of the Transport app's Intake view (design 72 "Review"), route root <c>/api/orders/order-intake</c>.
/// Gated on the Transport app, its Intake node and the tenant feature <see cref="IntakeConsts.OrderIntakeFeature"/>
/// (constitution rule 4); every read is tenant-filtered. Resolve re-runs the resolver with the clerk's picks, learns
/// picks on open fields as aliases and creates the order; reject closes the intake. Both publish their fact.
/// </summary>
[RequiresFeature(AppFeatures.Transport, AppFeatures.TransportApp.Intake, IntakeConsts.OrderIntakeFeature, RequiresAll = true)]
public class OrderIntakeAppService : OrdersAppServiceBase, IOrderIntakeAppService
{
    private readonly IOrderIntakeRepository _intakes;
    private readonly IRepository<IntakeAlias, Guid> _aliases;
    private readonly IRepository<TransportOrder, Guid> _orders;
    private readonly IRepository<TransportOrderType, Guid> _orderTypes;
    private readonly IRepository<WaypointType, Guid> _waypointTypes;
    private readonly OrdersReferences _references;
    private readonly OrderIntakeProcessor _processor;
    private readonly OrdersClock _clock;
    private readonly IDistributedEventBus _events;

    public OrderIntakeAppService(
        IOrderIntakeRepository intakes,
        IRepository<IntakeAlias, Guid> aliases,
        IRepository<TransportOrder, Guid> orders,
        IRepository<TransportOrderType, Guid> orderTypes,
        IRepository<WaypointType, Guid> waypointTypes,
        OrdersReferences references,
        OrderIntakeProcessor processor,
        OrdersClock clock,
        IDistributedEventBus events)
    {
        _intakes = intakes;
        _aliases = aliases;
        _orders = orders;
        _orderTypes = orderTypes;
        _waypointTypes = waypointTypes;
        _references = references;
        _processor = processor;
        _clock = clock;
        _events = events;
    }

    // ── the queue ──────────────────────────────────────────────────────────────

    [Authorize(OrdersPermissions.OrderIntakes.Default)]
    public virtual async Task<GridResponse<OrderIntakeRowDto>> GetListAsync(OrderIntakeListRequestDto input)
    {
        var query = await _intakes.GetQueryableAsync();
        var statuses = input.Status.Distinct().ToList();
        if (statuses.Count > 0)
        {
            query = query.Where(i => statuses.Contains(i.Status));
        }

        if (input.ReceivedFrom is { } from)
        {
            var start = StartOfDayUtc(from);
            query = query.Where(i => i.CreationTime >= start);
        }

        if (input.ReceivedTo is { } to)
        {
            var end = StartOfDayUtc(to.AddDays(1));
            query = query.Where(i => i.CreationTime < end);
        }

        var term = input.WildcardSearch?.Trim();
        /* ILIKE on the raw columns with the trigram indexes (PostgreSQL); see IOrderIntakeRepository. */
        var searched = string.IsNullOrEmpty(term) ? query : await _intakes.WhereSearchMatchesAsync(query, term);

        var totalCount = await AsyncExecuter.LongCountAsync(query);
        var filteredCount = string.IsNullOrEmpty(term) ? totalCount : await AsyncExecuter.LongCountAsync(searched);
        /* Projected in the query: the page reads the list's columns only, never the jsonb payloads and resolution. */
        var rows = await AsyncExecuter.ToListAsync(Sort(searched, input.SortBy, input.SortDir)
            .Skip(input.StartRow)
            .Take(input.EndRow - input.StartRow)
            .Select(i => new OrderIntakeRowDto
            {
                Id = i.Id,
                ReceivedAt = i.CreationTime,
                Channel = i.Channel,
                Source = i.Source,
                SenderAddress = i.SenderAddress,
                Subject = i.Subject,
                FileName = i.FileName,
                CustomerReference = i.CustomerReference,
                CustomerRelationId = i.CustomerRelationId,
                CustomerName = i.CustomerName,
                Status = i.Status,
                OpenFieldCount = i.OpenFieldCount,
                TransportOrderId = i.TransportOrderId
            }));
        var numbers = await OrderNumbersAsync(rows.Select(r => r.TransportOrderId).OfType<Guid>());
        foreach (var row in rows)
        {
            row.OrderNumber = row.TransportOrderId is { } id ? numbers.GetValueOrDefault(id) : null;
        }

        return new GridResponse<OrderIntakeRowDto>
        {
            Rows = rows,
            TotalCount = totalCount,
            FilteredCount = filteredCount
        };
    }

    [Authorize(OrdersPermissions.OrderIntakes.Default)]
    public virtual async Task<OrderIntakeDetailDto> GetAsync(Guid id) => await ToDetailAsync(await _intakes.GetAsync(id));

    // ── commands ───────────────────────────────────────────────────────────────

    [Authorize(OrdersPermissions.OrderIntakes.Resolve)]
    public virtual async Task<OrderIntakeDetailDto> ResolveAsync(Guid id, OrderIntakeResolveDto input)
    {
        if (input.Confirm)
        {
            /* "Create and confirm" confirms an order: it needs the permission confirming an order needs (PO decision on design 72). */
            await AuthorizationService.CheckAsync(OrdersPermissions.TransportOrders.Confirm);
        }

        var intake = await _intakes.GetAsync(id);
        intake.EnsureInReview();

        var received = OrderIntakeProcessor.Deserialize(intake.Payload);
        var effective = received;
        var corrected = input.ReviewedPayload != null && !OrderIntakeProcessor.SameValues(input.ReviewedPayload, received);
        if (corrected)
        {
            effective = input.ReviewedPayload!;
        }

        var result = await _processor.ResolveAsync(intake, effective, input.Picks);
        var blocking = result.Resolution.BlockingFieldPaths();
        if (blocking.Count == 0)
        {
            /* What is still unknown after the picks is created in Master Data (build note 26); a refused write blocks. */
            await _processor.CreateMasterDataAsync(intake, result);
            blocking = result.Resolution.BlockingFieldPaths();
        }

        if (blocking.Count > 0)
        {
            /* Nothing is kept: the clerk completes the picks and tries again (the transaction rolls back, created rows too). */
            throw new BusinessException(OrdersErrorCodes.IntakeUnresolved).WithData("fields", string.Join(", ", blocking));
        }

        if (corrected)
        {
            intake.Review(OrderIntakeProcessor.Serialize(effective));
        }

        await _processor.ApplyAsync(intake, effective, result);
        await _processor.LearnAliasesAsync(result);
        await _processor.CreateOrderAsync(intake, effective, result, input.Confirm, confirmOnlyWhenComplete: false);
        Logger.LogInformation("Order intake {IntakeId} resolved by a user ({Picks} picks, corrected: {Corrected}).", intake.Id, input.Picks.Count, corrected);
        return await ToDetailAsync(intake);
    }

    [Authorize(OrdersPermissions.OrderIntakes.Reject)]
    public virtual async Task<OrderIntakeDetailDto> RejectAsync(Guid id, OrderIntakeRejectDto input)
    {
        var intake = await _intakes.GetAsync(id);
        intake.Reject(input.Reason);
        await _intakes.UpdateAsync(intake, autoSave: true);
        await _events.PublishAsync(new OrderIntakeRejectedEto { IntakeId = intake.Id, SourceDocumentId = intake.SourceDocumentId, IntakeIndex = intake.IntakeIndex });
        Logger.LogInformation("Order intake {IntakeId} rejected.", intake.Id);
        return await ToDetailAsync(intake);
    }

    // ── aliases ────────────────────────────────────────────────────────────────

    [Authorize(OrdersPermissions.OrderIntakes.Default)]
    public virtual async Task<GridResponse<IntakeAliasDto>> GetAliasesAsync(IntakeAliasListRequestDto input)
    {
        var query = await _aliases.GetQueryableAsync();
        if (input.Kind is { } kind)
        {
            query = query.Where(a => a.Kind == kind);
        }

        var term = IntakeTextNormalizer.Normalize(input.WildcardSearch);
        var searched = term.Length == 0 ? query : query.Where(a => a.NormalizedText.Contains(term));
        var totalCount = await AsyncExecuter.LongCountAsync(query);
        var filteredCount = term.Length == 0 ? totalCount : await AsyncExecuter.LongCountAsync(searched);
        var descending = input.Descending;
        IOrderedQueryable<IntakeAlias> sorted = (input.SortBy?.ToLowerInvariant()) switch
        {
            "normalizedtext" => descending ? searched.OrderByDescending(a => a.NormalizedText) : searched.OrderBy(a => a.NormalizedText),
            "kind" => descending ? searched.OrderByDescending(a => a.Kind) : searched.OrderBy(a => a.Kind),
            "usecount" => descending ? searched.OrderByDescending(a => a.UseCount) : searched.OrderBy(a => a.UseCount),
            "creationtime" => descending ? searched.OrderByDescending(a => a.CreationTime) : searched.OrderBy(a => a.CreationTime),
            _ => searched.OrderByDescending(a => a.CreationTime)
        };
        var page = await AsyncExecuter.ToListAsync(sorted.ThenBy(a => a.Id).Skip(input.StartRow).Take(input.EndRow - input.StartRow));
        var names = await AliasNamesAsync(page);
        var scopeNames = await RelationNamesAsync(page.Select(a => a.ScopeRelationId).OfType<Guid>());

        return new GridResponse<IntakeAliasDto>
        {
            Rows = page.Select(a => new IntakeAliasDto
            {
                Id = a.Id,
                Kind = a.Kind,
                ScopeRelationId = a.ScopeRelationId,
                ScopeRelationName = a.ScopeRelationId is { } scope ? scopeNames.GetValueOrDefault(scope) : null,
                NormalizedText = a.NormalizedText,
                TargetId = a.TargetId,
                TargetName = names.GetValueOrDefault((a.Kind, a.TargetId)),
                TargetRelationId = a.TargetRelationId,
                UseCount = a.UseCount,
                CreationTime = a.CreationTime
            }).ToList(),
            TotalCount = totalCount,
            FilteredCount = filteredCount
        };
    }

    [Authorize(OrdersPermissions.OrderIntakes.Resolve)]
    public virtual async Task DeleteAliasesAsync(Guid aliasId)
    {
        var alias = await _aliases.GetAsync(aliasId);
        await _aliases.DeleteAsync(alias);
        Logger.LogInformation("Intake alias {AliasId} deleted.", aliasId);
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private async Task<OrderIntakeDetailDto> ToDetailAsync(OrderIntake intake)
    {
        var numbers = await OrderNumbersAsync(intake.TransportOrderId is { } orderId ? [orderId] : []);
        return new OrderIntakeDetailDto
        {
            Id = intake.Id,
            Status = intake.Status,
            SourceSummary = new OrderIntakeSourceSummaryDto
            {
                SourceDocumentId = intake.SourceDocumentId,
                IntakeIndex = intake.IntakeIndex,
                Channel = intake.Channel,
                Source = intake.Source,
                SenderAddress = intake.SenderAddress,
                Subject = intake.Subject,
                FileName = intake.FileName
            },
            Payload = OrderIntakeProcessor.Deserialize(intake.Payload),
            ReviewedPayload = intake.ReviewedPayload == null ? null : OrderIntakeProcessor.Deserialize(intake.ReviewedPayload),
            Resolution = intake.Resolution.Fields.Select(f => new OrderIntakeFieldResolutionDto
            {
                FieldPath = f.FieldPath,
                Status = f.Status,
                TargetId = f.TargetId,
                TargetName = f.TargetName,
                Method = f.Method,
                Score = f.Score,
                Creatable = f.Creatable,
                Candidates = f.Candidates.Select(c => new OrderIntakeCandidateDto { Id = c.Id, Name = c.Name, Detail = c.Detail }).ToList(),
                ErrorCode = f.ErrorCode
            }).ToList(),
            MappingConfidence = intake.MappingConfidence,
            ProfileTrusted = intake.ProfileTrusted,
            AutoConfirm = intake.AutoConfirm,
            Corrected = intake.Corrected,
            RejectReason = intake.RejectReason,
            TransportOrderId = intake.TransportOrderId,
            OrderNumber = intake.TransportOrderId is { } id ? numbers.GetValueOrDefault(id) : null,
            CreationTime = intake.CreationTime,
            LastModificationTime = intake.LastModificationTime,
            LastModifierId = intake.LastModifierId
        };
    }

    private async Task<Dictionary<Guid, string?>> OrderNumbersAsync(IEnumerable<Guid> orderIds)
    {
        var ids = orderIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string?>();
        }

        var rows = await AsyncExecuter.ToListAsync((await _orders.GetQueryableAsync()).Where(o => ids.Contains(o.Id)).Select(o => new { o.Id, o.Number }));
        return rows.ToDictionary(r => r.Id, r => r.Number);
    }

    private async Task<Dictionary<Guid, string>> RelationNamesAsync(IEnumerable<Guid> ids)
        => (await _references.RelationsAsync(ids, require: false)).ToDictionary(r => r.Key, r => r.Value.Name);

    /// <summary>The target names of one page of aliases: one batched read per kind.</summary>
    private async Task<Dictionary<(IntakeAliasKind, Guid), string>> AliasNamesAsync(List<IntakeAlias> aliases)
    {
        var culture = CultureInfo.CurrentUICulture.Name;
        var names = new Dictionary<(IntakeAliasKind, Guid), string>();
        List<Guid> TargetsOf(IntakeAliasKind kind) => aliases.Where(a => a.Kind == kind).Select(a => a.TargetId).Distinct().ToList();

        foreach (var (id, name) in await RelationNamesAsync(TargetsOf(IntakeAliasKind.Customer)))
        {
            names[(IntakeAliasKind.Customer, id)] = name;
        }

        foreach (var (id, address) in await _references.AddressesAsync(TargetsOf(IntakeAliasKind.Address), require: false))
        {
            names[(IntakeAliasKind.Address, id)] = address.RelationName + ", " + address.Street + (string.IsNullOrEmpty(address.HouseNumber) ? string.Empty : " " + address.HouseNumber)
                                                   + ", " + address.PostalCode + " " + address.City;
        }

        var orderTypeIds = TargetsOf(IntakeAliasKind.OrderType);
        if (orderTypeIds.Count > 0)
        {
            foreach (var type in await _orderTypes.GetListAsync(t => orderTypeIds.Contains(t.Id)))
            {
                names[(IntakeAliasKind.OrderType, type.Id)] = type.Name.Resolve(culture);
            }
        }

        var waypointTypeIds = TargetsOf(IntakeAliasKind.WaypointType);
        if (waypointTypeIds.Count > 0)
        {
            foreach (var type in await _waypointTypes.GetListAsync(t => waypointTypeIds.Contains(t.Id)))
            {
                names[(IntakeAliasKind.WaypointType, type.Id)] = type.Name.Resolve(culture);
            }
        }

        foreach (var (aliasKind, kind) in new[]
                 {
                     (IntakeAliasKind.TransportUnit, MasterDataReferenceKind.TransportUnit), (IntakeAliasKind.Product, MasterDataReferenceKind.Product),
                     (IntakeAliasKind.Temperature, MasterDataReferenceKind.Temperature)
                 })
        {
            foreach (var (id, name) in await _references.NamesAsync(kind, TargetsOf(aliasKind), require: false, "targetId"))
            {
                names[(aliasKind, id)] = name;
            }
        }

        return names;
    }

    private static IQueryable<OrderIntake> Sort(IQueryable<OrderIntake> query, string? sortBy, string? sortDir)
    {
        var descending = !string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase) && (sortBy == null || string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase));
        IOrderedQueryable<OrderIntake> sorted = (sortBy?.ToLowerInvariant()) switch
        {
            "status" => descending ? query.OrderByDescending(i => i.Status) : query.OrderBy(i => i.Status),
            "channel" => descending ? query.OrderByDescending(i => i.Channel) : query.OrderBy(i => i.Channel),
            "senderaddress" => descending ? query.OrderByDescending(i => i.SenderAddress) : query.OrderBy(i => i.SenderAddress),
            "subject" => descending ? query.OrderByDescending(i => i.Subject) : query.OrderBy(i => i.Subject),
            "filename" => descending ? query.OrderByDescending(i => i.FileName) : query.OrderBy(i => i.FileName),
            "customername" => descending ? query.OrderByDescending(i => i.CustomerName) : query.OrderBy(i => i.CustomerName),
            "customerreference" => descending ? query.OrderByDescending(i => i.CustomerReference) : query.OrderBy(i => i.CustomerReference),
            "openfieldcount" => descending ? query.OrderByDescending(i => i.OpenFieldCount) : query.OrderBy(i => i.OpenFieldCount),
            _ => descending ? query.OrderByDescending(i => i.CreationTime) : query.OrderBy(i => i.CreationTime)
        };
        return sorted.ThenBy(i => i.Id);
    }

    /// <summary>The start of a day in the installation's zone, as a UTC instant (design 50's rule, <see cref="OrdersClock"/>).</summary>
    private DateTime StartOfDayUtc(DateOnly day)
        => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), _clock.Zone);
}
