namespace OpenTms.Platform.Tenants;

/// <summary>
/// The extension properties Platform keeps on ABP's <c>Tenant</c>. <see cref="EditionId"/> is mapped to
/// a column (<c>AbpTenants.EditionId</c>, see OpenTmsEfCoreEntityExtensionMappings); the rest live in
/// the tenant's ExtraProperties and are only displayed.
/// </summary>
public static class TenantProperties
{
    public const string EditionId = "EditionId";

    public const string ProvisioningState = "ProvisioningState";

    /// <summary>Exception type and message of the last failed run; never a connection string.</summary>
    public const string ProvisioningError = "ProvisioningError";

    public const string ProvisioningAt = "ProvisioningAt";

    /// <summary>
    /// The tenant administrator's email address, kept only until provisioning succeeds so a retry can still seed
    /// the administrator; removed then. Not in the job arguments: Hangfire stores those in plain text.
    /// </summary>
    public const string PendingAdminEmail = "PendingAdminEmail";

    /// <summary>When the tenant administrator's invitation was sent; set once so a retry does not resend it.</summary>
    public const string InvitationSentAt = "InvitationSentAt";

    /// <summary>The request id of the last invitation that was sent; a redelivery of that job sends nothing (rule 3).</summary>
    public const string InvitationRequestId = "InvitationRequestId";
}
