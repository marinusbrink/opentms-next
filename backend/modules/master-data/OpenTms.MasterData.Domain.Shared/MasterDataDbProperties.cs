namespace OpenTms.MasterData;

public static class MasterDataDbProperties
{
    /// <summary>PostgreSQL schema owned by the MasterData module. One schema per module; no other module may touch it.</summary>
    public const string DbSchema = "masterdata";

    /// <summary>Named connection string; falls back to "Default" (ABP resolution), so database-per-tenant works unchanged.</summary>
    public const string ConnectionStringName = "MasterData";
}
