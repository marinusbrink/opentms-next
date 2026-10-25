using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData.Relations;
using OpenTms.Orders;
using OpenTms.Orders.TransportOrders;
using OpenTms.TestDoubles;
using OpenTms.TransportRun;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using MasterDataNames = OpenTms.MasterData.LocalizedNameDto;

namespace OpenTms.EntityFrameworkCore.Orders;

/// <summary>
/// Shared plumbing for the transport order service tests on SQLite (design 58): the world of
/// <see cref="TransportRunFixture"/> (Master Data, Orders, Planning and Financial master data created through the real
/// services in the same tenant), the create DTOs, the recorded derived-value requests and the recorded Orders facts.
/// Planning's legs and Financial's rate lines are real rows: an order confirmed here gets its legs through Planning's
/// handler (SQLite delivers events in process), trips are planned through Planning's service, stored revenue is written
/// as rate lines. Today is the date the services use.
/// </summary>
public abstract class TransportOrderTestBase<TModule> : OpenTmsTestBase<TModule> where TModule : IAbpModule
{
    protected ICurrentTenant CurrentTenant { get; }

    protected Guid TenantA { get; } = Guid.NewGuid();

    protected Guid TenantB { get; } = Guid.NewGuid();

    protected ITransportOrderAppService Orders => GetRequiredService<ITransportOrderAppService>();

    protected TransportRunFixture Run => GetRequiredService<TransportRunFixture>();

    protected RecordedDerivedValueRequests Requests => GetRequiredService<RecordedDerivedValueRequests>();

    protected OrdersEventRecorder Events => GetRequiredService<OrdersEventRecorder>();

    protected DateOnly Today { get; }

    protected TransportOrderTestBase()
    {
        CurrentTenant = GetRequiredService<ICurrentTenant>();
        Today = GetRequiredService<OrdersClock>().Today();
    }

    protected IDisposable InTenantA() => CurrentTenant.Change(TenantA);

    protected IDisposable InTenantB() => CurrentTenant.Change(TenantB);

    protected static CultureScope UiCulture(string culture) => new(culture);

    /// <summary>The reasons the order summary (target type Orders.TransportOrder) was requested for, in order.</summary>
    protected IReadOnlyList<string> SummaryReasons(Guid orderId) => Requests.ReasonsFor(OrdersConsts.OrderSummaryTargetType, orderId);

    /// <summary>The Master Data, Orders, Planning and Financial rows of one tenant; created in the current tenant.</summary>
    protected Task<TransportRunWorld> FixtureAsync(TransportOrderRequiredFields required = TransportOrderRequiredFields.None, bool withOwnCompany = true)
        => Run.BuildAsync(new TransportRunWorldOptions { RequiredFields = required, WithOwnCompany = withOwnCompany });

    protected Task<Place> RelationAsync(string code, string name, RelationRoles roles, Guid countryId, string street, string number, string postalCode, string city)
        => Run.PlaceAsync(code, name, roles, 51.9, 4.5, city, street, number, postalCode, countryId);

    protected static MasterDataNames Names(string en, string nl) => new() { ["en"] = en, ["nl"] = nl };

    protected static OpenTms.Platform.ReferenceData.LocalizedNameDto Names(string en) => new() { ["en"] = en };

    protected static OpenTms.Platform.ReferenceData.LocalizedNameDto OrdersNames(string en, string nl) => new() { ["en"] = en, ["nl"] = nl };

    protected static TransportOrderStopInputDto Stop(Guid waypointTypeId, Guid relationId, Guid addressId, DateOnly date, string? reference = null, Guid? timeSlotId = null, Guid? id = null)
        => TransportRunFixture.Stop(waypointTypeId, relationId, addressId, date, reference, timeSlotId, id: id);

    protected TransportOrderLineInputDto Line(TransportRunWorld f, decimal quantity = 2, double? weightKg = 100, bool withStops = true, Guid? id = null, DateOnly? date = null)
        => Run.Line(f, quantity, weightKg, withStops, id, date);

    protected TransportOrderCreateDto CreateDto(TransportRunWorld f, bool confirm = false, int lines = 1, string? customerReference = "PO-1", DateOnly? orderDate = null)
        => Run.CreateDto(f, confirm, lines, customerReference, orderDate);

    protected static TransportOrderUpdateDto HeaderUpdate(TransportOrderDto dto, Action<TransportOrderUpdateDto>? change = null)
    {
        var update = new TransportOrderUpdateDto
        {
            TransportOrderTypeId = dto.TransportOrderTypeId, CustomerRelationId = dto.CustomerRelationId, CustomerContactPersonId = dto.CustomerContactPersonId,
            OrderDate = dto.OrderDate, CustomerReference = dto.CustomerReference, OrganizationUnitId = dto.OrganizationUnitId,
            InternalRemarks = dto.InternalRemarks, InvoicingRemarks = dto.InvoicingRemarks, ConcurrencyStamp = dto.ConcurrencyStamp
        };
        change?.Invoke(update);
        return update;
    }

    protected static List<TransportOrderLineInputDto> LinesOf(TransportOrderDto dto)
        => dto.Lines.Select(l => new TransportOrderLineInputDto
        {
            Id = l.Id, TransportUnitId = l.TransportUnitId, ProductId = l.ProductId, Quantity = l.Quantity, WeightKg = l.WeightKg, Remarks = l.Remarks,
            Stops = l.Stops.Select(s => new TransportOrderStopInputDto
            {
                Id = s.Id, WaypointTypeId = s.WaypointTypeId, RelationId = s.RelationId, RelationAddressId = s.RelationAddressId, Date = s.Date,
                TimeSlotId = s.TimeSlotId, WindowFrom = s.WindowFrom, WindowTill = s.WindowTill, Reference = s.Reference, Remarks = s.Remarks, PlannedDurationMin = s.PlannedDurationMin
            }).ToList()
        }).ToList();
}
