namespace OpenTms.Platform;

/// <summary>
/// The static roles the Platform seeds. A role's name is stored as written here and shown through the localization key
/// <c>Role:&lt;name&gt;</c> (docs/designs/71-predefined-roles.md).
/// </summary>
public static class PlatformRoleNames
{
    /// <summary>
    /// Host role (PO 2026-10-29, docs/designs/63-user-menu.md): its members see every tenant in the user menu and log in
    /// to one through "Log in as". It holds the Impersonation permission only, not the Tenants app.
    /// </summary>
    public const string CustomerSuccessManager = "Customer Success Manager";

    /* The predefined roles of design 71, seeded by PredefinedRoleDataSeedContributor from PredefinedRoleOptions. */

    /// <summary>Tenant role: takes transport orders in and keeps them correct.</summary>
    public const string OrderEntry = "Order entry";

    /// <summary>Tenant role: the planboard, trips and their execution.</summary>
    public const string Planner = "Planner";

    /// <summary>Tenant role: rates, financial settings and approving orders.</summary>
    public const string Finance = "Finance";

    /// <summary>Tenant role: every domain's lists in the Master data app.</summary>
    public const string MasterDataManager = "Master data manager";

    /// <summary>Tenant role: every view permission, no actions.</summary>
    public const string ReadOnly = "Read-only";

    /// <summary>Host role: maintenance, logbooks and provisioning.</summary>
    public const string PlatformOperator = "Platform operator";

    /// <summary>Host role: tenants and editions, without deleting them.</summary>
    public const string TenantManager = "Tenant & edition manager";
}
