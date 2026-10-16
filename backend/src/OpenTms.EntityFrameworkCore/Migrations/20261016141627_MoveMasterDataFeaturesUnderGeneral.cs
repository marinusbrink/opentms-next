using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.Migrations
{
    /* The Master data app holds the master data of every domain, one nav group per domain (PO decision 2026-10-16,
     * docs/designs/tenants-and-editions.md, "App features"). The Master Data domain's own lists move under the new
     * domain group General, so every master-data feature name gains the ".General" segment
     * (Apps.MasterData.Relations.Relations → Apps.MasterData.General.Relations.Relations). Every stored tenant ("T") and
     * edition ("E") value of an old name is copied to its new name, unless the new name already has a value for the
     * same provider and key. A copy's id is derived from its source row and its new name (md5 → uuid), so Down removes
     * exactly the copies and never a value that existed before. The new Apps.MasterData.General itself gets no stored
     * value: its default is on. Expand only (constitution rule 2): the old values stay, inert, so a previous release
     * running next to this one during a rolling deploy still finds them. Removing the old names is the contract step of
     * a later release (docs/designs/tenants-and-editions.md, "Migration"). Raw SQL: these are ABP's feature-management
     * tables, not entities of ours. Tenant databases hold no feature values, so there it changes nothing. */
    public partial class MoveMasterDataFeaturesUnderGeneral : Migration
    {
        private const string Mapping = """
            (VALUES
            ('Apps.MasterData.Relations', 'Apps.MasterData.General.Relations'),
            ('Apps.MasterData.Relations.Relations', 'Apps.MasterData.General.Relations.Relations'),
            ('Apps.MasterData.Relations.AddressTypes', 'Apps.MasterData.General.Relations.AddressTypes'),
            ('Apps.MasterData.Relations.RelationCategories', 'Apps.MasterData.General.Relations.RelationCategories'),
            ('Apps.MasterData.Transport', 'Apps.MasterData.General.Transport'),
            ('Apps.MasterData.Transport.Products', 'Apps.MasterData.General.Transport.Products'),
            ('Apps.MasterData.Transport.TransportUnits', 'Apps.MasterData.General.Transport.TransportUnits'),
            ('Apps.MasterData.Transport.ProductRelationCodes', 'Apps.MasterData.General.Transport.ProductRelationCodes'),
            ('Apps.MasterData.Transport.Temperatures', 'Apps.MasterData.General.Transport.Temperatures'),
            ('Apps.MasterData.Fleet', 'Apps.MasterData.General.Fleet'),
            ('Apps.MasterData.Fleet.Vehicles', 'Apps.MasterData.General.Fleet.Vehicles'),
            ('Apps.MasterData.Fleet.VehicleCombinations', 'Apps.MasterData.General.Fleet.VehicleCombinations'),
            ('Apps.MasterData.Fleet.Drivers', 'Apps.MasterData.General.Fleet.Drivers'),
            ('Apps.MasterData.Fleet.VehicleTypes', 'Apps.MasterData.General.Fleet.VehicleTypes'),
            ('Apps.MasterData.Fleet.TransportCharacteristics', 'Apps.MasterData.General.Fleet.TransportCharacteristics'),
            ('Apps.MasterData.Reference', 'Apps.MasterData.General.Reference'),
            ('Apps.MasterData.Reference.Countries', 'Apps.MasterData.General.Reference.Countries'),
            ('Apps.MasterData.Reference.Currencies', 'Apps.MasterData.General.Reference.Currencies'),
            ('Apps.MasterData.Reference.GeographicalRegions', 'Apps.MasterData.General.Reference.GeographicalRegions')
            ) AS m(old_name, new_name)
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                INSERT INTO "AbpFeatureValues" ("Id", "Name", "Value", "ProviderName", "ProviderKey")
                SELECT md5(v."Id"::text || '|' || m.new_name)::uuid, m.new_name, v."Value", v."ProviderName", v."ProviderKey"
                FROM "AbpFeatureValues" v
                JOIN {Mapping} ON m.old_name = v."Name"
                WHERE v."ProviderName" IN ('T', 'E')
                  AND NOT EXISTS (
                    SELECT 1 FROM "AbpFeatureValues" x
                    WHERE x."Name" = m.new_name
                      AND x."ProviderName" = v."ProviderName"
                      AND x."ProviderKey" IS NOT DISTINCT FROM v."ProviderKey");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            /* The old values were never removed (expand only), so going back removes the copies only: the rows whose
             * id is the one Up derived from an old row and a new name. */
            migrationBuilder.Sql($"""
                DELETE FROM "AbpFeatureValues" c
                USING "AbpFeatureValues" v
                JOIN {Mapping} ON m.old_name = v."Name"
                WHERE c."Id" = md5(v."Id"::text || '|' || m.new_name)::uuid;
                """);
        }
    }
}
