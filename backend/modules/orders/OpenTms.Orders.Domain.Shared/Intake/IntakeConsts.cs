namespace OpenTms.Orders.Intake;

/// <summary>
/// The bounds of the order intake record, its aliases and the canonical intake model (design 72). Texts of the model
/// are bounded as they arrive from a document or the API; where the order has a field of its own the order's bound
/// applies (<c>OrdersConsts</c>), so a resolved intake always fits the order it becomes.
/// </summary>
public static class IntakeConsts
{
    /// <summary>
    /// The tenant feature that switches the order intake on (design 72 "Features"). Defined by Integrations
    /// (<c>IntegrationsFeatures.OrderIntake</c>); Orders never references Integrations (module boundary rule 2), so the name
    /// is repeated here as a literal and must stay equal to it.
    /// </summary>
    public const string OrderIntakeFeature = "Integrations.OrderIntake";

    /// <summary>The order types and waypoint types the resolver and the learning context read at most.</summary>
    public const int MaxTypes = 500;

    /* Fuzzy matching (design 72 build note 25, PO decision 2026-11-03). The threshold is the Integrations tenant setting
     * Integrations.OrderIntake.NameSimilarityThreshold, handed over with every submission and kept on the intake. */

    /// <summary>The name similarity a candidate needs when the channel gives no threshold.</summary>
    public const decimal DefaultNameSimilarityThreshold = 0.75m;

    public const decimal MinNameSimilarityThreshold = 0.5m;

    public const decimal MaxNameSimilarityThreshold = 0.95m;

    /// <summary>How far the best similar candidate must lead the runner-up (when that one passes the threshold too).</summary>
    public const decimal NameSimilarityMargin = 0.10m;

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

    /// <summary>The error code kept on the pseudo-field <c>order</c> of a refused order creation.</summary>
    public const int MaxErrorCodeLength = 128;

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
