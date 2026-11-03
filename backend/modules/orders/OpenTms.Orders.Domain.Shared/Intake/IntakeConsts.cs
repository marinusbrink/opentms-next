namespace OpenTms.Orders.Intake;

/// <summary>
/// The bounds of the order intake record, its aliases and the canonical intake model (design 72). Texts of the model
/// are bounded as they arrive from a document or the API; where the order has a field of its own the order's bound
/// applies (<c>OrdersConsts</c>), so a resolved intake always fits the order it becomes.
/// </summary>
public static class IntakeConsts
{
    /* The intake record. */

    /// <summary>The Integrations channel's name ("Gmail", "Microsoft365", "Api").</summary>
    public const int MaxChannelLength = 32;

    /// <summary>An e-mail address (RFC 5321).</summary>
    public const int MaxSenderAddressLength = 320;

    public const int MaxSubjectLength = 512;

    public const int MaxFileNameLength = 256;

    public const int MaxRejectReasonLength = 512;

    /// <summary>The candidates a resolution offers per field.</summary>
    public const int MaxCandidates = 5;

    /// <summary>A field path such as <c>lines[0].stops[1].address.postalCode</c>.</summary>
    public const int MaxFieldPathLength = 128;

    /// <summary>The picks one resolve request may carry.</summary>
    public const int MaxPicks = 500;

    public const int MaxCandidateDetailLength = 256;

    /* Aliases. */

    /// <summary>The normalised text an alias matches (lower case, single spaces).</summary>
    public const int MaxAliasTextLength = 512;

    /* The canonical model (OrderIntakeDto). */

    /// <summary>Names as a document gives them: a customer, an address, a type, a unit, a product, a temperature.</summary>
    public const int MaxNameLength = 256;

    public const int MaxCodeLength = 64;

    public const int MaxEmailAddressLength = 320;

    public const int MaxVatNumberLength = 32;

    public const int MaxChamberOfCommerceNumberLength = 32;

    public const int MaxStreetLength = 256;

    public const int MaxHouseNumberLength = 32;

    public const int MaxPostalCodeLength = 32;

    /// <summary>As received; the resolver matches ISO 3166-1 alpha-2 codes.</summary>
    public const int MaxCountryCodeLength = 64;

    /// <summary>An address as one text when the document does not split it.</summary>
    public const int MaxFullTextLength = 1000;

    public const int MaxContactNameLength = 128;

    public const int MaxContactPhoneLength = 64;

    /// <summary>A date as <c>yyyy-MM-dd</c>.</summary>
    public const int DateLength = 10;

    /// <summary>A time as <c>HH:mm</c>.</summary>
    public const int TimeLength = 5;

    public const string DateFormat = "yyyy-MM-dd";

    public const string TimeFormat = "HH:mm";

    /// <summary>The stops of one line in the canonical model.</summary>
    public const int MaxStopsPerLine = 50;
}
