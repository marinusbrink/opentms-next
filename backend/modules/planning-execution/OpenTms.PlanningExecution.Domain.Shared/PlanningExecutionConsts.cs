namespace OpenTms.PlanningExecution;

/// <summary>The bounds of the Planning &amp; Execution aggregates (design 59, security quickscan: bounded children and texts).</summary>
public static class PlanningExecutionConsts
{
    public const int MaxVehiclesPerTrip = 3;

    /// <summary>UI-GRID-013: the default row cap of a list export (setting <c>Planning:Export:MaxRows</c>).</summary>
    public const int DefaultExportMaxRows = 50_000;

    /// <summary>The columns one export may name.</summary>
    public const int MaxExportColumns = 100;

    /// <summary>The export reads the list in pages of this size (one batch of lookups per page).</summary>
    public const int ExportPageSize = 500;

    public const int MaxExportFieldLength = 64;

    public const int MaxExportHeaderLength = 128;

    public const int MaxDriversPerTrip = 3;

    public const int MaxStopsPerTrip = 100;

    /// <summary>Legs one assignment command may name.</summary>
    public const int MaxLegsPerCommand = 200;

    public const int MaxAttributes = 20;

    public const int MaxAttributeKeyLength = 32;

    public const int MaxAttributeValueLength = 500;

    public const int MaxNumberLength = 32;

    public const int MaxNameLength = 128;

    public const int MaxRemarksLength = 2500;

    public const int MaxReferenceLength = 128;

    public const int MaxSnapshotNameLength = 256;

    public const int MaxCityLength = 128;

    public const int MaxCountryCodeLength = 2;

    public const int MaxTimeZoneIdLength = 64;

    public const int MaxProviderLength = 32;

    public const int MaxFactorSetLength = 64;

    public const int MaxWarningDataLength = 256;

    /// <summary>The searchable text of a trip (number, name, carrier, vehicles, customers, stop cities), lower-cased.</summary>
    public const int MaxSearchKeyLength = 2000;

    /// <summary>OpenTMS's default speed correction: routed durations × 1.2.</summary>
    public const int DefaultSpeedCorrectionPercent = 120;

    /// <summary>The attribute definition key (OpenTMS custom property name).</summary>
    public const int MaxAttributeDefinitionKeyLength = MaxAttributeKeyLength;
}
