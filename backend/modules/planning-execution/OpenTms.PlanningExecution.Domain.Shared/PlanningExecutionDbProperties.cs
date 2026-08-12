namespace OpenTms.PlanningExecution;

public static class PlanningExecutionDbProperties
{
    /// <summary>PostgreSQL schema owned by the PlanningExecution module. One schema per module; no other module may touch it.</summary>
    public const string DbSchema = "planning";

    /// <summary>Named connection string; falls back to "Default" (ABP resolution), so database-per-tenant works unchanged.</summary>
    public const string ConnectionStringName = "PlanningExecution";
}
