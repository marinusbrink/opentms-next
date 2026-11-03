namespace OpenTms.Integrations.OrderIntake.Channels;

/// <summary>
/// The OAuth clients of the mailbox connections (design 72 "Channels", host tab Integrations): deployment defaults from
/// "Integrations:OrderIntake:Mail", overlaid by the host's saved settings (<c>ServiceSettingsPostConfigure</c>). One Google
/// OAuth client and one multi-tenant Entra app registration serve every tenant.
/// </summary>
public class MailOAuthOptions
{
    public const string SectionName = "Integrations:OrderIntake:Mail";

    public MailOAuthClientOptions Google { get; set; } = new();

    public MailOAuthClientOptions Microsoft { get; set; } = new();

    public MailOAuthClientOptions For(MailProvider provider) => provider == MailProvider.Gmail ? Google : Microsoft;
}

public class MailOAuthClientOptions
{
    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
