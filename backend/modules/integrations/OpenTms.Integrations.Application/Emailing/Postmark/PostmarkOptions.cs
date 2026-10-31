namespace OpenTms.Integrations.Emailing.Postmark;

/// <summary>
/// The Postmark server (design 64), configuration section <c>Integrations:Postmark</c>. One FiLogic server for all
/// tenants: host configuration, not tenant data. <see cref="ServerToken"/> and <see cref="WebhookPassword"/> come from the
/// deployment's secret store (environment variables from Secret Manager; <c>dotnet user-secrets</c> locally): never in a
/// database or an appsettings file, never returned, never logged.
/// </summary>
public class PostmarkOptions
{
    public const string SectionName = "Integrations:Postmark";

    /// <summary>A trailing slash is added when missing.</summary>
    public string BaseUrl { get; set; } = "https://api.postmarkapp.com/";

    /// <summary>The Server API token: header <c>X-Postmark-Server-Token</c>.</summary>
    public string? ServerToken { get; set; }

    /// <summary>The transactional stream.</summary>
    public string MessageStream { get; set; } = "outbound";

    /// <summary>The From of every mail: an address on the verified sending domain.</summary>
    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = "OpenTMS";

    public bool TrackOpens { get; set; } = true;

    /// <summary>Postmark's link tracking: None, HtmlAndText, HtmlOnly or TextOnly.</summary>
    public string TrackLinks { get; set; } = "HtmlAndText";

    /// <summary>The Basic credentials Postmark sends with every webhook call (set in the webhook URL).</summary>
    public string? WebhookUsername { get; set; }

    public string? WebhookPassword { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ServerToken) && !string.IsNullOrWhiteSpace(FromAddress);

    public bool IsWebhookConfigured => !string.IsNullOrWhiteSpace(WebhookUsername) && !string.IsNullOrWhiteSpace(WebhookPassword);
}
