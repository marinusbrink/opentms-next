using System;

namespace OpenTms.Platform;

/// <summary>Field lengths and bounds of the Platform master-data lists (docs/designs/49-platform-master-data.md).</summary>
public static class PlatformMasterDataConsts
{
    public const int MaxNameLength = 128;

    /// <summary>As Master Data (PO decision at gate 1, 2026-10-11): descriptions hold up to 2,500 characters.</summary>
    public const int MaxDescriptionLength = 2500;

    public const int MaxCodeLength = 32;
    public const int MaxCultureLength = 10;

    /// <summary>A list page is at most this many rows (the Platform grid contract uses the same bound).</summary>
    public const int MaxPageSize = 200;

    /// <summary>A lookup (picker) returns at most this many rows.</summary>
    public const int MaxLookupTake = 500;

    public const int MaxSearchLength = 200;

    // E-mail templates
    public const int MaxEmailAddressFieldLength = 512;
    public const int MaxSubjectLength = 512;
    public const int MaxHtmlBodyLength = 200_000;

    // Report definitions
    public const int MaxProviderTemplateIdLength = 128;
    public const int MaxFileNamePatternLength = 255;
    public const string PdfExtension = ".pdf";

    // Document types
    public const int MaxTransformationArgumentLength = 255;

    /// <summary>One row per transformation kind at most.</summary>
    public const int MaxDocumentTypeTransformations = 4;

    /// <summary>The match timeout a SplitPdfByReferencePattern argument is compiled with.</summary>
    public static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromSeconds(1);

    // Number sequences
    public const int MaxAffixLength = 16;
    public const int MinMinimumDigits = 1;
    public const int MaxMinimumDigits = 12;
}
