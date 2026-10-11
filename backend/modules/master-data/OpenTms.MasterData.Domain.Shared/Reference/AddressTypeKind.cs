namespace OpenTms.MasterData.Reference;

/// <summary>
/// What an address type means to code: a stop or a document asks for "the visiting address" by kind,
/// never by name (assessment §2.1a). At most one Visiting and one Postal type exist per tenant.
/// </summary>
public enum AddressTypeKind
{
    Other = 0,
    Visiting = 1,
    Postal = 2
}
