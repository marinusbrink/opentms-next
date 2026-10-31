namespace OpenTms.Platform.Emailing;

/// <summary>Column limits of the send log (design 64).</summary>
public static class EmailMessageConsts
{
    /// <summary>Comma-separated addresses of To, Cc or Bcc.</summary>
    public const int MaxAddressListLength = 2000;

    public const int MaxReplyToLength = 512;

    /// <summary>RFC 5322's line limit.</summary>
    public const int MaxSubjectLength = 998;

    public const int MaxTagLength = 100;

    public const int MaxProviderMessageIdLength = 128;

    public const int MaxLastErrorLength = 1000;

    public const int MaxBounceTypeLength = 64;

    public const int MaxBounceDescriptionLength = 1000;

    /// <summary>The error code of a send whose outcome is unknown (timed out after the request left).</summary>
    public const string OutcomeUnknownCode = "OutcomeUnknown";
}
