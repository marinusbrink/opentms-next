using System;
using System.Linq;
using OpenTms.Platform.Localization;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;

namespace OpenTms.Orders.TransportOrders;

/// <summary>
/// A tenant-defined attribute of orders or lines (design 56 Q12, design 58): the key the jsonb attributes use, its
/// label per language and its position in the editor. Keys are unique per target; archived definitions keep their
/// stored values readable but are offered no longer.
/// </summary>
public class OrderAttributeDefinition : ReferenceDataAggregateRoot
{
    public OrderAttributeTarget Target { get; private set; }

    public string Key { get; private set; } = default!;

    public LocalizedName Name { get; private set; } = default!;

    public int Position { get; private set; }

    public bool IsArchived { get; private set; }

    protected OrderAttributeDefinition()
    {
    }

    public OrderAttributeDefinition(Guid id, Guid? tenantId, OrderAttributeTarget target, string key, LocalizedName name, int position)
        : base(id, tenantId)
    {
        SetTarget(target);
        SetKey(key);
        SetName(name);
        SetPosition(position);
    }

    public void SetTarget(OrderAttributeTarget target) => Target = OrderValues.Defined(target, "target");

    /// <summary>Keys are trimmed, at most 32 characters, letters, digits, '-', '_' and '.' (the stored jsonb key).</summary>
    public void SetKey(string key) => Key = NormalizeKey(key);

    public static string NormalizeKey(string key)
    {
        var value = Check.NotNullOrWhiteSpace(key, nameof(key), OrdersConsts.MaxAttributeKeyLength).Trim();
        if (value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.'))
        {
            throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", "key").WithData("value", value);
        }

        return value;
    }

    public void SetName(LocalizedName name) => Name = RequireName(name);

    public void SetPosition(int position)
    {
        if (position < 1)
        {
            throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", "position").WithData("value", position);
        }

        Position = position;
    }

    public void SetArchived(bool isArchived) => IsArchived = isArchived;
}
