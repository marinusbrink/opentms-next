namespace OpenTms.Platform.DerivedValues;

/// <summary>Bounds of the derived-value mechanism (design 49).</summary>
public static class DerivedValueConsts
{
    /// <summary>"&lt;Module&gt;.&lt;Entity&gt;", e.g. "PlanningExecution.TransportTrip".</summary>
    public const int MaxTargetTypeLength = 64;

    public const int MaxReasonLength = 64;

    public const int MaxErrorCodeLength = 128;

    /// <summary>One bulk re-run requests at most this many records.</summary>
    public const int MaxTargetsPerRequest = 10_000;

    /// <summary>One request event carries at most this many records (the outbox row stays small).</summary>
    public const int MaxTargetsPerEvent = 1_000;

    /// <summary>One states read returns at most this many records.</summary>
    public const int MaxStatesPerRead = 500;

    /// <summary>One hub Watch call joins at most this many records.</summary>
    public const int MaxWatchPerCall = 200;
}
