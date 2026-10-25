using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.Migrations
{
    /* The contract step of MoveMasterDataFeaturesUnderGeneral (constitution rule 2, docs/designs/tenants-and-editions.md,
     * "Migration for D15"; PO 2026-10-16): the stored values and the definition rows of the 19 master-data feature
     * names that moved under the domain group General are removed. Their definitions left the code with the expand
     * step, and its values were copied to the new names there, so nothing reads these rows any more. Separate migration,
     * later than the expand step; no release carrying the old names was ever deployed. The list is exactly the old side
     * of the expand step's mapping. After this step the old names (Apps.MasterData.Relations, .Transport, .Fleet,
     * .Reference and their children) are free again, so a later domain group may take one of them. The old features
     * sat in the group "Apps", which still holds every app feature, so no feature group goes (a group that still holds
     * a feature is never deleted). Raw SQL: ABP's feature-management tables, not entities of ours. Tenant databases
     * hold no feature values, so there it changes nothing. Running it twice deletes nothing the second time. */
    public partial class RemoveOldMasterDataFeatureNames : Migration
    {
        private const string OldNames = """
            (
            'Apps.MasterData.Relations',
            'Apps.MasterData.Relations.Relations',
            'Apps.MasterData.Relations.AddressTypes',
            'Apps.MasterData.Relations.RelationCategories',
            'Apps.MasterData.Transport',
            'Apps.MasterData.Transport.Products',
            'Apps.MasterData.Transport.TransportUnits',
            'Apps.MasterData.Transport.ProductRelationCodes',
            'Apps.MasterData.Transport.Temperatures',
            'Apps.MasterData.Fleet',
            'Apps.MasterData.Fleet.Vehicles',
            'Apps.MasterData.Fleet.VehicleCombinations',
            'Apps.MasterData.Fleet.Drivers',
            'Apps.MasterData.Fleet.VehicleTypes',
            'Apps.MasterData.Fleet.TransportCharacteristics',
            'Apps.MasterData.Reference',
            'Apps.MasterData.Reference.Countries',
            'Apps.MasterData.Reference.Currencies',
            'Apps.MasterData.Reference.GeographicalRegions'
            )
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                DELETE FROM "AbpFeatureValues"
                WHERE "Name" IN {OldNames};

                DELETE FROM "AbpFeatures"
                WHERE "Name" IN {OldNames};
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            /* Nothing to restore: the removed values were already copied to the General names by
             * MoveMasterDataFeaturesUnderGeneral, and no code reads the old names. Going back further than that migration
             * therefore leaves those tenants and editions on the defaults of the old features. */
        }
    }
}
