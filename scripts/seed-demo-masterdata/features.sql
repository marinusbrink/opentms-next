-- Switches every Master Data app feature (Apps.MasterData and all its nav nodes) on for one tenant.
-- Runs against the HOST database (public."AbpFeatureValues"); idempotent.
--   psql -v ON_ERROR_STOP=1 -v tenant_id=<AbpTenants.Id> -d OpenTms -f features.sql
-- The backend caches feature values: restart the API host (or wait for the cache to expire) to see the change.
\set ON_ERROR_STOP on
BEGIN;
INSERT INTO public."AbpFeatureValues" ("Id", "Name", "Value", "ProviderName", "ProviderKey")
SELECT gen_random_uuid(), n, 'true', 'T', :'tenant_id'
FROM unnest(ARRAY[
 'Apps.MasterData',
 'Apps.MasterData.Relations', 'Apps.MasterData.Relations.Relations', 'Apps.MasterData.Relations.AddressTypes', 'Apps.MasterData.Relations.RelationCategories',
 'Apps.MasterData.Transport', 'Apps.MasterData.Transport.Products', 'Apps.MasterData.Transport.TransportUnits', 'Apps.MasterData.Transport.ProductRelationCodes', 'Apps.MasterData.Transport.Temperatures',
 'Apps.MasterData.Fleet', 'Apps.MasterData.Fleet.Vehicles', 'Apps.MasterData.Fleet.VehicleCombinations', 'Apps.MasterData.Fleet.Drivers', 'Apps.MasterData.Fleet.VehicleTypes', 'Apps.MasterData.Fleet.TransportCharacteristics',
 'Apps.MasterData.Reference', 'Apps.MasterData.Reference.Countries', 'Apps.MasterData.Reference.Currencies', 'Apps.MasterData.Reference.GeographicalRegions']) n
ON CONFLICT ("Name", "ProviderName", "ProviderKey") DO UPDATE SET "Value" = 'true';
COMMIT;
SELECT "Name", "Value" FROM public."AbpFeatureValues" WHERE "ProviderName" = 'T' AND "ProviderKey" = :'tenant_id' ORDER BY 1;
