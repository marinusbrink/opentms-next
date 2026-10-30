using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using OpenTms.MasterData.Published;
using OpenTms.Orders.Permissions;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution.Published;
using OpenTms.Platform.DocumentRendering;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using OpenTms.Platform.NumberSequences;
using OpenTms.Platform.ReferenceData;
using OpenTms.Platform.ReportDefinitions;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;

namespace OpenTms.Orders.ConsignmentNotes;

/// <summary>The blob container of the rendered consignment notes (AD-09; the database provider as configured today).</summary>
[BlobContainerName(ConsignmentNoteConsts.BlobContainerName)]
public class ConsignmentNoteContainer
{
}

/// <summary>
/// Printing and the stored consignment notes (design 58 "CMR", PO decision Q11): the print composes the catalogue payload
/// (ConsignmentNoteComposer), renders it through Platform's IDocumentRenderer with the report definition of purpose
/// ConsignmentNote, draws one number per document from Orders.ConsignmentNote, stores the bytes in the consignment-notes
/// container and keeps one row per print with the data snapshot. A trip print yields one document per order on the trip.
/// </summary>
[RequiresFeature(AppFeatures.Transport)]
public class ConsignmentNoteAppService : OrdersAppServiceBase, IConsignmentNoteAppService
{
    private readonly IRepository<ConsignmentNoteDocument, Guid> _documents;
    private readonly IRepository<ConsignmentNoteProfile, Guid> _profiles;
    private readonly IRepository<ConsignmentNoteFixedDescription, Guid> _fixedDescriptions;
    private readonly IRepository<TransportOrder, Guid> _orders;
    private readonly IRepository<ReportDefinition, Guid> _reportDefinitions;
    private readonly IDocumentRenderer _renderer;
    private readonly INumberSequenceGenerator _numbers;
    private readonly IBlobContainer<ConsignmentNoteContainer> _blobs;
    private readonly OrdersReferences _references;
    private readonly ILegReader _legs;
    private readonly ITransportTripLookup _trips;

    public ConsignmentNoteAppService(
        IRepository<ConsignmentNoteDocument, Guid> documents,
        IRepository<ConsignmentNoteProfile, Guid> profiles,
        IRepository<ConsignmentNoteFixedDescription, Guid> fixedDescriptions,
        IRepository<TransportOrder, Guid> orders,
        IRepository<ReportDefinition, Guid> reportDefinitions,
        IDocumentRenderer renderer,
        INumberSequenceGenerator numbers,
        IBlobContainer<ConsignmentNoteContainer> blobs,
        OrdersReferences references,
        ILegReader legs,
        ITransportTripLookup trips)
    {
        _documents = documents;
        _profiles = profiles;
        _fixedDescriptions = fixedDescriptions;
        _orders = orders;
        _reportDefinitions = reportDefinitions;
        _renderer = renderer;
        _numbers = numbers;
        _blobs = blobs;
        _references = references;
        _legs = legs;
        _trips = trips;
    }

    [RequiresFeature(AppFeatures.TransportApp.Orders)]
    [Authorize(OrdersPermissions.TransportOrders.Print)]
    public virtual async Task<List<ConsignmentNoteDocumentDto>> PrintAsync(PrintConsignmentNoteDto input)
    {
        var (orders, linesByOrder, tripId) = await SelectLinesAsync(input);
        var profile = await ProfileAsync(input.ProfileId);
        var layout = await LayoutAsync(profile, input.Overrides);
        var definition = await ReportDefinitionAsync(input.ReportDefinitionId ?? profile.ReportDefinitionId);
        var trip = tripId == null ? null : (await _trips.GetAsync([tripId.Value])).FirstOrDefault();

        var documents = new List<ConsignmentNoteDocument>();
        foreach (var order in orders.OrderBy(o => o.Number))
        {
            var lines = linesByOrder[order.Id];
            var orderTrip = trip ?? await TripOfAsync(order, lines);
            var source = await SourceAsync(order, lines, layout, orderTrip);
            var payload = ConsignmentNoteComposer.Compose(source with { Number = await _numbers.NextAsync(ConsignmentNoteConsts.NumberSequenceKey, ConsignmentNoteConsts.NumberFormat) });
            var json = payload.ToJsonString();
            var rendered = await RenderAsync(definition.Id, json);

            var blobName = GuidGenerator.Create().ToString("N");
            await _blobs.SaveAsync(blobName, rendered.Content);
            var document = new ConsignmentNoteDocument(
                GuidGenerator.Create(), CurrentTenant.Id, payload["Entity"]!["Number"]!.GetValue<string>(), order.Id, lines.Select(l => l.Id), orderTrip?.Id,
                profile.Id, definition.Id, Clock.Now, CurrentUser.Id, blobName, rendered.ContentType, rendered.FileName, rendered.Content.LongLength,
                Convert.ToHexString(SHA256.HashData(rendered.Content)).ToLowerInvariant(), json);
            await _documents.InsertAsync(document, autoSave: true);
            documents.Add(document);
            Logger.LogInformation("Consignment note {DocumentId} printed for order {OrderId} ({LineCount} lines, {Size} bytes).", document.Id, order.Id, lines.Count, document.SizeBytes);
        }

        return await ToDtosAsync(documents);
    }

    /// <summary>
    /// The stored notes, for three screens with their own nav-tree feature (review M3, design 56 "Flag &amp; rollout"): the
    /// Transport app's Documents list asks without an anchor and needs <c>Apps.Transport.Documents</c>; the order editor's
    /// Documents tab asks with <c>OrderId</c> and needs <c>Apps.Transport.Orders</c>; the trip editor's tab asks with
    /// <c>TripId</c> and needs <c>Apps.Transport.Trips</c>. The app itself is the class attribute. The detail and the
    /// download serve every screen and need only the app, so a CMR stays downloadable from the order with Documents off.
    /// </summary>
    [Authorize(OrdersPermissions.TransportOrders.Default)]
    public virtual async Task<GridResponse<ConsignmentNoteDocumentDto>> GetDocumentListAsync(ConsignmentNoteDocumentListRequestDto input)
    {
        await FeatureChecker.CheckEnabledAsync(input.OrderId != null
            ? AppFeatures.TransportApp.Orders
            : input.TripId != null ? AppFeatures.TransportApp.Trips : AppFeatures.TransportApp.Documents);

        var query = await _documents.GetQueryableAsync();
        if (input.OrderId != null) query = query.Where(d => d.OrderId == input.OrderId.Value);
        if (input.TripId != null) query = query.Where(d => d.TripId == input.TripId.Value);
        if (input.PrintedFrom != null) query = query.Where(d => d.PrintedAt >= input.PrintedFrom.Value.ToDateTime(TimeOnly.MinValue));
        if (input.PrintedTo != null) query = query.Where(d => d.PrintedAt < input.PrintedTo.Value.AddDays(1).ToDateTime(TimeOnly.MinValue));
        var term = input.WildcardSearch?.Trim().ToLowerInvariant();
        var searched = string.IsNullOrEmpty(term) ? query : query.Where(d => d.Number.ToLower().Contains(term) || d.FileName.ToLower().Contains(term));

        var totalCount = await AsyncExecuter.LongCountAsync(query);
        var filteredCount = string.IsNullOrEmpty(term) ? totalCount : await AsyncExecuter.LongCountAsync(searched);
        var sorted = (input.SortBy?.ToLowerInvariant()) switch
        {
            null or "" or "printedat" => input.Descending ? searched.OrderByDescending(d => d.PrintedAt) : searched.OrderBy(d => d.PrintedAt),
            "number" => input.Descending ? searched.OrderByDescending(d => d.Number) : searched.OrderBy(d => d.Number),
            _ => throw new BusinessException(ReferenceDataErrorCodes.UnknownSortColumn).WithData("sortBy", input.SortBy)
        };
        var page = await AsyncExecuter.ToListAsync(sorted.Skip(input.StartRow).Take(input.EndRow - input.StartRow));
        return new GridResponse<ConsignmentNoteDocumentDto> { Rows = await ToDtosAsync(page), TotalCount = totalCount, FilteredCount = filteredCount };
    }

    [Authorize(OrdersPermissions.TransportOrders.Default)]
    public virtual async Task<ConsignmentNoteDocumentDto> GetDocumentAsync(Guid id) => (await ToDtosAsync([await _documents.GetAsync(id)])).Single();

    /// <summary>Streams the stored blob; the caller needs the orders permission and the tenant filter finds the row (design 58 Security quickscan).</summary>
    [Authorize(OrdersPermissions.TransportOrders.Default)]
    public virtual async Task<IRemoteStreamContent> GetDownloadAsync(Guid id)
    {
        var document = await _documents.GetAsync(id);
        var bytes = await _blobs.GetAllBytesOrNullAsync(document.BlobName) ?? throw new BusinessException(OrdersErrorCodes.NothingToPrint);
        return new RemoteStreamContent(new MemoryStream(bytes), document.FileName, document.ContentType);
    }

    // ── the print's parts ──────────────────────────────────────────────────────

    private async Task<(List<TransportOrder> Orders, Dictionary<Guid, List<TransportOrderLine>> Lines, Guid? TripId)> SelectLinesAsync(PrintConsignmentNoteDto input)
    {
        var lineIds = input.OrderLineIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (input.TripId != null)
        {
            lineIds = (await _legs.GetByTripAsync(input.TripId.Value)).Select(l => l.OrderLineId).Distinct().ToList();
        }

        if (lineIds.Count == 0)
        {
            throw new BusinessException(OrdersErrorCodes.NothingToPrint);
        }

        var orders = await _orders.GetListAsync(o => o.Lines.Any(l => lineIds.Contains(l.Id)), includeDetails: true);
        if (orders.Count == 0)
        {
            throw new BusinessException(OrdersErrorCodes.NothingToPrint);
        }

        var lines = orders.ToDictionary(o => o.Id, o => o.Lines.Where(l => lineIds.Contains(l.Id)).OrderBy(l => l.Position).ToList());
        return (orders, lines, input.TripId);
    }

    private async Task<ConsignmentNoteProfile> ProfileAsync(Guid? profileId)
    {
        if (profileId != null)
        {
            return await _profiles.GetAsync(profileId.Value);
        }

        return (await _profiles.GetListAsync(p => p.IsDefault && !p.IsArchived)).FirstOrDefault()
               ?? throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "profileId");
    }

    private async Task<CmrLayout> LayoutAsync(ConsignmentNoteProfile p, ConsignmentNoteOverridesDto? o)
    {
        var copies = o?.Copies ?? p.Copies;
        if (copies is < ConsignmentNoteConsts.MinCopies or > ConsignmentNoteConsts.MaxCopies)
        {
            throw new BusinessException(OrdersErrorCodes.InvalidCopies).WithData("min", ConsignmentNoteConsts.MinCopies).WithData("max", ConsignmentNoteConsts.MaxCopies);
        }

        var fixedIds = (o?.FixedDescriptionIds ?? p.FixedDescriptionIds).Where(id => id != Guid.Empty).Distinct().ToList();
        var fixedTexts = fixedIds.Count == 0 ? [] : (await _fixedDescriptions.GetListAsync(f => fixedIds.Contains(f.Id))).OrderBy(f => fixedIds.IndexOf(f.Id)).Select(f => f.Text).ToList();
        return new CmrLayout(
            p.Name, copies, o?.ShowBorders ?? p.ShowBorders, o?.ShowQrCode ?? p.ShowQrCode, o?.GroupByLeg ?? p.GroupByLeg,
            Defined(o?.Box1Sender ?? p.Box1Sender), Defined(o?.Box2Consignee ?? p.Box2Consignee), Defined(o?.Box3DeliveryPlace ?? p.Box3DeliveryPlace),
            Defined(o?.Box4TakingOverPlace ?? p.Box4TakingOverPlace), Defined(o?.Box16Carrier ?? p.Box16Carrier), Defined(o?.Box17SuccessiveCarrier ?? p.Box17SuccessiveCarrier),
            Defined(o?.Box21EstablishedBy ?? p.Box21EstablishedBy),
            (o?.Box6Columns ?? p.Box6Columns).Where(Enum.IsDefined).Distinct().ToList(),
            (o?.Box13Items ?? p.Box13Items).Where(Enum.IsDefined).Distinct().ToList(),
            (o?.Box18Items ?? p.Box18Items).Where(Enum.IsDefined).Distinct().ToList(),
            fixedTexts);
    }

    private static CmrPartySource Defined(CmrPartySource source)
        => Enum.IsDefined(source) ? source : throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", "boxSource").WithData("value", (int)source);

    /// <summary>The explicit definition, else the tenant's default of purpose ConsignmentNote (the UI culture first), else ConsignmentNoteTemplateMissing.</summary>
    private async Task<ReportDefinition> ReportDefinitionAsync(Guid? reportDefinitionId)
    {
        if (reportDefinitionId != null)
        {
            var explicitDefinition = await _reportDefinitions.FindAsync(reportDefinitionId.Value);
            if (explicitDefinition == null || explicitDefinition.Purpose != ReportPurpose.ConsignmentNote || explicitDefinition.IsArchived)
            {
                throw new BusinessException(OrdersErrorCodes.ConsignmentNoteTemplateMissing);
            }

            return explicitDefinition;
        }

        var defaults = await _reportDefinitions.GetListAsync(d => d.Purpose == ReportPurpose.ConsignmentNote && d.IsDefault && !d.IsArchived);
        var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        return defaults.FirstOrDefault(d => d.Culture == culture) ?? defaults.OrderBy(d => d.Culture).FirstOrDefault()
               ?? throw new BusinessException(OrdersErrorCodes.ConsignmentNoteTemplateMissing);
    }

    private async Task<TransportTripReferenceDto?> TripOfAsync(TransportOrder order, List<TransportOrderLine> lines)
    {
        var lineIds = lines.Select(l => l.Id).ToHashSet();
        /* The request's trip stands in only for lines it takes (design 61 point 7: a request may name some freight items). */
        var tripId = (await _legs.GetByOrderAsync(order.Id)).Where(l => lineIds.Contains(l.OrderLineId)).Select(l => l.TripId).FirstOrDefault(t => t != null)
                     ?? (order.TripRequest is { } request && request.Covers(lineIds) ? request.TripId : null);
        return tripId == null ? null : (await _trips.GetAsync([tripId.Value])).FirstOrDefault();
    }

    private async Task<CmrSource> SourceAsync(TransportOrder order, List<TransportOrderLine> lines, CmrLayout layout, TransportTripReferenceDto? trip)
    {
        var stops = lines.SelectMany(l => l.Stops).ToList();
        var relationIds = stops.Select(s => s.Details.RelationId).OfType<Guid>().Append(order.CustomerRelationId)
            .Concat(lines.Select(l => l.Freight.ShipperRelationId).OfType<Guid>())
            .Concat(new[] { trip?.CarrierRelationId, trip?.DepartureRelationId, trip?.ArrivalRelationId, order.TripRequest?.CarrierRelationId }.OfType<Guid>())
            .Distinct().ToList();
        var own = await _references.OwnCompanyAsync();
        if (own != null)
        {
            relationIds.Add(own.Id);
        }

        var relations = await _references.RelationsAsync(relationIds, require: false);
        var addresses = await _references.AddressesAsync(stops.Select(s => s.Details.RelationAddressId).OfType<Guid>(), require: false);
        var defaults = await _references.DefaultAddressesAsync(relationIds);
        var parties = relations.Values.ToDictionary(r => r.Id, r => new CmrParty(r.Id, r.Name,
            defaults.TryGetValue(r.Id, out var home) ? new CmrAddress(home.Street, home.HouseNumber, home.PostalCode, home.City, home.CountryCode) : null));

        CmrParty? Party(Guid? relationId) => relationId != null && parties.TryGetValue(relationId.Value, out var p) ? p : null;
        var units = await _references.NamesAsync(MasterDataReferenceKind.TransportUnit, lines.Select(l => l.Freight.TransportUnitId), require: false, "transportUnitId");
        var products = await _references.NamesAsync(MasterDataReferenceKind.Product, lines.Select(l => l.Freight.ProductId).OfType<Guid>(), require: false, "productId");
        var codes = await _references.NamesAsync(MasterDataReferenceKind.ProductRelationCode, lines.Select(l => l.Freight.ProductRelationCodeId).OfType<Guid>(), require: false, "productRelationCodeId");
        var temperatures = await _references.NamesAsync(MasterDataReferenceKind.Temperature, lines.Select(l => l.Freight.TemperatureId).OfType<Guid>(), require: false, "temperatureId");

        var carrierId = trip?.CarrierRelationId ?? (order.TripRequest is { } request && request.Covers(lines.Select(l => l.Id)) ? request.CarrierRelationId : null);
        var carrier = Party(carrierId) ?? (trip?.CarrierName == null ? null : new CmrParty(carrierId, trip.CarrierName, null));
        return new CmrSource(
            order, lines, layout, string.Empty, Party(order.CustomerRelationId), parties,
            addresses.ToDictionary(a => a.Key, a => new CmrAddress(a.Value.Street, a.Value.HouseNumber, a.Value.PostalCode, a.Value.City, a.Value.CountryCode)),
            own == null ? null : Party(own.Id), carrier, Party(lines.Select(l => l.Freight.ShipperRelationId).FirstOrDefault(s => s != null)),
            trip == null ? null : new CmrTripFacts(trip.Id, trip.Number, trip.TripDate, trip.CarrierName, trip.VehicleNames, trip.LicencePlates, trip.DriverNames),
            Party(trip?.DepartureRelationId), Party(trip?.ArrivalRelationId),
            new CmrNames(units, products, codes, temperatures), Clock.Now, CurrentUser.UserName);
    }

    private async Task<RenderedDocument> RenderAsync(Guid definitionId, string json)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(ConsignmentNoteConsts.RenderTimeoutSeconds));
        using var document = JsonDocument.Parse(json);
        try
        {
            return await _renderer.RenderAsync(definitionId, document.RootElement.Clone(), ReportRenderFormat.Pdf, timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new BusinessException(OrdersErrorCodes.RenderTimedOut).WithData("seconds", ConsignmentNoteConsts.RenderTimeoutSeconds);
        }
    }

    private async Task<List<ConsignmentNoteDocumentDto>> ToDtosAsync(IReadOnlyList<ConsignmentNoteDocument> documents)
    {
        if (documents.Count == 0)
        {
            return [];
        }

        var orderIds = documents.Select(d => d.OrderId).Distinct().ToList();
        var numbers = (await _orders.GetListAsync(o => orderIds.Contains(o.Id), includeDetails: false)).ToDictionary(o => o.Id, o => o.Number);
        var profileIds = documents.Select(d => d.ProfileId).Distinct().ToList();
        var profiles = (await _profiles.GetListAsync(p => profileIds.Contains(p.Id))).ToDictionary(p => p.Id, p => p.Name);
        return documents.Select(d => new ConsignmentNoteDocumentDto
        {
            Id = d.Id,
            Number = d.Number,
            OrderId = d.OrderId,
            OrderNumber = numbers.GetValueOrDefault(d.OrderId),
            LineIds = d.Lines.Select(l => l.LineId).ToList(),
            TripId = d.TripId,
            ProfileId = d.ProfileId,
            ProfileName = profiles.GetValueOrDefault(d.ProfileId),
            ReportDefinitionId = d.ReportDefinitionId,
            PrintedAt = d.PrintedAt,
            PrintedByUserId = d.PrintedByUserId,
            FileName = d.FileName,
            ContentType = d.ContentType,
            SizeBytes = d.SizeBytes,
            Sha256 = d.Sha256
        }).ToList();
    }
}
