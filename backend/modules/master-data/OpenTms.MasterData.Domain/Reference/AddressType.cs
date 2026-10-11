using System;
using OpenTms.Platform.Localization;
using Volo.Abp;

namespace OpenTms.MasterData.Reference;

public class AddressType : MasterDataAggregateRoot
{
    public AddressTypeKind Kind { get; private set; }

    public LocalizedName Name { get; private set; } = default!;

    protected AddressType()
    {
    }

    public AddressType(Guid id, Guid? tenantId, AddressTypeKind kind, LocalizedName name)
        : base(id, tenantId)
    {
        SetKind(kind);
        SetName(name);
    }

    public void SetKind(AddressTypeKind kind)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Kind = kind;
    }

    public void SetName(LocalizedName name) => Name = Check.NotNull(name, nameof(name));
}
