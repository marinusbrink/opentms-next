using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Platform.Localization;
using OpenTms.Platform.NumberSequences;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Orders.TransportOrders;

/// <summary>
/// The type of a transport order (OpenTMS TransportOrderType), design 51: its number format, the stops a new order of the
/// type starts with (a child table, assessment §3.3, not a GUID string) and the fields an order of the type must have
/// (a flags set, §3.9). Exactly one default per tenant (§3.8).
/// </summary>
public class TransportOrderType : ReferenceDataAggregateRoot, IHasReferenceDataDefault
{
    public string Code { get; private set; } = default!;

    public LocalizedName Name { get; private set; } = default!;

    public bool IsDefault { get; private set; }

    public NumberSequenceFormat Numbering { get; private set; } = default!;

    public TransportOrderRequiredFields RequiredFields { get; private set; }

    public bool IsArchived { get; private set; }

    public virtual ICollection<TransportOrderTypeDefaultWaypoint> DefaultWaypoints { get; private set; } = new List<TransportOrderTypeDefaultWaypoint>();

    protected TransportOrderType()
    {
    }

    public TransportOrderType(Guid id, Guid? tenantId, string code, LocalizedName name, NumberSequenceFormat numbering)
        : base(id, tenantId)
    {
        SetCode(code);
        SetName(name);
        SetNumbering(numbering);
    }

    /// <summary>The key of this type's number sequence (design 50).</summary>
    public static string SequenceKeyOf(Guid id) => "Orders.TransportOrderType:" + id;

    public string SequenceKey => SequenceKeyOf(Id);

    public void SetCode(string code) => Code = NormalizeCode(code);

    public void SetName(LocalizedName name) => Name = RequireName(name);

    public void SetNumbering(NumberSequenceFormat numbering) => Numbering = Check.NotNull(numbering, nameof(numbering));

    public void SetRequiredFields(TransportOrderRequiredFields requiredFields)
    {
        if (!requiredFields.IsValid())
        {
            throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", "requiredFields").WithData("value", (int)requiredFields);
        }

        RequiredFields = requiredFields;
    }

    public void SetArchived(bool isArchived)
    {
        if (isArchived && IsDefault)
        {
            throw new BusinessException(ReferenceDataErrorCodes.DefaultRequired).WithData("reason", "archive");
        }

        IsArchived = isArchived;
    }

    public void SetDefault(bool isDefault) => IsDefault = isDefault;

    /// <summary>
    /// Replaces the default stops with <paramref name="waypointTypeIds"/> in that order (positions 1, 2, …); a waypoint
    /// type may occur more than once (two loading stops). At most <see cref="OrdersConsts.MaxDefaultWaypoints"/>. The
    /// service checks first that every type exists.
    /// </summary>
    public void SetDefaultWaypoints(IReadOnlyList<Guid> waypointTypeIds)
    {
        if (waypointTypeIds.Count > OrdersConsts.MaxDefaultWaypoints)
        {
            throw new BusinessException(ReferenceDataErrorCodes.TooManyChildren).WithData("max", OrdersConsts.MaxDefaultWaypoints).WithData("field", "defaultWaypoints");
        }

        foreach (var stale in DefaultWaypoints.Where(w => w.Position > waypointTypeIds.Count).ToList())
        {
            DefaultWaypoints.Remove(stale);
        }

        for (var i = 0; i < waypointTypeIds.Count; i++)
        {
            var position = i + 1;
            var line = DefaultWaypoints.FirstOrDefault(w => w.Position == position);
            if (line == null)
            {
                DefaultWaypoints.Add(new TransportOrderTypeDefaultWaypoint(TenantId, Id, position, waypointTypeIds[i]));
            }
            else
            {
                line.SetWaypointType(waypointTypeIds[i]);
            }
        }
    }

    /// <summary>Before a soft delete: the stops go, so no deleted type keeps a waypoint type in use.</summary>
    public void ClearDefaultWaypoints() => DefaultWaypoints.Clear();
}

/// <summary>A stop a new order of the type starts with, at a position (1-based); composite key (type, position).</summary>
public class TransportOrderTypeDefaultWaypoint : Entity, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid TransportOrderTypeId { get; private set; }

    public int Position { get; private set; }

    public Guid WaypointTypeId { get; private set; }

    protected TransportOrderTypeDefaultWaypoint()
    {
    }

    public TransportOrderTypeDefaultWaypoint(Guid? tenantId, Guid transportOrderTypeId, int position, Guid waypointTypeId)
    {
        TenantId = tenantId;
        TransportOrderTypeId = transportOrderTypeId;
        Position = position;
        WaypointTypeId = waypointTypeId;
    }

    public void SetWaypointType(Guid waypointTypeId) => WaypointTypeId = waypointTypeId;

    public override object[] GetKeys() => [TransportOrderTypeId, Position];
}
