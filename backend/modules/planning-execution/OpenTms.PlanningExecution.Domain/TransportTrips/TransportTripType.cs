using System;
using OpenTms.Platform.Localization;
using OpenTms.Platform.NumberSequences;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;

namespace OpenTms.PlanningExecution.TransportTrips;

/// <summary>
/// The type of a transport trip (OpenTMS: TransportFileType; PO 2026-10-22: TransportFile is TransportTrip), design 53:
/// its number format and how a new trip of the type is executed. Exactly one default per tenant (assessment §3.8).
/// </summary>
public class TransportTripType : ReferenceDataAggregateRoot, IHasReferenceDataDefault
{
    public string Code { get; private set; } = default!;

    public LocalizedName Name { get; private set; } = default!;

    public bool IsDefault { get; private set; }

    public NumberSequenceFormat Numbering { get; private set; } = default!;

    public TripExecution DefaultExecution { get; private set; } = TripExecution.OwnFleet;

    public bool SendToFleetManagementSystem { get; set; } = true;

    public bool IsArchived { get; private set; }

    protected TransportTripType()
    {
    }

    public TransportTripType(Guid id, Guid? tenantId, string code, LocalizedName name, NumberSequenceFormat numbering)
        : base(id, tenantId)
    {
        SetCode(code);
        SetName(name);
        SetNumbering(numbering);
    }

    /// <summary>The key of this type's number sequence (design 50).</summary>
    public static string SequenceKeyOf(Guid id) => "PlanningExecution.TransportTripType:" + id;

    public string SequenceKey => SequenceKeyOf(Id);

    public void SetCode(string code) => Code = NormalizeCode(code);

    public void SetName(LocalizedName name) => Name = RequireName(name);

    public void SetNumbering(NumberSequenceFormat numbering) => Numbering = Check.NotNull(numbering, nameof(numbering));

    public void SetDefaultExecution(TripExecution execution)
    {
        if (!Enum.IsDefined(execution))
        {
            throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", "defaultExecution").WithData("value", (int)execution);
        }

        DefaultExecution = execution;
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
}
