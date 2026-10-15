using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.Migrations
{
    /* The contract step of MoveFeatureValuesToAppTree (constitution rule 2, docs/designs/tenants-and-editions.md,
     * "Migration"): the stored values and the definition rows of the four features the app tree replaced are removed.
     * Their definitions left the code with the expand step, and its values were copied to the app tree there, so
     * nothing reads these rows any more. Separate PR, later than the expand step; no release carrying the old names was
     * ever deployed. Feature groups that only held old features go too, but never a group that still holds a feature.
     * Raw SQL: ABP's feature-management tables, not entities of ours. Tenant databases hold no feature values, so
     * there it changes nothing. */
    public partial class RemoveOldFeatureNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "AbpFeatureValues"
                WHERE "Name" IN ('MasterData.FirstRun', 'MasterData.Relations', 'Platform.Maintenance', 'UI.CommonToolbar');

                DELETE FROM "AbpFeatures"
                WHERE "Name" IN ('MasterData.FirstRun', 'MasterData.Relations', 'Platform.Maintenance', 'UI.CommonToolbar');

                DELETE FROM "AbpFeatureGroups" g
                WHERE g."Name" IN ('MasterData', 'Platform', 'UI')
                  AND NOT EXISTS (SELECT 1 FROM "AbpFeatures" f WHERE f."GroupName" = g."Name");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            /* Nothing to restore: the removed values were already copied to the app tree by MoveFeatureValuesToAppTree, and
             * no code reads the old names. Going back further than that migration therefore leaves those tenants and
             * editions on the defaults of the old features. */
        }
    }
}
