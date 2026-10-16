-- Rule checks on the demo master data of one tenant (scripts/seed-demo-masterdata.sh).
-- Variables: tenant_id (AbpTenants.Id), own_db (true when the tenant has its own database, false in the host database).
--   psql -v ON_ERROR_STOP=1 -v tenant_id=<id> -v own_db=true -f verify.sql
-- Every check under "rule violations" must be 0; otherwise the script ends with an error (exit code 3).
\set ON_ERROR_STOP on
\pset footer off
SET search_path = masterdata;

-- Tenant-scoped temporary views shadow the masterdata tables: pg_temp is searched first, so the unqualified
-- names below see only this tenant's rows (needed when the tenant shares the host database).
SELECT format('CREATE TEMP VIEW %I AS SELECT * FROM masterdata.%I WHERE "TenantId" = %L', table_name, table_name, :'tenant_id')
FROM information_schema.columns
WHERE table_schema = 'masterdata' AND column_name = 'TenantId'
\gexec

\echo == counts
SELECT t AS table, (xpath('/row/c/text()', query_to_xml(format('select count(*) as c from %I', t), false, true, '')))[1]::text::int AS rows
FROM unnest(ARRAY['TransportCharacteristics','Temperatures','TemperatureIncompatibilities','TransportUnits','TransportUnitLoadFactors','TransportUnitIncompatibilities','VehicleTypes','VehicleTypeTransportCharacteristics','Products','ProductIncompatibilities','RelationCategories','GeographicalRegions','GeographicalRegionPostalCodeRanges','Relations','RelationAddresses','RelationContactPersons','RelationBankAccounts','RelationCategoryMemberships','RelationGeographicalRegions','RelationExternalIdentifiers','RelationTransportCharacteristics','Drivers','DriverQualifications','Vehicles','VehicleTransportCharacteristics','VehicleDefaultDrivers','VehicleExternalIdentifiers','VehicleCombinations','VehicleCombinationMembers','ProductRelationCodes']) t;
\echo == relations per role
SELECT sum(("Roles"&1>0)::int) customers, sum(("Roles"&2>0)::int) carriers, sum(("Roles"&4>0)::int) creditors, sum(("Roles"&8>0)::int) depots, sum(("Roles"&16>0)::int) own_company,
       sum((bit_count("Roles"::bit(5))>1)::int) multi_role, sum("IsArchived"::int) archived, sum("IsBlocked"::int) blocked FROM "Relations";
\echo == rule violations (all must be 0)
CREATE TEMP TABLE seed_checks AS
SELECT 'relations with no/invalid roles' rule, count(*) FROM "Relations" WHERE "Roles" NOT BETWEEN 1 AND 31
UNION ALL SELECT 'own companies <> 1', abs(count(*)-1) FROM "Relations" WHERE "Roles"&16=16 AND NOT "IsDeleted"
UNION ALL SELECT 'blocked without reason / reason without block', count(*) FROM "Relations" WHERE ("IsBlocked" AND "BlockReason"=0) OR (NOT "IsBlocked" AND "BlockReason"<>0)
UNION ALL SELECT 'relation code not normalised', count(*) FROM "Relations" WHERE "Code" <> upper(btrim("Code"))
UNION ALL SELECT 'duplicate relation code', count(*) - count(DISTINCT "Code") FROM "Relations"
UNION ALL SELECT 'duplicate VAT', count("VatNumber") - count(DISTINCT "VatNumber") FROM "Relations"
UNION ALL SELECT 'duplicate CoC', count("ChamberOfCommerceNumber") - count(DISTINCT "ChamberOfCommerceNumber") FROM "Relations"
UNION ALL SELECT 'duplicate debtor/creditor no', (count("DebtorNumber") - count(DISTINCT "DebtorNumber")) + (count("CreditorNumber") - count(DISTINCT "CreditorNumber")) FROM "Relations"
UNION ALL SELECT 'VAT not normalised / bad shape', count(*) FROM "Relations" WHERE "VatNumber" IS NOT NULL AND ("VatNumber" <> upper(replace(replace(replace("VatNumber",' ',''),'.',''),'-','')) OR "VatNumber" !~ '^[A-Z]{2}[A-Z0-9]{2,13}$')
UNION ALL SELECT 'NL VAT failing elfproef', count(*) FROM "Relations" WHERE "VatNumber" LIKE 'NL%' AND (SELECT sum(substr("VatNumber",2+i,1)::int * (10-i)) FROM generate_series(1,8) i) % 11 <> substr("VatNumber",11,1)::int
UNION ALL SELECT 'BE VAT failing mod 97', count(*) FROM "Relations" WHERE "VatNumber" LIKE 'BE%' AND 97 - substr("VatNumber",3,8)::bigint % 97 <> substr("VatNumber",11,2)::int
UNION ALL SELECT 'debtor no without customer role / customer without', count(*) FROM "Relations" WHERE ("DebtorNumber" IS NULL) = ("Roles"&1=1)
UNION ALL SELECT 'creditor no without carrier/creditor role', count(*) FROM "Relations" WHERE ("CreditorNumber" IS NULL) = ("Roles"&6>0)
UNION ALL SELECT 'relation without address', count(*) FROM "Relations" r WHERE NOT EXISTS (SELECT 1 FROM "RelationAddresses" a WHERE a."RelationId"=r."Id")
UNION ALL SELECT 'postal code key not normalised', count(*) FROM "RelationAddresses" WHERE "PostalCodeKey" <> upper(replace("PostalCode",' ',''))
UNION ALL SELECT 'NL postcode not 9999 XX', count(*) FROM "RelationAddresses" a JOIN "Countries" c ON c."Id"=a."CountryId" WHERE c."IsoAlpha2"='NL' AND a."PostalCode" !~ '^[1-9][0-9]{3} [A-Z]{2}$'
UNION ALL SELECT 'address time zone <> country zone', count(*) FROM "RelationAddresses" a JOIN "Countries" c ON c."Id"=a."CountryId" WHERE a."TimeZoneId" IS DISTINCT FROM c."PrimaryTimeZoneId"
UNION ALL SELECT 'coordinates outside NL/BE/DE box', count(*) FROM "RelationAddresses" WHERE "Latitude" IS NOT NULL AND NOT ("Latitude" BETWEEN 47.2 AND 55.1 AND "Longitude" BETWEEN 2.5 AND 15.1)
UNION ALL SELECT 'region membership not matching address postcode', count(*) FROM "RelationGeographicalRegions" m WHERE NOT EXISTS (
   SELECT 1 FROM "RelationAddresses" a JOIN "GeographicalRegionPostalCodeRanges" g ON g."GeographicalRegionId"=m."GeographicalRegionId" AND g."CountryId"=a."CountryId"
   WHERE a."RelationId"=m."RelationId" AND substring(a."PostalCodeKey" from '^[0-9]+')::int BETWEEN g."FromNumeric" AND g."TillNumeric")
UNION ALL SELECT 'IBAN failing mod-97', count(*) FROM "RelationBankAccounts" b WHERE (
   SELECT (string_agg(CASE WHEN ch ~ '[0-9]' THEN ch ELSE (ascii(ch)-55)::text END, '' ORDER BY i))::numeric % 97
   FROM (SELECT i, substr(substr(b."Iban",5)||substr(b."Iban",1,4), i, 1) ch FROM generate_series(1, length(b."Iban")) i) x) <> 1
UNION ALL SELECT 'IBAN not normalised', count(*) FROM "RelationBankAccounts" WHERE "Iban" <> upper(replace("Iban",' ',''))
UNION ALL SELECT 'relations with bank accounts but <> 1 default', count(*) FROM (SELECT "RelationId" FROM "RelationBankAccounts" GROUP BY 1 HAVING sum("IsDefault"::int) <> 1) x
UNION ALL SELECT 'carrier/creditor without bank account', count(*) FROM "Relations" r WHERE "Roles"&6>0 AND NOT EXISTS (SELECT 1 FROM "RelationBankAccounts" b WHERE b."RelationId"=r."Id")
UNION ALL SELECT 'driver employer not carrier/own company', count(*) FROM "Drivers" d JOIN "Relations" r ON r."Id"=d."EmployerRelationId" WHERE r."Roles"&18=0
UNION ALL SELECT 'driver qualification from > until', count(*) FROM "DriverQualifications" WHERE "ValidFrom" > "ValidUntil"
UNION ALL SELECT 'vehicle owner not carrier/own company', count(*) FROM "Vehicles" v JOIN "Relations" r ON r."Id"=v."OwnerRelationId" WHERE r."Roles"&18=0
UNION ALL SELECT 'home depot not depot/own company', count(*) FROM "Vehicles" v JOIN "Relations" r ON r."Id"=v."HomeDepotRelationId" WHERE r."Roles"&24=0
UNION ALL SELECT 'plate not normalised (A-Z0-9 upper)', count(*) FROM "Vehicles" WHERE "LicencePlate" !~ '^[A-Z0-9]{1,16}$'
UNION ALL SELECT 'NL plate not a valid sidecode', count(*) FROM "Vehicles" v JOIN "Countries" c ON c."Id"=v."RegistrationCountryId" WHERE c."IsoAlpha2"='NL' AND translate(v."LicencePlate",'ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789','LLLLLLLLLLLLLLLLLLLLLLLLLLDDDDDDDDDD') NOT IN ('LLDDDD','DDDDLL','DDLLDD','LLDDLL','LLLLDD','DDLLLL','DDLLLD','DLLLDD','LLDDDL','LDDDLL','LLLDDL','LDDLLL','DLLDDD','DDDLLD')
UNION ALL SELECT 'VIN shape', count(*) FROM "Vehicles" WHERE "Vin" !~ '^[A-HJ-NPR-Z0-9]{17}$'
UNION ALL SELECT 'vehicle NormalizedName <> upper(Name)', count(*) FROM "Vehicles" WHERE "NormalizedName" <> upper("Name")
UNION ALL SELECT 'trailer with fuel <> NotApplicable or Euro class', count(*) FROM "Vehicles" v JOIN "VehicleTypes" t ON t."Id"=v."VehicleTypeId" WHERE t."Kind"=2 AND ("FuelType"<>12 OR "EuroClass" IS NOT NULL)
UNION ALL SELECT 'vehicle with > 5 default drivers', count(*) FROM (SELECT "VehicleId" FROM "VehicleDefaultDrivers" GROUP BY 1 HAVING count(*)>5) x
UNION ALL SELECT 'default driver sort order not 1..n', count(*) FROM (SELECT "VehicleId" FROM "VehicleDefaultDrivers" GROUP BY 1 HAVING max("SortOrder")<>count(*) OR min("SortOrder")<>1) x
UNION ALL SELECT 'vehicle in > 1 active combination', count(*) FROM (SELECT "VehicleId" FROM "VehicleCombinationMembers" WHERE "IsActive" GROUP BY 1 HAVING count(*)>1) x
UNION ALL SELECT 'combination member count not 2..4', count(*) FROM (SELECT "VehicleCombinationId" FROM "VehicleCombinationMembers" GROUP BY 1 HAVING count(*) NOT BETWEEN 2 AND 4) x
UNION ALL SELECT 'combination order rule (motorised first, then trailers)', count(*) FROM "VehicleCombinationMembers" m JOIN "Vehicles" v ON v."Id"=m."VehicleId" JOIN "VehicleTypes" t ON t."Id"=v."VehicleTypeId" WHERE (m."Position"=1 AND t."Kind"=2) OR (m."Position">1 AND t."Kind"<>2)
UNION ALL SELECT 'member IsActive <> NOT combination.IsArchived', count(*) FROM "VehicleCombinationMembers" m JOIN "VehicleCombinations" c ON c."Id"=m."VehicleCombinationId" WHERE m."IsActive" = c."IsArchived"
UNION ALL SELECT 'archived vehicle in active combination', count(*) FROM "VehicleCombinationMembers" m JOIN "Vehicles" v ON v."Id"=m."VehicleId" WHERE m."IsActive" AND v."IsArchived"
UNION ALL SELECT 'PRC without target', count(*) FROM "ProductRelationCodes" WHERE "RelationId" IS NULL AND "ProductId" IS NULL AND "TransportUnitId" IS NULL
UNION ALL SELECT 'PRC NormalizedCode <> upper(trim(Code))', count(*) FROM "ProductRelationCodes" WHERE "NormalizedCode" <> upper(btrim("Code"))
UNION ALL SELECT 'duplicate PRC normalised code', count(*) - count(DISTINCT "NormalizedCode") FROM "ProductRelationCodes"
UNION ALL SELECT 'PRC relation not a customer', count(*) FROM "ProductRelationCodes" p JOIN "Relations" r ON r."Id"=p."RelationId" WHERE r."Roles"&1=0
UNION ALL SELECT 'ADR product without UN 4 digits / class', count(*) FROM "Products" WHERE "IsAdr" AND ("AdrUnNumber" !~ '^[0-9]{4}$' OR "AdrClass" IS NULL)
UNION ALL SELECT 'non-ADR product with ADR fields', count(*) FROM "Products" WHERE NOT "IsAdr" AND ("AdrUnNumber" IS NOT NULL OR "AdrClass" IS NOT NULL)
UNION ALL SELECT 'incompatibility not canonical (Left < Right as N-string)', (SELECT count(*) FROM "ProductIncompatibilities" WHERE replace("LeftId"::text,'-','') >= replace("RightId"::text,'-',''))
   + (SELECT count(*) FROM "TemperatureIncompatibilities" WHERE replace("LeftId"::text,'-','') >= replace("RightId"::text,'-',''))
   + (SELECT count(*) FROM "TransportUnitIncompatibilities" WHERE replace("LeftId"::text,'-','') >= replace("RightId"::text,'-',''))
UNION ALL SELECT 'e-mail not matching domain shape', (SELECT count(*) FROM "Relations" WHERE "Email" !~ '^[^@\s]+@[^@\s]+\.[^@\s]+$') + (SELECT count(*) FROM "RelationContactPersons" WHERE "Email" !~ '^[^@\s]+@[^@\s]+\.[^@\s]+$') + (SELECT count(*) FROM "Drivers" WHERE "Email" !~ '^[^@\s]+@[^@\s]+\.[^@\s]+$')
UNION ALL SELECT 'e-mail/website not on a .example domain', (SELECT count(*) FROM "Relations" WHERE "Email" !~ '\.example$' OR "Website" !~ '\.example$') + (SELECT count(*) FROM "RelationContactPersons" WHERE "Email" !~ '\.example$')
UNION ALL SELECT 'SearchKey longer than 320 / empty', count(*) FROM "Relations" WHERE length("SearchKey") NOT BETWEEN 1 AND 320
UNION ALL SELECT 'rows with missing or foreign TenantId (all seeded tables)', sum((xpath('/row/c/text()', query_to_xml(format(
   'select count(*) as c from masterdata.%I where "TenantId" is null or (%s and "TenantId" <> %L)', t, :'own_db', :'tenant_id'), false, true, '')))[1]::text::int)
FROM unnest(ARRAY['TransportCharacteristics','Temperatures','TemperatureIncompatibilities','TransportUnits','TransportUnitLoadFactors','TransportUnitIncompatibilities','VehicleTypes','VehicleTypeTransportCharacteristics','Products','ProductIncompatibilities','RelationCategories','GeographicalRegions','GeographicalRegionPostalCodeRanges','Relations','RelationAddresses','RelationContactPersons','RelationBankAccounts','RelationCategoryMemberships','RelationGeographicalRegions','RelationExternalIdentifiers','RelationTransportCharacteristics','Drivers','DriverQualifications','Vehicles','VehicleTransportCharacteristics','VehicleDefaultDrivers','VehicleExternalIdentifiers','VehicleCombinations','VehicleCombinationMembers','ProductRelationCodes']) t
;
SELECT * FROM seed_checks;
\echo == other figures
SELECT (SELECT count(*) FROM "Products" WHERE "IsAdr") adr_products, (SELECT count(*) FROM "Products" WHERE "IsArchived") archived_products,
       (SELECT count(*) FROM "DriverQualifications" WHERE "ValidUntil" BETWEEN current_date AND current_date + 90) quals_expiring_90d,
       (SELECT count(*) FROM "DriverQualifications" WHERE "ValidUntil" < current_date) quals_expired,
       (SELECT count(*) FROM "VehicleCombinations" WHERE "IsArchived") archived_combinations,
       (SELECT count(*) FROM "ProductRelationCodes" WHERE "IsArchived") archived_prc;
SELECT t."Kind", count(*) vehicles, sum((v."OwnerRelationId" IN (SELECT "Id" FROM "Relations" WHERE "Roles"&16=16))::int) own FROM "Vehicles" v JOIN "VehicleTypes" t ON t."Id"=v."VehicleTypeId" GROUP BY 1 ORDER BY 1;

DO $$
DECLARE failed text;
BEGIN
  SELECT string_agg(rule, '; ') INTO failed FROM seed_checks WHERE count <> 0;
  IF failed IS NOT NULL THEN RAISE EXCEPTION 'rule checks failed: %', failed; END IF;
END $$;
