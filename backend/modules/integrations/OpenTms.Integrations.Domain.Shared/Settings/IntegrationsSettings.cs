namespace OpenTms.Integrations.Settings;

/// <summary>The tenant settings of the Integrations module (design 72); the host's service settings are <c>ServiceSettingNames</c>.</summary>
public static class IntegrationsSettings
{
    /// <summary>
    /// The confirmations without a correction after which a proposed import profile becomes Active (1–20, default
    /// <see cref="OrderIntake.OrderIntakeConsts.DefaultProfileActivationThreshold"/>). Per tenant, no screen in this run.
    /// </summary>
    public const string ProfileActivationThreshold = "Integrations.OrderIntake.ProfileActivationThreshold";

    /// <summary>
    /// The name similarity (pg_trgm similarity, 0.5–0.95, default
    /// <see cref="OrderIntake.OrderIntakeConsts.DefaultNameSimilarityThreshold"/>) a customer, address, transport unit,
    /// product or temperature needs to be matched by a similar name (design 72 build note 25). Per tenant, set in the
    /// Integrations app (<c>/api/integrations/order-intake-settings</c>) and handed to Orders with every intake.
    /// </summary>
    public const string NameSimilarityThreshold = "Integrations.OrderIntake.NameSimilarityThreshold";

    /// <summary>
    /// When the tenant administrator accepted the data sharing of the AI connector (design 74), as an ISO 8601 UTC instant;
    /// unset means not accepted. Tenant only, not visible to clients other than through the status endpoint.
    /// </summary>
    public const string AiConnectorAcceptedAt = "Integrations.AiConnector.AcceptedAt";

    /// <summary>Who accepted (a user id). Cleared when that user is deleted (design 74 "GDPR"); the acceptance stands.</summary>
    public const string AiConnectorAcceptedByUserId = "Integrations.AiConnector.AcceptedByUserId";
}
