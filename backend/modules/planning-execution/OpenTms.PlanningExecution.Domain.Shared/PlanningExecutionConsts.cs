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

    /// <summary>Design 74 §10: a trip created on the Planboard arrives this long after its departure until a route says otherwise (legacy).</summary>
    public const int NewTripDefaultDurationHours = 2;

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

    /// <summary>An ISO 4217 currency code (the trip's toll currency).</summary>
    public const int MaxCurrencyCodeLength = 3;

    public const int MaxFactorSetLength = 64;

    public const int MaxWarningDataLength = 256;

    /// <summary>The searchable text of a trip (number, name, carrier, vehicles, customers, stop cities), lower-cased.</summary>
    public const int MaxSearchKeyLength = 2000;

    /// <summary>OpenTMS's default speed correction: routed durations × 1.2.</summary>
    public const int DefaultSpeedCorrectionPercent = 120;

    /// <summary>The attribute definition key (OpenTMS custom property name).</summary>
    public const int MaxAttributeDefinitionKeyLength = MaxAttributeKeyLength;

    /* Design 68 (Planboard): the bounds of its reads (D1, 200 ms at the volume dataset). */

    /// <summary>The longest Scheduler window in days.</summary>
    public const int MaxSchedulerWindowDays = 14;

    /// <summary>The largest page of rows (Scheduler resources, refetch ids, leg groups).</summary>
    public const int MaxPlanboardPageSize = 200;

    /// <summary>The most ids an <c>?ids=</c> filter of a GET read takes: 100 GUIDs keep the request line well under Kestrel's 8 KB.</summary>
    public const int MaxIdsPerQuery = 100;

    /// <summary>The bars one Scheduler read returns at most; more set <c>BarsTruncated</c>.</summary>
    public const int MaxSchedulerBars = 2000;

    /// <summary>The longest leg group key (a city or an id).</summary>
    public const int MaxGroupKeyLength = 128;

    /* Design 68 §2 (availability), §5 (optimisation) and the map reads. */

    /// <summary>The longest unavailability in days, and the longest repetition span.</summary>
    public const int MaxUnavailabilityDays = 366;

    /// <summary>The longest occurrence of a weekly repeating unavailability in hours.</summary>
    public const int MaxRecurringOccurrenceHours = 24;

    public const int MaxUnavailabilityRemarksLength = 512;

    /// <summary>The resource ids one availability read may name.</summary>
    public const int MaxAvailabilityResourceIds = MaxIdsPerQuery;

    /// <summary>The longest window of an availability read in days.</summary>
    public const int MaxAvailabilityWindowDays = 62;

    /// <summary>The trips one change asks to recalculate besides itself (the other side of a double booking, an unavailability).</summary>
    public const int MaxRecalculationFanOut = 200;

    /// <summary>The trips of the map's "all trips" layer.</summary>
    public const int MaxMapTrips = 500;

    /// <summary>The unplanned legs near a trip the map shows.</summary>
    public const int MaxUnplannedNearLegs = 200;

    /// <summary>The largest radius of the unplanned-legs-nearby search in kilometres.</summary>
    public const int MaxUnplannedNearRadiusKm = 100;

    /// <summary>The candidate legs the bounding box may yield before the exact distance filter.</summary>
    public const int MaxUnplannedNearCandidates = 2000;

    /// <summary>The longest date range of the unplanned-legs-nearby search in days.</summary>
    public const int MaxUnplannedNearDays = 31;
}
