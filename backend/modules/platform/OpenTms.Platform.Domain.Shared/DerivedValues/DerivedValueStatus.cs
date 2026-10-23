namespace OpenTms.Platform.DerivedValues;

/// <summary>The calculation state of one record's derived values (design 49).</summary>
public enum DerivedValueStatus
{
    /// <summary>A change was saved; the stored values are about to be recalculated ("calculating").</summary>
    Pending = 1,

    /// <summary>The stored values belong to the latest saved version.</summary>
    UpToDate = 2,

    /// <summary>The latest attempt failed; the previous values are kept and the job is retried.</summary>
    Failed = 3
}
