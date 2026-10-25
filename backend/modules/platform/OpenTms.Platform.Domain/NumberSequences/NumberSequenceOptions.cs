namespace OpenTms.Platform.NumberSequences;

/// <summary>Bound from the "NumberSequences" configuration section (design 50, Assumption 2).</summary>
public class NumberSequenceOptions
{
    /// <summary>The IANA time zone whose local date decides the year and month of a number.</summary>
    public string TimeZone { get; set; } = "Europe/Amsterdam";
}
