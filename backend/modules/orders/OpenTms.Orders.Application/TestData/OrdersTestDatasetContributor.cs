using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTms.Financial.TestData;
using OpenTms.MasterData.TestData;
using OpenTms.Orders.ConsignmentNotes;
using OpenTms.Orders.TransportOrders;
using OpenTms.Platform.NumberSequences;
using OpenTms.Platform.TestData;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.BlobStoring;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace OpenTms.Orders.TestData;

/// <summary>
/// Orders' part of "Reset to dataset" (docs/designs/65-tenant-test-datasets.md), Order 300: after Master Data (100) and
/// Financial (200), before Planning &amp; Execution (400), which builds legs and trips from the published
/// <see cref="OrdersTestDataset"/>.
/// <para>
/// <b>Wipe:</b> transport orders (lines, stops, measurements, characteristics, default stops, references, price-list and
/// discount links, remarks go with them through their cascading foreign keys), order templates (with their children)
/// and consignment-note documents (their lines cascade; their stored PDFs are removed from the blob container first).
/// Kept, as reference data and tenant configuration: waypoint types, time slots, transport order types, consignment
/// note profiles and fixed descriptions, order attribute definitions.
/// </para>
/// <para>
/// <b>Generate:</b> templates (<see cref="TestDatasetContext.NextCount"/>) and <see cref="TestDatasetProfile.Orders"/>
/// orders through the aggregates themselves (<see cref="OrdersTestDataBuilder"/>), numbered from the order type's real
/// sequence in the order date's period, written in batches. The single-order create path's side effects are skipped on
/// purpose (one per order would flood the outbox and the job queue): no distributed events (TransportOrderConfirmed,
/// LinesChanged, StopsChanged, StatusChanged, Cancelled), so Planning generates no legs and Financial calculates no rate
/// lines from events, and no derived-value request for the order summary. The summary's grid columns are written directly.
/// </para>
/// </summary>
[DisableAuditing]
public class OrdersTestDatasetContributor : ITestDatasetContributor, ITransientDependency
{
    public const int OrderBatchSize = 500;
    public const int TemplateBatchSize = 250;
    private const int PageSize = 1_000;

    private readonly TestDatasetWriter _writer;
    private readonly IRepository<TransportOrder, Guid> _orders;
    private readonly IRepository<TransportOrderTemplate, Guid> _templates;
    private readonly IRepository<ConsignmentNoteDocument, Guid> _consignmentNotes;
    private readonly IRepository<TransportOrderType, Guid> _orderTypes;
    private readonly IRepository<WaypointType, Guid> _waypointTypes;
    private readonly IRepository<TimeSlot, Guid> _timeSlots;
    private readonly IBlobContainer<ConsignmentNoteContainer> _blobs;
    private readonly INumberSequenceCounterStore _counters;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly IDataFilter _dataFilter;
    private readonly IGuidGenerator _guids;
    private readonly ICurrentTenant _currentTenant;
    private readonly OrdersClock _clock;

    public ILogger<OrdersTestDatasetContributor> Logger { get; set; } = NullLogger<OrdersTestDatasetContributor>.Instance;

    public OrdersTestDatasetContributor(
        TestDatasetWriter writer,
        IRepository<TransportOrder, Guid> orders,
        IRepository<TransportOrderTemplate, Guid> templates,
        IRepository<ConsignmentNoteDocument, Guid> consignmentNotes,
        IRepository<TransportOrderType, Guid> orderTypes,
        IRepository<WaypointType, Guid> waypointTypes,
        IRepository<TimeSlot, Guid> timeSlots,
        IBlobContainer<ConsignmentNoteContainer> blobs,
        INumberSequenceCounterStore counters,
        IUnitOfWorkManager unitOfWorkManager,
        IAsyncQueryableExecuter asyncExecuter,
        IDataFilter dataFilter,
        IGuidGenerator guids,
        ICurrentTenant currentTenant,
        OrdersClock clock)
    {
        _writer = writer;
        _orders = orders;
        _templates = templates;
        _consignmentNotes = consignmentNotes;
        _orderTypes = orderTypes;
        _waypointTypes = waypointTypes;
        _timeSlots = timeSlots;
        _blobs = blobs;
        _counters = counters;
        _unitOfWorkManager = unitOfWorkManager;
        _asyncExecuter = asyncExecuter;
        _dataFilter = dataFilter;
        _guids = guids;
        _currentTenant = currentTenant;
        _clock = clock;
    }

    public int Order => 300;

    public async Task WipeAsync(TestDatasetContext context)
    {
        var (removed, failed) = await DeleteConsignmentNoteFilesAsync();
        await _writer.DeleteAllAsync(_consignmentNotes);
        await _writer.DeleteAllAsync(_orders);
        await _writer.DeleteAllAsync(_templates);
        Logger.LogInformation(
            "Tenant {TenantId}: Orders wiped (orders, templates, consignment notes; {FileCount} consignment-note files removed, {FailedCount} not removed).",
            context.TenantId, removed, failed);
    }

    public async Task GenerateAsync(TestDatasetContext context)
    {
        var masterData = context.Get<MasterDataTestDataset>();
        var refs = await LoadReferencesAsync();
        if (refs == null)
        {
            Logger.LogWarning("Tenant {TenantId}: no transport order type or no Load/Unload waypoint type; Orders generated nothing.", context.TenantId);
            context.Publish(new OrdersTestDataset());
            return;
        }

        var builder = new OrdersTestDataBuilder(context, masterData, context.Find<FinancialTestDataset>(), refs, _guids.Create, _currentTenant.Id, _clock.NowUtc);
        foreach (var waypointType in refs.WaypointTypes)
        {
            builder.Dataset.WaypointTypes[waypointType.Id] = new TestWaypointType(
                waypointType.Id, waypointType.Code, waypointType.Activity, waypointType.SuccessiveWaypointTypeId, waypointType.DefaultDurationMinutes);
        }

        if (builder.MissingInput is { } missing)
        {
            Logger.LogWarning("Tenant {TenantId}: Orders generated nothing: {Reason}.", context.TenantId, missing);
            context.Publish(builder.Dataset);
            return;
        }

        var templates = await _writer.InsertAsync(_templates, builder.BuildTemplates(context.NextCount()), TemplateBatchSize);

        /* The order type's own sequence (design 50), one counter per period of the order dates. Numbers are drawn here in
         * date order and the counters raised once per period afterwards, instead of one counter round trip per order;
         * the next order a user confirms continues after the last generated number. */
        var type = refs.OrderType;
        var periods = builder.Dates.Select(d => type.Numbering.Period(d)).Distinct().ToList();
        var last = new Dictionary<string, long>(StringComparer.Ordinal);
        using (var unitOfWork = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            foreach (var period in periods)
            {
                last[period] = await _counters.GetLastValueAsync(type.SequenceKey, period);
            }

            await unitOfWork.CompleteAsync();
        }

        string NextNumber(DateOnly date)
        {
            var period = type.Numbering.Period(date);
            var value = last[period] + 1;
            last[period] = value;
            return type.Numbering.Format(value, date);
        }

        var orders = await _writer.InsertAsync(_orders, builder.BuildOrders(NextNumber), OrderBatchSize);

        using (var unitOfWork = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            foreach (var (period, value) in last.Where(p => p.Value > 0))
            {
                await _counters.RaiseLastValueAsync(type.SequenceKey, period, value);
            }

            await unitOfWork.CompleteAsync();
        }

        context.Publish(builder.Dataset);
        Logger.LogInformation(
            "Tenant {TenantId}: Orders generated {TemplateCount} templates and {OrderCount} orders ({TemplateOrderCount} from recurring templates).",
            context.TenantId, templates, orders, builder.TemplateOrderCount);
    }

    /// <summary>The seeded reference data (the seed ran before generation); small lists, read whole like the seed does.</summary>
    private async Task<OrdersTestReferences?> LoadReferencesAsync()
    {
        using var unitOfWork = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var types = (await _orderTypes.GetListAsync(t => !t.IsArchived, includeDetails: true))
            .OrderByDescending(t => t.IsDefault).ThenBy(t => t.Code, StringComparer.Ordinal).ToList();
        var waypointTypes = await _waypointTypes.GetListAsync(w => !w.IsArchived);
        var timeSlots = await _timeSlots.GetListAsync(t => !t.IsArchived);
        await unitOfWork.CompleteAsync();

        var load = FirstOf(waypointTypes, WaypointActivity.Load, "LOAD");
        var unload = FirstOf(waypointTypes, WaypointActivity.Unload, "UNLOAD");
        if (types.Count == 0 || load == null || unload == null)
        {
            return null;
        }

        return new OrdersTestReferences(types[0], load, unload, waypointTypes, timeSlots);
    }

    private static WaypointType? FirstOf(IEnumerable<WaypointType> types, WaypointActivity activity, string code)
        => types.Where(t => t.Activity == activity).OrderBy(t => t.Code == code ? 0 : 1).ThenBy(t => t.Code, StringComparer.Ordinal).FirstOrDefault();

    /// <summary>
    /// The stored PDFs of the consignment notes about to be deleted, soft-deleted documents included. Best effort: a file
    /// that cannot be removed is counted and left (the row goes anyway), so a storage hiccup never blocks a reset.
    /// </summary>
    private async Task<(int Removed, int Failed)> DeleteConsignmentNoteFilesAsync()
    {
        var blobNames = new List<string>();
        using (_dataFilter.Disable<ISoftDelete>())
        using (var unitOfWork = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
        {
            var query = await _consignmentNotes.GetQueryableAsync();
            for (var skip = 0; ; skip += PageSize)
            {
                var page = await _asyncExecuter.ToListAsync(query.OrderBy(d => d.Id).Select(d => d.BlobName).Skip(skip).Take(PageSize));
                blobNames.AddRange(page);
                if (page.Count < PageSize)
                {
                    break;
                }
            }

            await unitOfWork.CompleteAsync();
        }

        int removed = 0, failed = 0;
        foreach (var name in blobNames)
        {
            try
            {
                await _blobs.DeleteAsync(name);
                removed++;
            }
            catch (Exception ex)
            {
                failed++;
                Logger.LogDebug(ex, "A consignment-note file could not be removed during a dataset reset.");
            }
        }

        return (removed, failed);
    }
}
