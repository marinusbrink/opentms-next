namespace OpenTms.Platform;

public static class PlatformDbProperties
{
    /// <summary>PostgreSQL schema owned by the Platform module. One schema per module; no other module may touch it.</summary>
    public const string DbSchema = "platform";

    /// <summary>Named connection string; falls back to "Default" (ABP resolution), so database-per-tenant works unchanged.</summary>
    public const string ConnectionStringName = "Platform";
}
