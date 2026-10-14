using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTms.Migrations
{
    /* The app and navigation features replace the ad-hoc ones (PO decision 2026-10-14,
     * docs/designs/tenants-and-editions.md, "App features"). Every stored tenant ("T") and edition ("E") value
     * of an old feature is copied to each node it covered, unless that node already has a value for the same
     * provider and key. A copy's id is derived from its source row and its new name (md5 → uuid), so Down removes
     * exactly the copies and never a value that existed before. Expand only (constitution rule 2): the old values stay, so a previous release running next
     * to this one during a rolling deploy still finds them. Removing the old names (MasterData.FirstRun,
     * MasterData.Relations, Platform.Maintenance, UI.CommonToolbar) is the contract step of a later release
     * (docs/designs/tenants-and-editions.md, "Migration"). Raw SQL: these are ABP's feature-management tables, not
     * entities of ours. Tenant databases hold no feature values, so there it changes nothing. */
    public partial class MoveFeatureValuesToAppTree : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO "AbpFeatureValues" ("Id", "Name", "Value", "ProviderName", "ProviderKey")
                SELECT md5(v."Id"::text || '|' || m.new_name)::uuid, m.new_name, v."Value", v."ProviderName", v."ProviderKey"
                FROM "AbpFeatureValues" v
                JOIN (VALUES
                ('MasterData.FirstRun', 'Apps.MasterData.Relations.AddressTypes'),
                ('MasterData.FirstRun', 'Apps.MasterData.Relations.RelationCategories'),
                ('MasterData.FirstRun', 'Apps.MasterData.Transport.Products'),
                ('MasterData.FirstRun', 'Apps.MasterData.Transport.TransportUnits'),
                ('MasterData.FirstRun', 'Apps.MasterData.Transport.Temperatures'),
                ('MasterData.FirstRun', 'Apps.MasterData.Fleet.VehicleTypes'),
                ('MasterData.FirstRun', 'Apps.MasterData.Fleet.TransportCharacteristics'),
                ('MasterData.FirstRun', 'Apps.MasterData.Reference.Countries'),
                ('MasterData.FirstRun', 'Apps.MasterData.Reference.Currencies'),
                ('MasterData.FirstRun', 'Apps.MasterData.Reference.GeographicalRegions'),
                ('MasterData.Relations', 'Apps.MasterData.Relations.Relations'),
                ('MasterData.Relations', 'Apps.MasterData.Fleet.Drivers'),
                ('Platform.Maintenance', 'Apps.Administration.Maintenance')
                ) AS m(old_name, new_name) ON m.old_name = v."Name"
                WHERE NOT EXISTS (
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
            migrationBuilder.Sql("""
                DELETE FROM "AbpFeatureValues" c
                USING "AbpFeatureValues" v
                JOIN (VALUES
                ('MasterData.FirstRun', 'Apps.MasterData.Relations.AddressTypes'),
                ('MasterData.FirstRun', 'Apps.MasterData.Relations.RelationCategories'),
                ('MasterData.FirstRun', 'Apps.MasterData.Transport.Products'),
                ('MasterData.FirstRun', 'Apps.MasterData.Transport.TransportUnits'),
                ('MasterData.FirstRun', 'Apps.MasterData.Transport.Temperatures'),
                ('MasterData.FirstRun', 'Apps.MasterData.Fleet.VehicleTypes'),
                ('MasterData.FirstRun', 'Apps.MasterData.Fleet.TransportCharacteristics'),
                ('MasterData.FirstRun', 'Apps.MasterData.Reference.Countries'),
                ('MasterData.FirstRun', 'Apps.MasterData.Reference.Currencies'),
                ('MasterData.FirstRun', 'Apps.MasterData.Reference.GeographicalRegions'),
                ('MasterData.Relations', 'Apps.MasterData.Relations.Relations'),
                ('MasterData.Relations', 'Apps.MasterData.Fleet.Drivers'),
                ('Platform.Maintenance', 'Apps.Administration.Maintenance')
                ) AS m(old_name, new_name) ON m.old_name = v."Name"
                WHERE c."Id" = md5(v."Id"::text || '|' || m.new_name)::uuid;
                """);
        }
    }
}
