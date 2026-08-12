namespace OpenTms.Reporting;

public static class ReportingDbProperties
{
    /// <summary>PostgreSQL schema owned by the Reporting module. One schema per module; no other module may touch it.</summary>
    public const string DbSchema = "reporting";

    /// <summary>Named connection string; falls back to "Default" (ABP resolution), so database-per-tenant works unchanged.</summary>
    public const string ConnectionStringName = "Reporting";
}
