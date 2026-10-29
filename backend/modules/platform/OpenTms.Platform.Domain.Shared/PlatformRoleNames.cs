namespace OpenTms.Platform;

/// <summary>The static roles the Platform seeds.</summary>
public static class PlatformRoleNames
{
    /// <summary>
    /// Host role (PO 2026-10-29, docs/designs/63-user-menu.md): its members see every tenant in the user menu and log in
    /// to one through "Log in as". It holds the Impersonation permission only, not the Tenants app.
    /// </summary>
    public const string CustomerSuccessManager = "Customer Success Manager";
}
