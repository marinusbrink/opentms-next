using System;
using System.Text.RegularExpressions;
using OpenTms.Platform.Localization;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;

namespace OpenTms.PlanningExecution.TransportTrips;

/// <summary>
/// A tenant's custom trip attribute (OpenTMS custom property, design 56 "Attributes", PO decision Q12): the key the
/// trip's <c>Attributes</c> bag uses, its nl/en label and its position in the editor. Keys are unique per tenant.
/// </summary>
public partial class TripAttributeDefinition : ReferenceDataAggregateRoot
{
    public string Key { get; private set; } = default!;

    public LocalizedName Name { get; private set; } = default!;

    public int Position { get; private set; } = 1;

    public bool IsArchived { get; private set; }

    protected TripAttributeDefinition()
    {
    }

    public TripAttributeDefinition(Guid id, Guid? tenantId, string key, LocalizedName name)
        : base(id, tenantId)
    {
        SetKey(key);
        SetName(name);
    }

    public static string NormalizeKey(string? key)
    {
        var trimmed = (key ?? string.Empty).Trim();
        if (trimmed.Length == 0 || trimmed.Length > PlanningExecutionConsts.MaxAttributeDefinitionKeyLength || !KeyShape().IsMatch(trimmed))
        {
            throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", "key").WithData("value", trimmed);
        }

        return trimmed;
    }

    public void SetKey(string key) => Key = NormalizeKey(key);

    public void SetName(LocalizedName name) => Name = RequireName(name);

    public void SetPosition(int position) => Position = position >= 1 ? position : throw new BusinessException(PlanningExecutionErrorCodes.InvalidPosition).WithData("value", position);

    public void SetArchived(bool archived) => IsArchived = archived;

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]*$")]
    private static partial Regex KeyShape();
}
