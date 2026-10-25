using OpenTms.Platform.NumberSequences;

namespace OpenTms.Orders.ConsignmentNotes;

/// <summary>The bounds and fixed keys of the consignment note (design 58 "CMR", design 56 "Numbers").</summary>
public static class ConsignmentNoteConsts
{
    public const int MinCopies = 1;

    public const int MaxCopies = 5;

    public const int DefaultCopies = 4;

    public const int MaxFixedDescriptionTextLength = 2000;

    public const int MaxBlobNameLength = 64;

    public const int MaxContentTypeLength = 128;

    public const int MaxNumberLength = 32;

    public const int Sha256Length = 64;

    /// <summary>The number sequence of every consignment note of a tenant (no type).</summary>
    public const string NumberSequenceKey = "Orders.ConsignmentNote";

    /// <summary>The ABP blob container the rendered documents live in (AD-09; the provider is configuration).</summary>
    public const string BlobContainerName = "consignment-notes";

    /// <summary>The seconds a print waits for the renderer before it answers RenderTimedOut (design 58, Cost &amp; SLO).</summary>
    public const int RenderTimeoutSeconds = 30;

    /// <summary>"CMR-2026-000001": the format of design 56.</summary>
    public static NumberSequenceFormat NumberFormat => NumberSequenceFormat.Create("CMR", includeYear: true, includeMonth: false, separator: "-", digits: 6);
}
