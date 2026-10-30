using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;

namespace OpenTms.Orders.ConsignmentNotes;

/// <summary>
/// A consignment-note profile (design 58 "CMR", PO decision Q11; OpenTMS ConsignmentNoteProfile): how many copies, the
/// borders and QR code, grouping by leg, which party fills each party box, which goods columns, instruction and remark
/// items appear, and the fixed descriptions of boxes 6/13. One default per tenant. The report definition is optional:
/// without one the tenant's default of purpose ConsignmentNote renders.
/// </summary>
public class ConsignmentNoteProfile : ReferenceDataAggregateRoot, IHasReferenceDataDefault
{
    public string Name { get; private set; } = default!;

    public string NormalizedName { get; private set; } = default!;

    public bool IsDefault { get; private set; }

    public bool IsArchived { get; private set; }

    public int Copies { get; private set; } = ConsignmentNoteConsts.DefaultCopies;

    public bool ShowBorders { get; private set; } = true;

    public bool ShowQrCode { get; private set; } = true;

    public bool GroupByLeg { get; private set; }

    public CmrPartySource Box1Sender { get; private set; } = CmrPartySource.Customer;

    public CmrPartySource Box2Consignee { get; private set; } = CmrPartySource.DestinationAddress;

    public CmrPartySource Box3DeliveryPlace { get; private set; } = CmrPartySource.DestinationAddress;

    public CmrPartySource Box4TakingOverPlace { get; private set; } = CmrPartySource.OriginAddress;

    public CmrPartySource Box16Carrier { get; private set; } = CmrPartySource.Carrier;

    public CmrPartySource Box17SuccessiveCarrier { get; private set; } = CmrPartySource.Empty;

    public CmrPartySource Box21EstablishedBy { get; private set; } = CmrPartySource.OwnCompany;

    public List<CmrGoodsColumn> Box6Columns { get; private set; } = new();

    public List<CmrInstructionItem> Box13Items { get; private set; } = new();

    public List<CmrRemarkItem> Box18Items { get; private set; } = new();

    public List<Guid> FixedDescriptionIds { get; private set; } = new();

    /// <summary>The report definition (Platform, purpose ConsignmentNote) this profile renders with; null = the tenant's default.</summary>
    public Guid? ReportDefinitionId { get; private set; }

    protected ConsignmentNoteProfile()
    {
    }

    public ConsignmentNoteProfile(Guid id, Guid? tenantId, string name)
        : base(id, tenantId)
    {
        SetName(name);
        Box6Columns = OpenTmsDefaultColumns.ToList();
        Box13Items = OpenTmsDefaultInstructionItems.ToList();
        Box18Items = OpenTmsDefaultRemarkItems.ToList();
    }

    /// <summary>The OpenTMS defaults of the goods table (routing-co2-cmr.md §3.2).</summary>
    public static readonly CmrGoodsColumn[] OpenTmsDefaultColumns =
    [
        CmrGoodsColumn.Quantity, CmrGoodsColumn.TransportUnit, CmrGoodsColumn.Product, CmrGoodsColumn.WeightKg,
        CmrGoodsColumn.VolumeM3, CmrGoodsColumn.ColumnNames, CmrGoodsColumn.Totals
    ];

    public static readonly CmrInstructionItem[] OpenTmsDefaultInstructionItems = [CmrInstructionItem.ShipperInstructions, CmrInstructionItem.FixedDescriptions];

    public static readonly CmrRemarkItem[] OpenTmsDefaultRemarkItems = [CmrRemarkItem.OrderNumber, CmrRemarkItem.CustomerReference, CmrRemarkItem.Vehicles, CmrRemarkItem.LineRemarks];

    public void SetName(string name)
    {
        Name = NormalizeName(name);
        NormalizedName = Name.ToUpperInvariant();
    }

    public void SetDefault(bool isDefault) => IsDefault = isDefault;

    public void SetArchived(bool isArchived)
    {
        if (isArchived && IsDefault)
        {
            throw new BusinessException(ReferenceDataErrorCodes.DefaultRequired).WithData("reason", "archive");
        }

        IsArchived = isArchived;
    }

    public void SetLayout(int copies, bool showBorders, bool showQrCode, bool groupByLeg)
    {
        if (copies is < ConsignmentNoteConsts.MinCopies or > ConsignmentNoteConsts.MaxCopies)
        {
            throw new BusinessException(OrdersErrorCodes.InvalidCopies).WithData("min", ConsignmentNoteConsts.MinCopies).WithData("max", ConsignmentNoteConsts.MaxCopies);
        }

        Copies = copies;
        ShowBorders = showBorders;
        ShowQrCode = showQrCode;
        GroupByLeg = groupByLeg;
    }

    public void SetPartySources(CmrPartySource box1, CmrPartySource box2, CmrPartySource box3, CmrPartySource box4, CmrPartySource box16, CmrPartySource box17, CmrPartySource box21)
    {
        Box1Sender = Defined(box1, "box1Sender");
        Box2Consignee = Defined(box2, "box2Consignee");
        Box3DeliveryPlace = Defined(box3, "box3DeliveryPlace");
        Box4TakingOverPlace = Defined(box4, "box4TakingOverPlace");
        Box16Carrier = Defined(box16, "box16Carrier");
        Box17SuccessiveCarrier = Defined(box17, "box17SuccessiveCarrier");
        Box21EstablishedBy = Defined(box21, "box21EstablishedBy");
    }

    public void SetItems(IEnumerable<CmrGoodsColumn> box6, IEnumerable<CmrInstructionItem> box13, IEnumerable<CmrRemarkItem> box18)
    {
        Box6Columns = box6.Select(c => Defined(c, "box6Columns")).Distinct().ToList();
        Box13Items = box13.Select(c => Defined(c, "box13Items")).Distinct().ToList();
        Box18Items = box18.Select(c => Defined(c, "box18Items")).Distinct().ToList();
    }

    /// <summary>The service checks the ids exist (and are not archived) first.</summary>
    public void SetFixedDescriptions(IEnumerable<Guid> ids) => FixedDescriptionIds = ids.Where(id => id != Guid.Empty).Distinct().ToList();

    public void SetReportDefinition(Guid? reportDefinitionId) => ReportDefinitionId = reportDefinitionId;

    private static TEnum Defined<TEnum>(TEnum value, string field) where TEnum : struct, Enum
        => Enum.IsDefined(value) ? value : throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", field).WithData("value", Convert.ToInt32(value));
}

/// <summary>A reusable text for boxes 6 and 13 of the consignment note (OpenTMS ConsignmentNoteFixedDescription).</summary>
public class ConsignmentNoteFixedDescription : ReferenceDataAggregateRoot
{
    public string Name { get; private set; } = default!;

    public string NormalizedName { get; private set; } = default!;

    public string Text { get; private set; } = default!;

    public bool IsArchived { get; private set; }

    protected ConsignmentNoteFixedDescription()
    {
    }

    public ConsignmentNoteFixedDescription(Guid id, Guid? tenantId, string name, string text)
        : base(id, tenantId)
    {
        SetName(name);
        SetText(text);
    }

    public void SetName(string name)
    {
        Name = NormalizeName(name);
        NormalizedName = Name.ToUpperInvariant();
    }

    public void SetText(string text) => Text = Check.NotNullOrWhiteSpace(text, nameof(text), ConsignmentNoteConsts.MaxFixedDescriptionTextLength).Trim();

    public void SetArchived(bool isArchived) => IsArchived = isArchived;
}
