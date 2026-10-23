using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Platform.Localization;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Orders.TransportOrders;

/// <summary>
/// The type of a stop (OpenTMS WaypointType), design 51: what happens there (<see cref="Activity"/>), the type proposed
/// for the next stop, the relation category the address picker offers first, the relation a new stop starts with, the
/// planned duration and the codes on-board computers use for it. The Master Data ids carry no database constraint
/// (assessment §2.2); the service checks them through the published lookup on every save.
/// </summary>
public class WaypointType : ReferenceDataAggregateRoot
{
    public string Code { get; private set; } = default!;

    public LocalizedName Name { get; private set; } = default!;

    public WaypointActivity Activity { get; private set; } = WaypointActivity.Other;

    /// <summary>The type proposed for the next stop; a proposal only, so a cycle (Load → Unload → Load) is allowed.</summary>
    public Guid? SuccessiveWaypointTypeId { get; private set; }

    /// <summary>Master Data relation category (OpenTMS WaypointTypeCategory).</summary>
    public Guid? RelationCategoryId { get; private set; }

    /// <summary>Master Data relation a new stop of this type starts with (OpenTMS StationingRelationId).</summary>
    public Guid? DefaultRelationId { get; private set; }

    public int? DefaultDurationMinutes { get; private set; }

    public bool IsArchived { get; private set; }

    public virtual ICollection<WaypointTypeFleetManagementCode> FleetManagementCodes { get; private set; } = new List<WaypointTypeFleetManagementCode>();

    protected WaypointType()
    {
    }

    public WaypointType(Guid id, Guid? tenantId, string code, LocalizedName name, WaypointActivity activity)
        : base(id, tenantId)
    {
        SetCode(code);
        SetName(name);
        SetActivity(activity);
    }

    public void SetCode(string code) => Code = NormalizeCode(code);

    public void SetName(LocalizedName name) => Name = RequireName(name);

    public void SetArchived(bool isArchived) => IsArchived = isArchived;

    public void SetActivity(WaypointActivity activity)
    {
        if (!Enum.IsDefined(activity))
        {
            throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", "activity").WithData("value", (int)activity);
        }

        Activity = activity;
    }

    /// <summary>The service checks that the successor exists; a self-reference is refused here.</summary>
    public void SetSuccessor(Guid? successiveWaypointTypeId)
    {
        if (successiveWaypointTypeId == Id)
        {
            throw new BusinessException(OrdersErrorCodes.SuccessorIsSelf);
        }

        SuccessiveWaypointTypeId = successiveWaypointTypeId;
    }

    public void SetDefaults(Guid? relationCategoryId, Guid? defaultRelationId)
    {
        RelationCategoryId = relationCategoryId;
        DefaultRelationId = defaultRelationId;
    }

    public void SetDefaultDuration(int? minutes)
    {
        if (minutes is < 0 or > OrdersConsts.MaxDefaultDurationMinutes)
        {
            throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", "defaultDurationMinutes").WithData("value", minutes.Value);
        }

        DefaultDurationMinutes = minutes;
    }

    /// <summary>
    /// Replaces the on-board computer codes: at most <see cref="OrdersConsts.MaxFleetManagementCodes"/>, one per vendor
    /// (the vendor compared without case). Rows of a vendor that stays are updated in place.
    /// </summary>
    public void SetFleetManagementCodes(IEnumerable<(string Vendor, string Code)> codes, Func<Guid> newId)
    {
        var wanted = codes
            .Select(c => (Vendor: WaypointTypeFleetManagementCode.NormalizeVendor(c.Vendor), Code: WaypointTypeFleetManagementCode.NormalizeCode(c.Code)))
            .ToList();
        if (wanted.Count > OrdersConsts.MaxFleetManagementCodes)
        {
            throw new BusinessException(ReferenceDataErrorCodes.TooManyChildren).WithData("max", OrdersConsts.MaxFleetManagementCodes).WithData("field", "fleetManagementCodes");
        }

        if (wanted.Select(w => w.Vendor.ToUpperInvariant()).Distinct().Count() != wanted.Count)
        {
            throw new BusinessException(ReferenceDataErrorCodes.DuplicateKey).WithData("field", "fleetManagementCodes").WithData("value", string.Empty);
        }

        var keep = new HashSet<Guid>();
        foreach (var (vendor, code) in wanted)
        {
            var existing = FleetManagementCodes.FirstOrDefault(c => string.Equals(c.Vendor, vendor, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                existing = new WaypointTypeFleetManagementCode(newId(), TenantId, Id, vendor, code);
                FleetManagementCodes.Add(existing);
            }
            else
            {
                existing.Set(vendor, code);
            }

            keep.Add(existing.Id);
        }

        foreach (var stale in FleetManagementCodes.Where(c => !keep.Contains(c.Id)).ToList())
        {
            FleetManagementCodes.Remove(stale);
        }
    }
}

/// <summary>The code an on-board computer of one vendor uses for the activity of a waypoint type (OpenTMS FMS codes).</summary>
public class WaypointTypeFleetManagementCode : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid WaypointTypeId { get; private set; }

    public string Vendor { get; private set; } = default!;

    public string Code { get; private set; } = default!;

    protected WaypointTypeFleetManagementCode()
    {
    }

    public WaypointTypeFleetManagementCode(Guid id, Guid? tenantId, Guid waypointTypeId, string vendor, string code)
        : base(id)
    {
        TenantId = tenantId;
        WaypointTypeId = waypointTypeId;
        Set(vendor, code);
    }

    public void Set(string vendor, string code)
    {
        Vendor = NormalizeVendor(vendor);
        Code = NormalizeCode(code);
    }

    public static string NormalizeVendor(string vendor)
        => Check.NotNullOrWhiteSpace(vendor, nameof(vendor), OrdersConsts.MaxFleetManagementVendorLength).Trim();

    public static string NormalizeCode(string code)
        => Check.NotNullOrWhiteSpace(code, nameof(code), OrdersConsts.MaxFleetManagementCodeLength).Trim();
}
