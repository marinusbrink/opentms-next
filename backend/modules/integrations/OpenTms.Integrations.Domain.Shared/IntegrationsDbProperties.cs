namespace OpenTms.Integrations;

public static class IntegrationsDbProperties
{
    /// <summary>PostgreSQL schema owned by the Integrations module. One schema per module; no other module may touch it.</summary>
    public const string DbSchema = "integrations";

    /// <summary>Named connection string; falls back to "Default" (ABP resolution), so database-per-tenant works unchanged.</summary>
    public const string ConnectionStringName = "Integrations";
}
