namespace OpenTms.MasterData.Features;

public static class MasterDataFeatures
{
    public const string GroupName = "MasterData";

    /// <summary>Per-tenant switch for the first run of the Master data app (constitution rule 4; default off).</summary>
    public const string FirstRun = GroupName + ".FirstRun";
}
