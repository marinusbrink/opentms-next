namespace OpenTms.Platform.ReferenceData;

/// <summary>
/// Field lengths and bounds of every domain's reference data that is built on the Platform base
/// (docs/designs/50-platform-reference-data-and-number-sequences.md); the values of design 41.
/// </summary>
public static class ReferenceDataConsts
{
    public const int MaxNameLength = 128;

    public const int MaxCodeLength = 16;

    public const int MaxDescriptionLength = 2500;

    public const int MaxSearchLength = 200;

    /// <summary>A list page is at most this many rows (the Platform grid contract uses the same bound).</summary>
    public const int MaxPageSize = 200;

    /// <summary>A lookup (picker) returns at most this many rows.</summary>
    public const int MaxLookupTake = 500;

    public const int LookupPageSize = 25;

    public const int MaxLedgerAccountLength = 32;

    public const int IsoAlpha2Length = 2;

    public const int IsoCurrencyCodeLength = 3;

    /// <summary>A bulk delete resolves at most this many rows.</summary>
    public const int MaxBulkDeleteRows = 1000;
}
