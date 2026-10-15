# PBI: Master Data, final run: Vehicle, vehicle combination and product relation code

<!-- Written from the assessment docs/input/masterdata-rebuild-assessment.md (§2 row 1, §2.1 "Later runs:
     … Vehicle and ProductRelationCode", §3.2, §3.3, §3.15, §5 "Fleet") and the PO's instruction of
     2026-10-15 "build the final step of master data". No GitHub issue; "48" is the next free number after
     PR #47. -->

## Story

As a planner I want one record per truck, tractor, trailer and van, with its owner, home depot,
dimensions, capacities and characteristics, and the standing combinations of a tractor with its trailer,
because the planboard, trip assignment, route calculation and CO₂ reporting all refer to vehicles by id
and none of them can be built before the vehicle exists. As an order clerk I want the customer's own
article codes mapped to our product, transport unit and destination, because imports and order lines
name goods by the customer's code.

## Affected area

The Master Data domain, the last row of assessment §2 row 1 that is not built yet: `Vehicle` with its
children (characteristics, organisation units, default drivers, external identifiers),
`VehicleCombination` with its members, and `ProductRelationCode`; their application services under
`/api/master-data/...`; the Vehicles and Vehicle combinations views in the Fleet group and the Product
relation codes view in the Transport group of the Master data app. Builds on designs 41 and 43.

## Tenant dimension

Configurable per tenant; every row belongs to one tenant. Three new app-tree features (tenants-and-editions
D8), default off: `Apps.MasterData.Fleet.Vehicles`, `Apps.MasterData.Fleet.VehicleCombinations`,
`Apps.MasterData.Transport.ProductRelationCodes`. `TenantId` nullable (assessment §4, parked).

## Expected volumes

Per tenant, steady state (the test engineer multiplies by five):

| Entity | Rows | Note |
|---|---|---|
| Vehicle | up to 1,000 | own fleet plus regular charter vehicles; most tenants under 200 |
| VehicleTransportCharacteristic | 3 per vehicle | |
| VehicleDefaultDriver | 1 per vehicle, at most 5 | |
| VehicleExternalIdentifier | 1 per vehicle, at most 50 | FMS, EDI and partner ids |
| VehicleCombination | up to 500 | a standing tractor–trailer or truck–trailer coupling |
| VehicleCombinationMember | 2 per combination, at most 4 | |
| ProductRelationCode | up to 50,000 | customer article codes from imports; the largest list of the run |

Busiest screen: the product relation code list (50,000 rows, server-side paged); the code lookup from an
import or order line is the hot read later.

## Known edge cases

- Duplicate natural keys: vehicle name, licence plate per registration country, VIN; a vehicle in two
  combinations; the same vehicle twice in one combination; a product relation code twice.
- A combination without a motorised vehicle, or with two; a trailer as the first member.
- A vehicle whose owner relation loses its Carrier/OwnCompany role or is archived; a default driver who is
  archived or anonymised; a vehicle type changed after the vehicle took its defaults.
- Deleting a vehicle type, relation, driver, product or transport unit that a vehicle or code uses.
- Licence plates typed with spaces, dashes and lower case; foreign plates; plates without a country.
- An import that names a product relation code that does not exist (no silent creation, §3.15).

## Privacy note

No new personal data. A vehicle links to drivers by id only (design 43 covers the driver). A licence plate
of a sole trader's vehicle is company data.

---

# Design: Master Data, final run: Vehicle, vehicle combination and product relation code

**PBI:** above (this file); assessment `docs/input/masterdata-rebuild-assessment.md`
**Status:** approved (gate 1, PO 2026-10-15: assumptions 1, 2 and 4 confirmed as drafted — standing combination entity, RDW block later, destination relation with the code unique per tenant)
**Date:** 2026-10-15
**Depends on:** designs 41 and 43 merged (VehicleType, TransportCharacteristics, Product, TransportUnit,
Country, Relation, Driver, `EntityLookup`, `LinesGrid`); tenants-and-editions (app-tree features).

## Domain impact

**Module:** Master Data, schema `masterdata`. Three new aggregates (Vehicle, VehicleCombination,
ProductRelationCode), one additive migration `MasterDataVehicles`. After this run the Master Data row of
assessment §2 is complete except the parked host-level rows (§4).

**Flow:** commands only (AD-18); no event in this run (as design 43: the Planning run that reacts to
"vehicle archived" adds the ETO under `MasterData.Domain.Shared/Events` with its design). It extends
none of the three flows of `02-flows.md` yet.

**Decisions relied on:** AD-02, AD-03, AD-06 (no jsonb: every part is relational), AD-16, AD-18.
AD-07 and AD-08 untouched: no money (cost price and cost centre leave, see below), no derived value.
**AO-12 is not depended on:** no position, `LastSeen*` or device field lives here; the device binding is
the Integrations run (assessment §2 row 6, §3.15).

**Cross-module references:** `VehicleOrganizationUnit.OrganizationUnitId` → ABP organisation unit
(Platform, no constraint, validated through `IOrganizationUnitRepository` as in design 43). Every other
reference is in-module and a real foreign key (§3.2).

### Vehicle

| Field | Type | Rule |
|---|---|---|
| `Name` | string(64), required | the fleet number or short name the planboard shows; **unique per tenant** |
| `VehicleTypeId` | Guid FK → VehicleTypes (restrict), required | the vehicle's **kind is its type's kind** (`VehicleTypeKind`: Tractor, Trailer, Rigid, Van); not stored twice |
| `LicencePlate` | string(16)? | stored normalised: upper case, letters and digits only ("12-ABC-3" → "12ABC3"); shown as typed by a formatter per country (NL sidecodes; other countries as stored); **unique per tenant and registration country when set** (§5: "licence plate as a value object") |
| `RegistrationCountryId` | Guid? FK → Countries (restrict) | required when a plate is set; new vehicles start with the tenant's default country |
| `Vin` | string(17)? | upper case, ISO 3779 shape (17 characters, no I, O, Q); **unique per tenant when set** |
| `OwnerRelationId` | Guid FK → Relations (restrict), required | the relation must carry the Carrier or OwnCompany role (as the driver's employer); new vehicles start with the own company |
| `HomeDepotRelationId` | Guid? FK → Relations (restrict) | OpenTMS "Stationing"; the relation must carry the Depot or OwnCompany role |
| `LengthCm`, `WidthCm`, `HeightCm`, `GrossVehicleWeightKg`, `EmptyWeightKg`, `MaxEpq`, `MaxLoadingMeters`, `MaxPayloadKg` | double? | **copied from the type on create** by the server when not given (§5: explicit override semantics; OpenTMS copied only in Angular, from the default type); afterwards the vehicle's own values, never re-inherited |
| `FuelType` | enum `FuelType { Diesel = 1, Petrol, Lng, Cng, Lpg, Hvo, Electric, Hydrogen, DieselElectricHybrid, PetrolElectricHybrid, Other, NotApplicable }`? | null = unknown; NotApplicable for trailers (OpenTMS `Unknow` dropped) |
| `EuroClass` | enum `EuroClass { Euro0 … Euro6, Eev, ZeroEmission }`? | §5: an enum instead of the free `ExhaustEmissionLevel` string |
| `IsArchived` | bool | hides the vehicle from pickers |
| `Description` | string(2500)? | |

Children, all owned (`IMultiTenant`, cascade from the vehicle, replace semantics):

| Child | Fields | Rules |
|---|---|---|
| **VehicleTransportCharacteristic** | `VehicleId`, `TransportCharacteristicsId` FK (cascade) | composite key; the vehicle's **own additions**. The effective set is the type's characteristics ∪ the vehicle's, evaluated live (§5: "explicit override semantics"); the detail DTO returns both, marked by origin. A vehicle cannot remove a characteristic of its type. |
| **VehicleDefaultDriver** | `VehicleId`, `DriverId` FK → Drivers (restrict), `SortOrder` int | composite key; at most 5; replaces OpenTMS's `;`-joined `DefaultDriverIdentifiers` (§3.3) |
| **VehicleOrganizationUnit** | `VehicleId`, `OrganizationUnitId` (Platform id) | composite key; empty = all units (as the relation) |
| **VehicleExternalIdentifier** | `Source` string(64), `Identifier` string(128) | as the relation's: unique `(TenantId, Source, Identifier)`, at most 50 |

**Delete:** soft delete plus the in-service check of design 41: a vehicle in a combination is refused with
`InUse`. Deleting a vehicle type, relation (owner or home depot) or driver (default driver) that a vehicle
uses is refused with `InUse` in their services (one `AnyAsync` per reference, as run 1).

### VehicleCombination

The coupling of assessment §5 ("a Combination/coupling entity instead of three fixed slots plus
`IsPrimaryVehicle`"): a **standing** combination that the planner uses as one unit by default. The actual
combination on a trip is Planning's fact and references vehicle ids itself.

| Field | Type | Rule |
|---|---|---|
| `Name` | string(64), required | unique per tenant; defaults to the members' names joined by " + " |
| `IsArchived` | bool | |
| `Description` | string(2500)? | |
| child **VehicleCombinationMember** | `VehicleId` FK → Vehicles (restrict), `Position` int (1-based) | 2 to 4 members; position 1 is the **motorised** vehicle (kind Tractor, Rigid or Van), later positions are **trailers**; a vehicle appears **at most once in all active combinations** (unique partial index on `(TenantId, VehicleId)` over members of non-archived, non-deleted combinations, maintained by a denormalised `IsActive` on the member) |

### ProductRelationCode

The customer article code of assessment §2 row 1: the customer's name for goods, mapped to our product,
transport unit and destination. All references are Master Data, so real foreign keys.

| Field | Type | Rule |
|---|---|---|
| `Code` | string(64), required | OpenTMS `Name`; stored as typed, trimmed; `NormalizedCode` (upper case) **unique per tenant** (OpenTMS matched trimmed case-insensitively in one place and case-sensitively in another) |
| `RelationId` | Guid? FK → Relations (restrict) | OpenTMS "Destination" (see Assumption 4) |
| `ProductId` | Guid? FK → Products (restrict) | |
| `TransportUnitId` | Guid? FK → TransportUnits (restrict) | |
| `IsArchived` | bool | |
| `Description` | string(2500)? | |

At least one of relation, product and transport unit is required (a code that maps to nothing is noise).
**No find-or-create** (§3.15 CHANGE): `IProductRelationCodeResolver.ResolveAsync(codes[])` in
`Application.Contracts` returns the matches by normalised code (archived excluded) for the Orders import
run to call; an unknown code goes to the intake flow as a proposal there. Delete refused with `InUse` once
order lines reference codes (Orders run).

### Feature flags and permissions

- Features (Platform `AppFeatures`, mirrored in `apps.config.ts`, default off, chain-checked as every
  node): `Apps.MasterData.Fleet.Vehicles` (`VehicleAppService`, not its picker),
  `Apps.MasterData.Fleet.VehicleCombinations` (`VehicleCombinationAppService`),
  `Apps.MasterData.Transport.ProductRelationCodes` (`ProductRelationCodeAppService`, not the resolver).
  Pickers (`GetLookupPageAsync` of vehicles) need only `Apps.MasterData` (D12).
- Permissions, both sides, in `MasterDataPermissions`:
  `MasterData.Vehicles`, `MasterData.Vehicles.Create`, `MasterData.Vehicles.Update`, `MasterData.Vehicles.Delete`,
  `MasterData.VehicleCombinations`, `MasterData.VehicleCombinations.Create`, `MasterData.VehicleCombinations.Update`, `MasterData.VehicleCombinations.Delete`,
  `MasterData.ProductRelationCodes`, `MasterData.ProductRelationCodes.Create`, `MasterData.ProductRelationCodes.Update`, `MasterData.ProductRelationCodes.Delete`.
  The resolver is `MasterData.ProductRelationCodes`. Seeded for the tenant admin role by the DbMigrator, as
  the earlier runs.

### Deliberate differences from OpenTMS

| Area | Dropped or changed | Reason |
|---|---|---|
| Vehicle | `IsPrimaryVehicle`, `DefaultSecondaryVehicleId`, `DefaultThirdVehicleId`; TransportFile's three slots | `VehicleCombination` (§5); the trip's own combination is Planning's |
| Vehicle | `DefaultDriverIdentifiers` (`;`-string) | `VehicleDefaultDriver` join with FK (§3.3) |
| Vehicle | `LastSeen*`, `FleetManagementSystemDeviceId`, `FleetManagementSystemVendorId` | telematics state out of master data (§5, AO-12 open); device binding is Integrations (§3.15) |
| Vehicle | RDW registry block (`Brand`, `TradeName`, `VehicleCategory`, masses, `FuelConsumption`, `CO2Emission*`, `NetMaximumPower`, the string `VehicleType`) | a separate read-only block filled by an RDW adapter in the Integrations run (§5); this run keeps only what planning and CO₂ need: fuel type, Euro class, weights (Assumption 2) |
| Vehicle | `SpeedCorrectionFactor` | routing settings (AD-12), Planning run |
| Vehicle | `CostPricePerKm`, `CostCenter` | Financial run, keyed by VehicleId |
| Vehicle | `TransportCompanyId` | `OwnerRelationId`, role-checked, required in the backend (OpenTMS checked only in Angular) |
| Vehicle | capabilities inherited live but dimensions copied once, client-side, from the *default* type | both explicit: dimensions copied server-side from the *chosen* type on create; characteristics live union, origin shown |
| Vehicle | no uniqueness on name, plate or VIN; no plate validation | unique name, plate per country, VIN (§3.2) |
| Vehicle | Calendar, sensor measurements, attachments, merge wizard, `MaxVehicleCount` edition limit | Planning (availability), Integrations (sensors), Platform (attachments); merge repaired duplicates that the unique keys now prevent; an edition limit is a Platform design |
| Vehicle | org-unit removal deleting the link of every vehicle (`VehicleAppService.cs:949`) | owned join rows, replace semantics per vehicle |
| ProductRelationCode | `FindOrCreate`, `BatchFindOrCreate`, the cache manager and its create bug | resolver that never creates (§3.15); paged server query, no cache |
| ProductRelationCode | the list's derived TU conversion factors and transaction times | read from Product and TransportUnit by the consumer; no copies (§3.4) |

## API contract

Conventional controllers, route root `master-data`, the design-41 shape (`<E>ListRequestDto`,
`GridResponse<<E>RowDto>`, detail, create and update DTOs with concurrency stamp, bulk delete). OpenAPI and
the typed client regenerated.

| Service | Route root | Extra operations |
|---|---|---|
| `VehicleAppService` | `/api/master-data/vehicle` | `GET /lookup-page?term=&skip=&take=&kinds=&ownerRelationId=` (pages of 25 for `EntityLookup`), `GET /type-defaults/{vehicleTypeId}` (the values a new vehicle takes), `POST /{id}/archive`, `POST /{id}/unarchive` |
| `VehicleCombinationAppService` | `/api/master-data/vehicle-combination` | `POST /{id}/archive`, `POST /{id}/unarchive` |
| `ProductRelationCodeAppService` | `/api/master-data/product-relation-code` | `POST /{id}/archive`, `POST /{id}/unarchive` |

- `VehicleRowDto`: `id, name, licencePlate (formatted), registrationCountryCode, vehicleTypeName, kind,
  ownerName, homeDepotName, fuelType, euroClass, combinationName, isArchived`. List filters: `kinds`,
  `vehicleTypeId`, `ownerRelationId`, `includeArchived`; wildcard over name, plate, VIN. Sort whitelist:
  `name`, `licencePlate`, `vehicleTypeName`, `ownerName`, `kind`.
- `VehicleDto`: all fields, `characteristics[] { id, code, name, origin: Type | Vehicle }`,
  `defaultDrivers[] { driverId, name }`, `organizationUnitIds[]`, `externalIdentifiers[]`, audit, stamp.
- `VehicleCombinationRowDto`: `id, name, members (names joined), motorisedVehicleName, isArchived`;
  detail with `members[] { vehicleId, name, kind, licencePlate, position }`.
- `ProductRelationCodeRowDto`: `id, code, relationName, relationCity, productName, transportUnitName,
  isArchived`; wildcard over code, relation name, product name.
- Errors: `DuplicateKey` (name, plate, VIN, combination name, code, external id), `InUse`,
  `OwnerRoleRequired`, `HomeDepotRoleRequired`, `InvalidLicencePlate`, `InvalidVin`,
  `RegistrationCountryRequired`, `CombinationMotorisedFirst`, `CombinationMemberCount`,
  `VehicleAlreadyInCombination`, `ProductRelationCodeTargetRequired`, `TooManyChildren`.

## Migration strategy

One additive migration `MasterDataVehicles` after `MasterDataRelationAdministration`: tables `Vehicles`,
`VehicleTransportCharacteristics`, `VehicleDefaultDrivers`, `VehicleOrganizationUnits`,
`VehicleExternalIdentifiers`, `VehicleCombinations`, `VehicleCombinationMembers`, `ProductRelationCodes`.
Indexes: per-tenant unique (`NULLS NOT DISTINCT`, soft-delete filter) on vehicle name, `(TenantId,
RegistrationCountryId, LicencePlate)` where set, VIN where set, combination name, `NormalizedCode`, external
id; the active-member partial unique index; `(VehicleTypeId)`, `(OwnerRelationId)`, `(HomeDepotRelationId)`,
`(DriverId)`, `(RelationId)`, `(ProductId)`, `(TransportUnitId)` for the in-use checks; trigram GIN on
`NormalizedCode` (50,000 codes, contains search; `pg_trgm` exists since design 43). Expand only; `Down`
drops the tables. One application-context migration for nothing: the three features need no stored value.

## UI design

**Navigation** (`apps.config.ts`, features as above): Fleet gets **Vehicles** (`/master-data/vehicles`,
first) and **Vehicle combinations** (`/master-data/vehicle-combinations`); Transport gets **Product
relation codes** (`/master-data/product-relation-codes`, after Transport units).

**Vehicles list:** Name, Licence plate, Type, Kind, Owner, Home depot, Combination; hidden: Fuel type, Euro
class, VIN, Archived. Filters: Kind (`Select`), Owner (`EntityLookup`), Show archived. Row actions Edit,
Archive/Unarchive, Delete; command bar New vehicle, Delete selected.

**Vehicle editor** (`EntityDialog size="lg"`), tabs:
1. **General**: *Vehicle* (Name, Type `ReferenceCombobox` — choosing a type on a new vehicle fills the
   empty dimension fields from `type-defaults`, Archived), *Registration* (Country, Licence plate, VIN),
   *Ownership* (Owner and Home depot as `EntityLookup` over relations filtered by role, with "Add new
   relation…"; Organisation units `MultiSelect`), *Remarks* (Description).
2. **Dimensions and capacity**: L/W/H (cm), Empty and gross weight (kg), Max payload (kg), Max EPQ, Max
   loading metres; Fuel type and Euro class `Select`s.
3. **Characteristics**: one grid; the type's rows read-only with an "from type" badge, the vehicle's own
   rows removable; picker `EntityLookup` over `transport-characteristics/lookup-page`.
4. **Drivers and identification**: Default drivers `LinesGrid` (driver `EntityLookup`, order), External
   identification numbers grid (Id, Source) as the relation's.
5. Record identification.

Badges after the title: kind and *Archived*.

**Vehicle combinations list:** Name, Members, Motorised vehicle, Archived (hidden). **Editor** (`md`):
Name, Archived, Description, Members `LinesGrid` (position, vehicle `EntityLookup` restricted by
position: motorised kinds for row 1, trailers after), validated on submit.

**Product relation codes list:** Code, Destination (relation name + city), Product, Transport unit,
Archived (hidden); wildcard search. **Editor** (`md`): Code, Archived, Destination `EntityLookup`
(relations), Product and Transport unit `ReferenceCombobox`, Description.

**Deviation UI-LOOK-001:** the vehicle type (at most 50 per tenant), the product and the transport unit are
picked with a `ReferenceCombobox` over their run-1 lookups, not an `EntityLookup`: these lists have no
lookup-page endpoint and stay within the lookup's cap of 500 per tenant. The code marks each as
`DEVIATION(UI-LOOK-001)`. The organisation units are a `MultiSelect` under the same rule, as in the relation
editor.

**Library:** nothing new; `EntityLookup`, `LinesGrid`, `MultiSelect`, `ReferenceCombobox` exist. A
`LicencePlateCell`/formatter in `lib/format` (UI-FMT) formats NL plates by sidecode.

**Manual:** `docs/manual/voertuigen.md` (vehicles and combinations) and a section in
`docs/manual/stamgegevens.md` for product relation codes (Dutch).

## Test risk analysis

| Part | Class | Mandatory coverage |
|---|---|---|
| Tenant isolation of vehicles, children, combinations, codes, the lookups and the resolver | **Critical** | PostgreSQL negative tests per operation |
| Authorization and the three features (attributes via the architecture test; definitions, chain and flag-off) | **Critical** | definition and flag-off tests; app-tree feature test updated |
| Migration and every index | **Critical** | PostgreSQL migration test as earlier runs |
| Uniqueness: name, plate per country, VIN, combination name, normalised code, external id, one active combination per vehicle | **Critical** | PostgreSQL index-violation tests, friendly 409 through the service |
| Combination rules (motorised first, trailers after, 2–4 members, archived combination frees its vehicles) | **High** | SQLite service tests, table-driven |
| Plate normalisation and NL formatting, VIN shape, type defaults on create, characteristic union and origin, owner/depot roles, in-use refusals in VehicleType/Relation/Driver/Product/TransportUnit deletes | **High** | SQLite service tests; formatter vitest |
| Volume ×5: 5,000 vehicles, 250,000 codes: list, lookup, resolver of 100 codes | **High** | PostgreSQL volume test, index use |
| Frontend: three lists and editors, type-defaults fill, combination member validation | **Medium** | vitest |
| Localization parity nl/en | **Low** | key test |

Idempotency: archive/unarchive twice is one effect; bulk delete twice deletes zero the second time.

## Flag & rollout plan

Three features default off; health tenant → friendly tenants → all. New tables, no existing data.
Rollback: features off, tables stay. The OpenTMS data migration (duplicate vehicles, `;`-strings, the three
slots to combinations, codes deduplicated by normalised code) is the separate brief of assessment §7.

## Cost & SLO impact

- Cloud SQL: eight tables; at 50,000 codes about 15 MB per large tenant with the trigram index, vehicles
  negligible. No jobs, no external calls, no egress. Per-tenant margin unaffected.
- Budgets: list, lookup and resolver p95 < 300 ms (index probes; the resolver is one `IN` over the unique
  index); mutations p95 < 500 ms.

## Assumptions

1. **Combination is a standing default, not the trip's combination.** Planning records which vehicles
   ran a trip; this aggregate only proposes the usual coupling. A vehicle is in at most one active
   combination.
2. **No RDW registry block in this run.** Brand, trade name, masses, CO₂ figures and power arrive with an
   RDW adapter in Integrations as a read-only block; this run stores fuel type, Euro class and the weights
   planning needs.
3. **Vehicle kind comes from the vehicle type** (the type is required); a vehicle has no kind of its own.
4. **ProductRelationCode's relation is the destination** (OpenTMS's label "Destination", mapped from the
   legacy `ArrivalAddressId`), and the code is unique per tenant, not per relation. If the code is the
   *customer's* article code and two customers may use the same code, uniqueness becomes `(TenantId,
   RelationId, NormalizedCode)` and the relation the customer.
5. Dimensions are copied from the type once on create and never re-inherited; characteristics are a live
   union. Changing a type later does not touch existing vehicles' dimensions.
6. Cost price per km and cost centre go to the Financial run; speed correction factor to the Planning
   run.
7. Positions and device data stay out (AO-12 open).
8. File number 48 stands in for the issue number.

## Security quickscan

- **Permissions:** twelve new definitions, both sides; every public method attributed (architecture test);
  three app-tree features as the second gate.
- **Input validation:** lengths, plate and VIN shape, enum ranges, referenced ids checked in the tenant,
  owner/depot roles, children bounded (50 external ids, 50 characteristics, 5 default drivers, 4 members),
  lookup pages at 25, resolver input at 500 codes.
- **Attack surface:** three conventional controllers behind authentication, permissions and features; no
  anonymous endpoint, no upload, no raw SQL, no outbound call.
- **Tenant isolation:** every entity `IMultiTenant`; no filter bypass.
- **Logging:** ids and counts only; no plates or VINs in logs.
- **Personal data:** none new; default drivers are ids.
