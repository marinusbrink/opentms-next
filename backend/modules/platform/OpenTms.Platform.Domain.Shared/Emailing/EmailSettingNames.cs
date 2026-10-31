namespace OpenTms.Platform.Emailing;

/// <summary>
/// The host's e-mail settings (design 64, "Host e-mail settings"): global values in the host database that apply to every
/// tenant. A value left empty falls back to the deployment's configuration (<c>Integrations:Postmark</c>). The token and
/// the webhook password are stored encrypted and never returned.
/// </summary>
public static class EmailSettingNames
{
    private const string Prefix = "Platform.Email.";

    /// <summary>The mail service's server token (Postmark's Server API token). Encrypted.</summary>
    public const string ServerToken = Prefix + "ServerToken";

    public const string FromAddress = Prefix + "FromAddress";

    public const string FromName = Prefix + "FromName";

    /// <summary>"true" or "false".</summary>
    public const string TrackOpens = Prefix + "TrackOpens";

    /// <summary>One of <see cref="EmailSettingsConsts.TrackLinksValues"/>.</summary>
    public const string TrackLinks = Prefix + "TrackLinks";

    public const string WebhookUsername = Prefix + "WebhookUsername";

    /// <summary>Encrypted.</summary>
    public const string WebhookPassword = Prefix + "WebhookPassword";
}

public static class EmailSettingsConsts
{
    /// <summary>Postmark's link tracking modes.</summary>
    public static readonly string[] TrackLinksValues = ["None", "HtmlAndText", "HtmlOnly", "TextOnly"];

    /// <summary>The host's webhook endpoint for delivery reports, relative to the API's own URL.</summary>
    public const string WebhookPath = "api/integrations/postmark/webhook";

    public const int MaxFromNameLength = 100;

    public const int MaxSecretLength = 256;

    /// <summary>The tag of a test mail on the send log.</summary>
    public const string TestEmailTag = "TestEmail";
}
