# Task: Master Data module, first run (ten reference entities)

You are working in opentms-next. `/CLAUDE.md`, the dev-department conventions (SaaS constitution, coding conventions, performance budgets, UI rules) and `docs/architecture/` apply in full. Where this task and a convention conflict, stop and report.

## Input

Read `docs/input/masterdata-rebuild-assessment.md` first. It is the audit of the current OpenTMS master data with the PO decisions per finding. Section 2.1a is the scope of this task, section 3 lists what to keep (KEEP) and what must be done differently (CHANGE), section 2.2 the seed. When a design choice deliberately differs from OpenTMS, cite the section number.

For the field list of each entity, the OpenTMS source is the reference: `~/dev/OpenTMS/aspnet-core/src/FiLogic.Core/BusinessEntities/<Entity>/`. Take the business fields from there, apply the CHANGE items, and drop the fields the assessment marks as unused or as belonging to another module.

## Process

1. Write the PBI (`dev-department/templates/pbi.md`) and the design doc (`dev-department/templates/design-doc.md`, all nine sections) as `docs/designs/<n>-master-data-first-run.md`. Cite AD-06 for the JSONB value object and list AO-12 under assumptions. Then **stop and hand the design to me for gate 1 approval**.
2. After approval, build exactly the approved design: backend, migration, seed, API contract, frontend screens, tests.

## Scope: the Master Data module, these ten entities and nothing else

Names are final. All entities live in the `masterdata` schema with one additive migration.

| Entity | Notes |
|---|---|
| Currency | ISO 4217 code unique per tenant, numeric code, minor units, symbol, `IsActive`, single tenant default enforced by a unique partial index |
| Country | ISO 3166-1 alpha-2 unique per tenant, alpha-3, numeric, real foreign key to Currency (default currency), primary IANA timezone id. The default VAT reference comes later with Financial |
| AddressType | `Kind` enum (Visiting, Postal, Other) so code never matches on the name |
| RelationCategory | Free classification of relations; no system flags (roles will live on Relation in a later run) |
| TransportCharacteristics | Was "Capability" in OpenTMS. Code unique per tenant |
| Temperature | Was "TemperatureRange". Min/max in Celsius, self many-to-many "do not combine with" |
| Product | Business fields incl. ADR (UN number, class, packing group, description); self many-to-many "do not combine with"; transaction times per waypoint type come later with Orders |
| TransportUnit | Packaging type only: dimensions, weight, loading meters, EPQ factor, pallet-exchange flag; load factors per VehicleType as a child table; self many-to-many "do not combine with". Balances and transactions are Financial, later |
| VehicleType | Add a `Kind` enum (Tractor, Trailer, Rigid, Van); default dimensions and weights; many-to-many to TransportCharacteristics |
| GeographicalRegion | Foreign key to Country; postal-code ranges as a child table (numeric from/till or prefix), with matching that does not match every range when a postcode has no leading digits. Relation membership and default via-waypoints come later |

Out of scope: Relation, Driver, Vehicle, ProductRelationCode, every Financial, Orders, Planning, Platform and Integrations entity, transport profiles and host-level rows (parked), data migration from OpenTMS.

## PO decisions you must not reopen (assessment section 3)

- KEEP: `double` for measures and money, with the unit in the property and column name (`WeightKg`, `LengthCm`, `VolumeM3`, `TemperatureCelsius`, `LoadingMeters`). Reference data is copied per tenant; the ISO code is the natural key and integrations match on it. Consumers reference master data by id, no snapshots.
- CHANGE: real foreign keys and unique constraints in the migration (3.2); join tables instead of delimited strings (3.3); one enforced place per "default" (3.8); separate Create/Update DTOs, server-generated ids, explicit mapping, concurrency stamp (3.14); no UI concerns such as colour flags on entities (3.12); paged, indexed list queries with an explicit filter contract, search over a defined set of columns (3.13); never `TenantId ?? 1`, ABP's `IMultiTenant` with a nullable `TenantId` and no data-filter bypass (3.6).

## Localized names (PO approved)

Add one reusable value object `LocalizedName` to Platform `Domain.Shared`: a culture → text map stored as JSONB, with `Resolve(culture)` falling back to the default language. Currency, Country and AddressType use it for `Name`. Application services return the resolved name for the current UI culture plus the full map for editors; grids and pickers sort on the resolved name; editors show one field per configured language (nl, en).

## Seed (assessment section 2.2)

Embedded JSON in the module's `Domain.Shared`, applied by an ABP data-seed contributor run from the DbMigrator, idempotent (upsert on the natural key), per tenant database:

- Currency: every ISO 4217 currency, `IsActive` true for EUR only, EUR the default.
- Country: every ISO 3166-1 country with names in nl and en, default currency, IANA timezone.
- AddressType: visiting address and postal address, names in nl and en.
- Do not copy the OpenTMS `DefaultTenantDataBuilder` seed values for the other entities; the assessment names its errors.

## Cross-cutting requirements

- Permissions: a `MasterData` permission group with read/create/update/delete per entity in `Application.Contracts/Permissions/`; every app-service method carries its permission; add an architecture test that fails the build on any public app-service method without an authorize attribute (3.11).
- Feature flag `MasterData.FirstRun`, default off, per tenant (constitution rule 4).
- Every user-facing string through the `MasterData` localization resource, nl and en.
- API: conventional controllers under `/api/master-data/...`; regenerate `openapi/opentms-next.json` and the typed client.
- Frontend: screens in the Master data app (`src/apps/master-data/`, domain hooks in `src/domains/master-data/`) following `docs/conventions/ui/ui-rules/`: `OpenTmsGrid` list with server-side paging, filter and sort; `EntityDialog` editors with the General and Record identification tabs; all four screen states; optimistic updates only where the design says so. Library gaps go to the component map, never inline.
- Tests: unit tests for the seed contributor (idempotent on second run), uniqueness rules, `LocalizedName` fallback and the postcode matcher; the PostgreSQL-backed test module for the migration.
- Logging without PII; no raw SQL; indexes ship in the same migration as the queries that need them.

## Done means

Design doc approved at gate 1; migration applies on host and on the `demo` tenant via the DbMigrator; seed present and idempotent; all ten entities have list and editor screens behind the flag; OpenAPI and client regenerated; `dotnet test backend/OpenTms.slnx` and `npm run lint && npm run build` pass; the PR description cites the assessment sections where the design deliberately differs from OpenTMS.
