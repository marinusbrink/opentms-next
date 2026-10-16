-- Switches every Master Data app feature (Apps.MasterData, its domain group General and all their nav nodes; PO
-- 2026-10-16) on for one tenant.
-- Runs against the HOST database (public."AbpFeatureValues"); idempotent.
--   psql -v ON_ERROR_STOP=1 -v tenant_id=<AbpTenants.Id> -d OpenTms -f features.sql
-- The backend caches feature values: restart the API host (or wait for the cache to expire) to see the change.
\set ON_ERROR_STOP on
BEGIN;
INSERT INTO public."AbpFeatureValues" ("Id", "Name", "Value", "ProviderName", "ProviderKey")
SELECT gen_random_uuid(), n, 'true', 'T', :'tenant_id'
FROM unnest(ARRAY[
 'Apps.MasterData', 'Apps.MasterData.General',
 'Apps.MasterData.General.Relations', 'Apps.MasterData.General.Relations.Relations', 'Apps.MasterData.General.Relations.AddressTypes', 'Apps.MasterData.General.Relations.RelationCategories',
 'Apps.MasterData.General.Transport', 'Apps.MasterData.General.Transport.Products', 'Apps.MasterData.General.Transport.TransportUnits', 'Apps.MasterData.General.Transport.ProductRelationCodes', 'Apps.MasterData.General.Transport.Temperatures',
 'Apps.MasterData.General.Fleet', 'Apps.MasterData.General.Fleet.Vehicles', 'Apps.MasterData.General.Fleet.VehicleCombinations', 'Apps.MasterData.General.Fleet.Drivers', 'Apps.MasterData.General.Fleet.VehicleTypes', 'Apps.MasterData.General.Fleet.TransportCharacteristics',
 'Apps.MasterData.General.Reference', 'Apps.MasterData.General.Reference.Countries', 'Apps.MasterData.General.Reference.Currencies', 'Apps.MasterData.General.Reference.GeographicalRegions']) n
ON CONFLICT ("Name", "ProviderName", "ProviderKey") DO UPDATE SET "Value" = 'true';
COMMIT;
SELECT "Name", "Value" FROM public."AbpFeatureValues" WHERE "ProviderName" = 'T' AND "ProviderKey" = :'tenant_id' ORDER BY 1;
