namespace OpenTms.Platform.TestData;

/// <summary>The test datasets a host user can reset a tenant to (docs/designs/65-tenant-test-datasets.md).</summary>
public static class TestDatasets
{
    /// <summary>5,000 relations and a regular random amount (tens to hundreds) of every other record type.</summary>
    public const string Average = "Average";

    /// <summary>150,000 relations and over 1,000 of every other record type.</summary>
    public const string Large = "Large";

    public static readonly string[] All = { Average, Large };
}

/// <summary>Where the last "Reset to dataset" of a tenant stands.</summary>
public static class TestDatasetResetState
{
    public const string Queued = "Queued";
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
}
