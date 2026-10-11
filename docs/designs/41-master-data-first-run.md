# PBI: Master Data module, first run (ten reference entities)

<!-- Written from the PO's brief of 2026-10-11 (docs/input/masterdata-first-run-prompt.md) and the
     assessment docs/input/masterdata-rebuild-assessment.md. No GitHub issue was opened (local rule:
     no GitHub side effects on the agent's own initiative); "41" is the next free number on the
     repository and the PO may renumber the file when the issue exists. -->

## Story

As a tenant administrator I want to maintain the reference lists every transport order, trip and
invoice depends on (currencies, countries, address types, relation categories, transport
characteristics, temperatures, products, transport units, vehicle types, geographical regions)
because the Orders, Planning and Financial runs that follow reference these rows by id and cannot
be built before they exist.

## Affected area

The Master Data domain: a new set of entities in the `masterdata` schema, their application
services under `/api/master-data/...`, a seed of ISO reference data per tenant, and the first
screens of the Master data app. One reusable value object (`LocalizedName`) is added to Platform.

## Tenant dimension

Configurable per tenant. Every row belongs to one tenant (reference data is copied per tenant,
assessment §3.7). The feature flag `MasterData.FirstRun` is per tenant, default off. Host-level
rows and transport profiles are parked (assessment §4); the model keeps `TenantId` nullable so they
can be added later without a contract migration.

## Expected volumes

Per tenant, steady state (the figures the test engineer multiplies by five):

| Entity | Rows | Source |
|---|---|---|
| Currency | 180 (seeded), a handful active | ISO 4217 |
| Country | 249 (seeded) | ISO 3166-1 |
| AddressType | 2 seeded, up to 10 | tenant |
| RelationCategory | up to 50 | tenant |
| TransportCharacteristics | up to 50 | tenant |
| Temperature | up to 20 | tenant |
| Product | up to 1,000 | tenant |
| TransportUnit | up to 50, each with up to 20 load factors | tenant |
| VehicleType | up to 50 | tenant |
| GeographicalRegion | up to 200, each with up to 50 postal-code ranges (10,000 ranges) | tenant |

Busiest screen: the product list (1,000 rows, server-side paged in blocks of 100). Calls: CRUD by
administrators, in the order of 100 requests per tenant per day; no external parties.

## Known edge cases

- A code that already exists for the tenant (ISO code, characteristics code, name of a code-list
  entity), also when the existing row was soft-deleted.
- Deleting a currency that a country uses as default, or the tenant's default currency; deleting a
  country that a postal-code range uses; deleting a vehicle type that a load factor uses.
- A postcode without leading digits (UK `SW1A 1AA`, Irish Eircodes) against numeric ranges.
- A temperature, product or transport unit marked incompatible with itself; the same pair entered
  from both sides.
- Two load factors for the same vehicle type on one transport unit.
- Two administrators saving the same row (concurrency stamp).
- Re-running the seed on a tenant that renamed, deactivated or deleted a seeded row.
- A tenant-created currency without an ISO numeric code.
- Archived products, units and temperatures: hidden from pickers, visible in the list on request.

## Privacy note

No personal data. Every entity is reference data about things, not people. GDPR art. 15/17 do not
apply; the design's security quickscan says so once.

---

# Design: Master Data first run (ten reference entities)

**PBI:** above (this file); brief `docs/input/masterdata-first-run-prompt.md`; assessment
`docs/input/masterdata-rebuild-assessment.md`
**Status:** approved (gate 1, PO 2026-10-11: assumptions 1–6 confirmed; `Description` max 2,500 characters)
**Date:** 2026-10-11

> Scope is exactly the ten entities of assessment §2.1a. Where this design deliberately differs from
> OpenTMS the assessment section is cited inline. Field lists were taken from
> `OpenTMS/aspnet-core/src/FiLogic.Core/BusinessEntities/<Entity>/`; the table "Deliberate differences
> from OpenTMS" at the end of *Domain impact* lists every dropped or changed field with its reason.
>
> **Platform change inside this design** (risk class critical, `/CLAUDE.md` rule 4): the
> `LocalizedName` value object in `Platform.Domain.Shared`, its EF Core function mapping in
> `Platform.EntityFrameworkCore`, the authorization architecture test in
> `test/OpenTms.ArchitectureTests`, and the HTTP status mapping for the module's conflict codes in
> `OpenTms.HttpApi`. Nothing else outside the Master Data module changes, except the app's nav
> entries in `frontend/src/app/apps.config.ts` (views, not a new app or mapping) and the Dutch manual.

## Domain impact

**Module:** Master Data (`backend/modules/master-data`), schema `masterdata`. One ABP module, five
projects as they exist today; the entities arrive through this approved design (AD-02, `/CLAUDE.md`
"Deliberately absent").

**Flow:** none of the three flows in `02-flows.md` is extended. The application services are the
commands (AD-18) that write these facts; nothing reacts to them yet, so **no event is published in
this run** (`03-events-and-outbox.md`: add an event when something reacts to it). Nothing runs in
the background (no flow 2) and nothing is ingested (no flow 3). When a later run needs a fact such as
"currency deactivated", it adds the ETO to `MasterData.Domain.Shared/Events` through the outbox
(AD-16).

**Published surface for later runs.** Other modules will reference these rows by id without a
database constraint (assessment §2.1 note 4, AO-11) and validate existence through this module's
`Application.Contracts`: the `I<Entity>AppService.GetLookupAsync` and `GetAsync` methods are that
contract. Whether a later module calls them synchronously is that module's design decision
(`/CLAUDE.md` module rule 3), not this one's.

**Decisions relied on:** AD-02 (modular monolith), AD-03 (database per tenant: the seed runs per
tenant database), AD-06 (`jsonb` for the document-shaped `LocalizedName`), AD-16/AD-18 (application
services as the command layer; no event yet). AD-07 and AD-08 are not touched: no derived value and no
money is stored here; `double` for measures is the PO's KEEP (assessment §3.5) and no money column
exists in this run, so the `decimal`-for-money question stays open for Financial.

### Common shape of every entity

- `FullAuditedAggregateRoot<Guid>, IMultiTenant`: server-generated id, audit fields, **soft delete**
  (assessment §3.12 keeps audit, tenant and soft delete on the base), ABP concurrency stamp
  (§3.14). `TenantId` is nullable and never defaulted (§3.6, §4); no `IDataFilter.Disable<IMultiTenant>()`
  anywhere in the module.
- Codes (`IsoCode`, `IsoAlpha2`, `IsoAlpha3`, `Code`, `SutcCode`) are stored trimmed and upper-cased
  by the entity; uniqueness is on the stored value.
- Every unique constraint is a **unique index on `(TenantId, <key>)` with `NULLS NOT DISTINCT`**
  (§4: PostgreSQL treats nulls as distinct, which would allow duplicate host rows later) **and the
  filter `"IsDeleted" = false`**, so a soft-deleted row frees its key.
- Names are `string(128)`, descriptions `string(2500)` (PO, gate 1), codes `string(32)` unless stated.
- Measures are `double?` with the unit in the name (§3.5 KEEP): `LengthCm`, `WidthCm`, `HeightCm`,
  `WeightKg`, `VolumeM3`, `LoadingMeters`, `MinCelsius`, `MaxCelsius`.
- Nothing UI-related on an entity (§3.12): no colours, flags or "visible in planboard".

### Entities

| Entity (table) | Fields | Keys and rules |
|---|---|---|
| **Currency** (`Currencies`) | `IsoCode` string(3) · `IsoNumeric` int? (ISO 4217 numeric, null for tenant-created) · `MinorUnits` int? (0–4, null for "N.A." currencies such as XAU) · `Symbol` string(8)? · `Name` LocalizedName · `Description`? · `IsActive` bool · `IsDefault` bool | unique `(TenantId, IsoCode)`; `IsoCode` matches `^[A-Z]{3}$`; **one default per tenant**: unique partial index on `(TenantId)` where `IsDefault` (§3.8, the one enforced place); the default must be active; `IsDefault` is only changed through `SetDefault` |
| **Country** (`Countries`) | `IsoAlpha2` string(2) · `IsoAlpha3` string(3) · `IsoNumeric` int? · `Name` LocalizedName · `Description`? · `DefaultCurrencyId` Guid? **FK → Currencies, restrict** · `PrimaryTimeZoneId` string(64)? (IANA) | unique `(TenantId, IsoAlpha2)` and `(TenantId, IsoAlpha3)` (§3.7: correctly named ISO keys); the currency is nullable because ISO lists territories without one (Antarctica); the timezone is nullable because tzdata has none for some territories (Bouvet, Heard); when set, it must be a known IANA id (`TimeZoneInfo.TryFindSystemTimeZoneById`); the default VAT reference comes with Financial as an id without constraint |
| **AddressType** (`AddressTypes`) | `Kind` enum `AddressTypeKind { Other = 0, Visiting = 1, Postal = 2 }` · `Name` LocalizedName · `Description`? | unique partial index on `(TenantId, Kind)` where `Kind <> Other`: code that needs "the visiting address" finds exactly one type by kind and never matches on a name; the tenant may add any number of `Other` types |
| **RelationCategory** (`RelationCategories`) | `Name` · `Description`? | unique `(TenantId, Name)`; no system flags and no default (§3.1: roles become a typed set on Relation in a later run; categories stay free classification) |
| **TransportCharacteristics** (`TransportCharacteristics`) | `Code` · `Name` · `Description`? | unique `(TenantId, Code)` (§3.2) |
| **Temperature** (`Temperatures`) | `Name` · `Description`? · `MinCelsius` double? · `MaxCelsius` double? · `IsArchived` bool | unique `(TenantId, Name)`; check `MinCelsius <= MaxCelsius` when both set; incompatibilities in `TemperatureIncompatibilities` (below) |
| **Product** (`Products`) | `Name` · `Description`? · `IsArchived` · ADR block (PO option A, 2026-10-11: the ADR Table A columns plus the document flags): `IsAdr` bool, `AdrUnNumber` string(4)?, `AdrClass` string(8)?, `AdrClassificationCode` string(8)?, `AdrPackingGroup` string(3)? (`I`, `II`, `III`), `AdrDescription` string(256)? (the proper shipping name), `AdrTechnicalName` string(256)?, `AdrHazardLabels` string(32)?, `AdrHazardIdentificationNumber` string(4)? (Kemler), `AdrTunnelRestrictionCode` string(8)?, `AdrTransportCategory` int? (0–4, the 1.1.3.6 multiplier), `AdrIsEnvironmentallyHazardous`, `AdrIsLimitedQuantity`, `AdrIsWaste` bool, `AdrFlashpointCelsius` double?, `AdrSpecialProvisions` string(64)? · order-line defaults: `DefaultQuantity`, `DefaultWeightKg`, `DefaultColli`, `DefaultVolumeM3`, `DefaultLoadingMeters`, `DefaultEpq`, `DefaultLengthCm`, `DefaultWidthCm`, `DefaultHeightCm` (all double?) | unique `(TenantId, Name)`; when `IsAdr`, `AdrUnNumber` (`^\d{4}$`) and `AdrClass` are required; incompatibilities in `ProductIncompatibilities` |
| **TransportUnit** (`TransportUnits`) | `Name` · `SutcCode` string(16)? (the standard packaging code integrations match on) · `Description`? · `IsArchived` · `LengthCm`, `WidthCm`, `HeightCm`, `WeightKg`, `LoadingMeters`, `EpqFactor` (double?) · `IsExchangeable` bool (the pallet-exchange flag) | unique `(TenantId, Name)`; child `TransportUnitLoadFactors`; incompatibilities in `TransportUnitIncompatibilities`; balances and transactions are Financial (§2) |
| **TransportUnitLoadFactor** (`TransportUnitLoadFactors`) | `TransportUnitId` **FK → TransportUnits, cascade** · `VehicleTypeId` **FK → VehicleTypes, restrict** · `LoadingCapacityFactor` double? · `MaxCapacity` double? | owned by the unit (`Entity<Guid>`, not an aggregate root); unique `(TransportUnitId, VehicleTypeId)` |
| **VehicleType** (`VehicleTypes`) | `Name` · `Description`? · `Kind` enum `VehicleTypeKind { Tractor = 1, Trailer = 2, Rigid = 3, Van = 4 }` · `MaxEpq`, `MaxLoadingMeters`, `DefaultLengthCm`, `DefaultWidthCm`, `DefaultHeightCm`, `DefaultGrossVehicleWeightKg` (double?) | unique `(TenantId, Name)`; join `VehicleTypeTransportCharacteristics` (`VehicleTypeId`, `TransportCharacteristicsId`), composite PK, both FKs cascade (§3.3: a join table, no `;`-string) |
| **GeographicalRegion** (`GeographicalRegions`) | `Name` · `Description`? | unique `(TenantId, Name)`; child `GeographicalRegionPostalCodeRanges`; relation membership and default via-waypoints come later (§2) |
| **GeographicalRegionPostalCodeRange** (`GeographicalRegionPostalCodeRanges`) | `GeographicalRegionId` **FK → GeographicalRegions, cascade** · `CountryId` **FK → Countries, restrict** · `FromNumeric` int? · `TillNumeric` int? · `Prefix` string(16)? | owned by the region; check: either (`Prefix` set, numeric fields null) or (`FromNumeric` and `TillNumeric` set, `FromNumeric <= TillNumeric`, `Prefix` null); `Prefix` stored normalised (upper-case, no spaces); index `(CountryId)` for the matcher |

**Incompatibility join tables** (`TemperatureIncompatibilities`, `ProductIncompatibilities`,
`TransportUnitIncompatibilities`): one undirected row per pair, columns `LeftId`, `RightId` (both FK
to the owning table, cascade), composite PK, check `LeftId <> RightId`. The pair is canonical:
`LeftId` is the id whose 32-character hex form sorts first ordinally (this equals PostgreSQL's `uuid`
order), enforced by the domain method that adds a pair, so "A with B" and "B with A" are the same row
and symmetry cannot drift (§3.3: a join table replaces `DoNotCombineWith`; assessment §5: one rule
instead of three). A row's incompatibles are read with `LeftId == id || RightId == id`.

**Children on update** (load factors, postal-code ranges): the update DTO carries the full
collection; the service matches by id, updates matching rows, inserts rows without id, deletes the
rest, all in the aggregate's unit of work.

**Delete.** Foreign keys declare the integrity (`restrict` where another aggregate points at the row,
`cascade` for owned children and join rows; §3.2). Because the base keeps soft delete, a soft delete
would not fire a restrict, so the service additionally refuses a delete with a `409` when a known
in-module reference exists: a currency used by a country or marked default; a country used by a
postal-code range; a vehicle type used by a load factor. The check is one `AnyAsync` per referencing
table, in the owning module only. Cross-module references (a relation's country, an order line's
product) arrive in later runs and are protected there, by the module that holds the id.

**Defaults.** The only default in this run is the tenant's default currency, enforced by the partial
unique index and changed through one command, `SetDefault`, which clears the previous default in the
same transaction (§3.8). `IsDefault` was deliberately **not** kept on Product, TransportUnit,
VehicleType and Country: those defaults belong to the settings of the consuming module (Orders,
Planning) and arrive there, so a default exists in one place.

### `LocalizedName` (Platform `Domain.Shared`, PO approved)

```csharp
public sealed class LocalizedName : IEquatable<LocalizedName>
{
    public const string DefaultCulture = "en";              // ABP's default resource culture; see Assumptions
    public IReadOnlyDictionary<string, string> Texts { get; } // culture code → text, e.g. { "nl": "Nederland", "en": "Netherlands" }
    public static LocalizedName Create(IReadOnlyDictionary<string, string> texts); // trims, drops empty texts, lower-cases culture keys; throws when the result is empty
    public string Resolve(string? culture);                  // culture → its language part ("nl-NL" → "nl") → DefaultCulture → first text
    public LocalizedName With(string culture, string text);
}
```

- Stored as one `jsonb` column (AD-06), `{"nl": "...", "en": "..."}`, through a value converter; the
  precedent is `ApplicationInstance.HealthDetails`.
- Querying and sorting on the resolved name in SQL: `Platform.EntityFrameworkCore` maps a database
  function `LocalizedNameDbFunctions.Text(LocalizedName name, string culture)` to PostgreSQL's
  `jsonb_extract_path_text` (`HasDbFunction(...).HasTranslation(...)`, no raw SQL). A list query sorts
  and searches on `Text(name, uiCulture) ?? Text(name, DefaultCulture)`. The function has no SQLite
  translation, so the list and lookup tests of the three localized entities run on the PostgreSQL
  test module; everything else about them runs on SQLite.
- Application services return `name` (resolved for `CultureInfo.CurrentUICulture`) on every row and
  lookup DTO, and `names` (the full map) on the detail DTO for editors. Editors show one field per
  configured language (nl, en); the `DefaultCulture` field is required, the others optional.
- Validation on input: keys must be configured languages (`AbpLocalizationOptions.Languages`), texts
  at most 128 characters, the default-culture text present.

### Postal-code matching (GeographicalRegion)

A pure domain function `PostalCodeRangeMatcher.Matches(range, postalCode)`:

1. Normalise the postcode: trim, upper-case, remove spaces. Empty → no match.
2. Prefix range: match when the normalised postcode starts with the normalised prefix.
3. Numeric range: take the **leading digit run** of the postcode (`1234AB` → 1234, `80331` → 80331).
   No leading digits (`SW1A1AA`, `D02X285`) → **no match** (assessment §5: today every numeric range
   matches such a postcode). Otherwise match when `FromNumeric <= n <= TillNumeric`.
4. A range belongs to one country; the resolver only evaluates ranges of the requested country.

`IGeographicalRegionAppService.MatchAsync(countryId, postalCode)` returns the regions with at least
one matching range, ordered by resolved name. The query loads the country's ranges (index on
`CountryId`; at most a few thousand rows per tenant) and evaluates the function in memory, so the
rule lives in one place and is unit-tested as a table.

### Seed (assessment §2.2, embedded in `MasterData.Domain.Shared/Seed/`)

Three embedded JSON files, registered in the ABP virtual file system: `iso-4217-currencies.json`,
`iso-3166-countries.json`, `address-types.json`. A committed generator script
(`scripts/masterdata-seed/generate.mjs`, Node, run by hand) produces the first two from: the ISO 4217
list-one publication (code, numeric, minor units, the country-to-currency column), ICU display names
through `Intl.DisplayNames` for nl and en (region and currency names), and tzdata's `zone.tab` for the
primary IANA zone per country (first listed zone, with an override table inside the script for
countries whose first zone is not the capital's, such as RU and US). Manual overrides and omissions
are listed in the script so the JSON is reproducible and reviewable in the PR.

`MasterDataDataSeedContributor` (`MasterData.Domain`, an `IDataSeedContributor`, so the DbMigrator
loads it without the Application layer) runs inside `ICurrentTenant.Change(context.TenantId)`:

- Skips when `context.TenantId` is null: host-level rows are parked (§4).
- Idempotent, keyed on the natural key: it reads the existing keys of the tenant in one query per
  entity **with the soft-delete filter disabled for that read** (`IDataFilter.Disable<ISoftDelete>()`
  with a comment; the multi-tenancy filter is untouched, constitution rule 1), inserts the missing
  rows in one `InsertManyAsync` per entity, and **never resurrects or re-inserts a soft-deleted row**.
- Existing rows: the seed re-asserts only the ISO-fixed fields a tenant cannot sensibly change
  (`IsoNumeric`, `MinorUnits`; `IsoAlpha3`, `IsoNumeric`; `Kind`) and leaves names, symbol, activity,
  description, timezone and default currency alone: the tenant owns those.
- Currency: all ISO 4217 currencies, `IsActive` true for EUR only. EUR becomes the default only
  when the tenant has no default yet.
- Country: all ISO 3166-1 countries, `DefaultCurrencyId` resolved by ISO code against the tenant's
  currency rows in the same run, timezone from the file. Country rows are inserted after currencies.
- AddressType: `Visiting` and `Postal`, names nl/en.
- Nothing from `DefaultTenantDataBuilder` is copied (§2.2 names its errors); the other seven entities
  are not seeded.
- Logs tenant id and counts only.

### Feature flag and permissions

- Feature `MasterData.FirstRun` (group `MasterData`, toggle, default `false`), defined in
  `MasterData.Application.Contracts/Features/`. Every application service of the module carries
  `[RequiresFeature(MasterDataFeatures.FirstRun)]` at class level; the frontend hides the app's
  content behind `useIsFeatureEnabled`.
- Permission group `MasterData` with, per entity, `MasterData.<Entities>` (view) and children
  `.Create`, `.Update`, `.Delete`, all `MultiTenancySides.Both` (§3.11, §4): `Currencies`, `Countries`,
  `AddressTypes`, `RelationCategories`, `TransportCharacteristics`, `Temperatures`, `Products`,
  `TransportUnits`, `VehicleTypes`, `GeographicalRegions`. `SetDefault` on a currency is `.Update`;
  `Lookup` and `Match` are the view permission; `DeleteMany` is `.Delete`. ABP's permission seeder
  grants them to the admin role as it does today.
- **Architecture test** (`test/OpenTms.ArchitectureTests/AuthorizationCoverageTests`): for every
  `OpenTms.<Module>.Application` assembly, every public non-abstract `IApplicationService` class and
  every public instance method it declares must carry `[Authorize]` (class or method) or an explicit
  `[AllowAnonymous]`; a class marked `[RemoteService(IsEnabled = false)]` is exempt there and its
  controller actions in `OpenTms.HttpApi` are checked instead by the same test (that is how the
  Platform administration services are built today). It fails the build on a violation (§3.11).

### Deliberate differences from OpenTMS

| Entity | Dropped or changed | Reason |
|---|---|---|
| all | `Flags`, `DisplayColor`, `IncludeInGenericListView`, `VisibleInPlanboard` | UI concern on the entity (§3.12) |
| all | `DoNotCombineWith` strings, `VehicleTypeCapability` without constraints | join tables with constraints (§3.3, §3.2) |
| all | client-supplied ids, `[AutoMapTo]` edit DTOs | server ids, separate Create/Update DTOs, explicit mapping, concurrency stamp (§3.14) |
| all | `TenantId ?? 1` | `IMultiTenant`, nullable, no fallback, no filter bypass (§3.6) |
| Currency | `IsDefault` without constraint; `Sign` → `Symbol`; `Iso4217_3` → `IsoCode` | single enforced default (§3.8); correct ISO naming (§3.7); `IsoNumeric`, `MinorUnits`, `IsActive` added (§2.1a) |
| Currency | `CurrencyDayRate` | Financial, later (§2) |
| Country | `Iso3166_2` (held alpha-2, not unique) → `IsoAlpha2` unique; `Iso3166_3` → `IsoAlpha3`; `Iso3166_Number` → `IsoNumeric` | §3.7 |
| Country | `IsDefault` | no default country in this run; a tenant default belongs to the setting that uses it (§3.8) |
| Country | `DefaultVatId` | Financial, later, as an id without constraint (§2.1 note 4) |
| Country | `DefaultLanguage`, `DefaultPostalCodeFormat` | not in the brief's field list; both can be added additively when a consumer needs them (postal format belongs with address validation in the Relation run) |
| Country | `DefaultTimeZone` (Windows id) → `PrimaryTimeZoneId` (IANA) | §2.2 |
| AddressType | `IsVisitingAddress` + `IsPostalAddress` booleans | one `Kind` enum, at most one Visiting and one Postal type per tenant (§2.1a) |
| RelationCategory | `IsDefault`, seven `Is*Category` flags, `IsSystemCategory` | roles become a typed set on Relation (§3.1); categories are free classification |
| Temperature | name `TemperatureRange`; `MinTemperature`/`MaxTemperature` | PO name; unit in the name: `MinCelsius`/`MaxCelsius` (§3.5) |
| Product | `IsDefault` | belongs to Orders intake settings (§3.8) |
| Product | `TransactionTime`, `ProductTransactionTime` | per waypoint type, with Orders (§2.1 note 3) |
| Product | `AdrLanguage` | the document language is not a product property. The other ADR fields OpenTMS had are kept and completed with classification code, transport category, limited quantity, flashpoint and special provisions (PO option A, 2026-10-11): what a transport document and the 1.1.3.6 exemption need. A dangerous-goods catalogue (§5 "Products and packaging") stays a later design; the product's columns then become the override layer |
| Product | `DefaultAmount` → `DefaultQuantity`; `DefaultWeight` → `DefaultWeightKg`, `DefaultVolume` → `DefaultVolumeM3`, `DefaultEPQ` → `DefaultEpq`, `DefaultWidth/Length/Height` → `…Cm` | unit in the name (§3.5) |
| TransportUnit | `IsDefault`, `RegisterTransactions`, `OnlyForTransactions`, `TransactionTime`, `TransportUnitTransactionTime`, the `Default*` line defaults | packaging type only; balances are Financial, transaction times Orders (§2, brief); `RegisterTransactions` becomes the explicit `IsExchangeable` flag |
| TransportUnit | `ConversionFactorForEpq` → `EpqFactor`, `ConversionFactorForLoadingMeters` → `LoadingMeters`; `SutcStandardCode` → `SutcCode` | names say what they hold |
| TransportUnit | load factors | kept as a child table with a unique `(unit, vehicle type)` and real FKs (§3.2) |
| VehicleType | `IsDefault` | Planning settings, later (§3.8) |
| VehicleType | `DefaultFuelType`, `SpeedCorrectionFactor`, `CostPricePerKm` | not in the brief's field list: fuel type arrives with Vehicle/CO₂ (Reporting), speed correction with routing (AD-12), cost price with Financial tariffs; all additive later |
| VehicleType | `Kind` added | brief |
| GeographicalRegion | `VisibleInPlanboard` | §3.12 |
| GeographicalRegion | `GeographicalRegionDefaultViaWaypoint` | Planning configuration, later (§2) |
| GeographicalRegion | numeric-range matching of postcodes without digits | fixed (§5 "Reference lists") |

## API contract

Conventional controllers from the `MasterData.Application` assembly, route root `master-data`
(already configured in the host). One application service per entity; ABP's conventions give the
routes below. `openapi/opentms-next.json` and the typed client are regenerated and are the contract
the frontend builds against.

| Entity | Service | Route root |
|---|---|---|
| Currency | `CurrencyAppService` | `/api/master-data/currency` |
| Country | `CountryAppService` | `/api/master-data/country` |
| AddressType | `AddressTypeAppService` | `/api/master-data/address-type` |
| RelationCategory | `RelationCategoryAppService` | `/api/master-data/relation-category` |
| TransportCharacteristics | `TransportCharacteristicsAppService` | `/api/master-data/transport-characteristics` |
| Temperature | `TemperatureAppService` | `/api/master-data/temperature` |
| Product | `ProductAppService` | `/api/master-data/product` |
| TransportUnit | `TransportUnitAppService` | `/api/master-data/transport-unit` |
| VehicleType | `VehicleTypeAppService` | `/api/master-data/vehicle-type` |
| GeographicalRegion | `GeographicalRegionAppService` | `/api/master-data/geographical-region` |

Every service exposes the same seven operations (permission in brackets):

| Method | HTTP | Request | Response |
|---|---|---|---|
| `GetListAsync` [view] | `GET /` | `<E>ListRequestDto` from the query string | `GridResponse<<E>RowDto>` |
| `GetAsync` [view] | `GET /{id}` | | `<E>Dto` (404 when absent) |
| `GetLookupAsync` [view] | `GET /lookup?term=&take=` | `term` ≤ 100 chars, `take` 1–500 (default 500) | `List<MasterDataLookupDto>`, non-archived / active rows only, ordered by resolved name |
| `CreateAsync` [.Create] | `POST /` | `<E>CreateDto` | `<E>Dto` |
| `UpdateAsync` [.Update] | `PUT /{id}` | `<E>UpdateDto` (carries `concurrencyStamp`) | `<E>Dto` |
| `DeleteAsync` [.Delete] | `DELETE /{id}` | | 204; 409 when in use or default |
| `BulkDeleteAsync` [.Delete] | `POST /bulk-delete` | `<E>BulkDeleteRequestDto { selection: GridSelectionDto, …filters }` | `MasterDataBulkDeleteResultDto { deletedCount, skippedRows[] { id, name, reason } }` |

Entity-specific operations:

| Method | HTTP | Request | Response |
|---|---|---|---|
| `CurrencyAppService.SetDefaultAsync` [Currencies.Update] | `POST /currency/{id}/set-default` | | 204; 409 when the currency is inactive |
| `GeographicalRegionAppService.GetMatchesAsync` [GeographicalRegions] | `GET /geographical-region/matches/{countryId}?postalCode=` | `postalCode` ≤ 16 chars | `List<MasterDataLookupDto>` |

**List request** (`MasterDataListRequestDto`, the explicit filter contract of §3.13, flat so the
query string stays readable and the generated client simple; the Platform administration lists use
the same shape): `startRow` ≥ 0, `endRow` > `startRow` and at most 200 rows apart, `wildcardSearch` ≤
200 chars, `sortBy` one of the row DTO's column ids (whitelist; anything else is rejected with 400),
`sortDir` `asc` | `desc`. Without `sortBy` the server orders by resolved name (localized entities) or
`name` ascending. `wildcardSearch` is a case-insensitive contains over a **defined column set per
entity**: codes and the resolved name for the localized three; `name`, `code`/`sutcCode` and
`description` for the others; `adrUnNumber` for products. Entity filters extend the request:
`CurrencyListRequestDto.isActive?`, `ArchivableListRequestDto.includeArchived` (default false; shared by
Temperature, Product and TransportUnit), `GeographicalRegionListRequestDto.countryId?` (regions with a range
in that country). The response carries `totalCount` (tenant rows visible with the default filters)
and `filteredCount`.

**Bulk delete:** `selection.mode = Explicit` deletes `explicitIds`; `FilterBased` resolves the ids
with `selection.filterRequest.wildcardSearch` and the request's entity filters minus `excludedIds`
(bounded by the entity's volume; more than 1,000 rows in one call is refused with `TooManyRows`).
Each row goes through the same in-use and default checks as `DeleteAsync`; refused rows are returned
as skipped with a localized reason, the rest is deleted in one unit of work. The method is named
`BulkDeleteAsync` because ABP maps a `Delete` prefix to the DELETE verb, which has no body.

**DTOs** (`Application.Contracts/<Entity>/`), one row DTO for grids, one detail DTO for editors and
`GetAsync`, separate create and update DTOs (§3.14), validated with data annotations:

- `MasterDataLookupDto { id, name, code? }` (shared by every lookup, incompatibility list and match).
- `MasterDataAuditDto` fields on every detail DTO: `creationTime`, `creatorId`, `lastModificationTime`,
  `lastModifierId`, `concurrencyStamp` (for the Record identification tab and the update round trip).
- **Currency** row: `id, isoCode, isoNumeric, minorUnits, symbol, name, isActive, isDefault`. Detail:
  row + `names` (map) + `description` + audit. Create: `isoCode` (required, `^[A-Za-z]{3}$`),
  `isoNumeric?` (0–999), `minorUnits?` (0–4), `symbol?`, `names` (map, default culture required),
  `description?`, `isActive`. Update: the same + `concurrencyStamp`. `isDefault` is not writable.
- **Country** row: `id, isoAlpha2, isoAlpha3, isoNumeric, name, defaultCurrencyId, defaultCurrencyCode,
  primaryTimeZoneId`. Detail: row + `names`, `description`, audit. Create/Update: `isoAlpha2`
  (`^[A-Za-z]{2}$`), `isoAlpha3` (`^[A-Za-z]{3}$`), `isoNumeric?`, `names`, `description?`,
  `defaultCurrencyId?` (must exist in the tenant), `primaryTimeZoneId?` (valid IANA id).
- **AddressType** row: `id, kind, name`. Detail: + `names`, `description`, audit. Create/Update:
  `kind` (enum), `names`, `description?`.
- **RelationCategory** row: `id, name, description`. Create/Update: `name` (required), `description?`.
- **TransportCharacteristics** row: `id, code, name, description`. Create/Update: `code` (required),
  `name` (required), `description?`.
- **Temperature** row: `id, name, minCelsius, maxCelsius, isArchived, description`. Detail: +
  `incompatibleTemperatures: MasterDataLookupDto[]`, audit. Create/Update: `name`, `description?`,
  `minCelsius?`, `maxCelsius?`, `isArchived`, `incompatibleTemperatureIds: Guid[]` (no self, must
  exist).
- **Product** row: `id, name, isAdr, adrUnNumber, adrClass, isArchived, defaultWeightKg,
  defaultLoadingMeters, defaultEpq`. Detail: every field of the entity + `incompatibleProducts` +
  audit. Create/Update: every writable field + `incompatibleProductIds`.
- **TransportUnit** row: `id, name, sutcCode, lengthCm, widthCm, heightCm, weightKg, loadingMeters,
  epqFactor, isExchangeable, isArchived`. Detail: + `description`, `loadFactors[] { id, vehicleTypeId,
  vehicleTypeName, loadingCapacityFactor, maxCapacity }`, `incompatibleTransportUnits`, audit.
  Create/Update: writable fields + `loadFactors[] { id?, vehicleTypeId, loadingCapacityFactor?,
  maxCapacity? }` (unique `vehicleTypeId`) + `incompatibleTransportUnitIds`.
- **VehicleType** row: `id, name, kind, maxEpq, maxLoadingMeters, defaultLengthCm, defaultWidthCm,
  defaultHeightCm, defaultGrossVehicleWeightKg`. Detail: + `description`, `transportCharacteristics:
  MasterDataLookupDto[]`, audit. Create/Update: writable fields + `transportCharacteristicsIds`.
- **GeographicalRegion** row: `id, name, description, rangeCount`. Detail: + `postalCodeRanges[] { id,
  countryId, countryCode, fromNumeric, tillNumeric, prefix }`, audit. Create/Update: `name`,
  `description?`, `postalCodeRanges[] { id?, countryId, fromNumeric?, tillNumeric?, prefix? }`.

**Errors.** Business rules throw `BusinessException` with codes in the module's namespace (mapped to
the `MasterData` resource today): `OpenTms.MasterData:DuplicateKey` (with `field` and `value` data),
`OpenTms.MasterData:InUse` (with `referencedBy`), `OpenTms.MasterData:DefaultCannotBeDeleted`,
`OpenTms.MasterData:DefaultMustBeActive`, `OpenTms.MasterData:InvalidTimeZone`,
`OpenTms.MasterData:SelfIncompatibility`, `OpenTms.MasterData:AdrFieldsRequired`,
`OpenTms.MasterData:InvalidPostalCodeRange`, `OpenTms.MasterData:InvalidTemperatureRange`,
`OpenTms.MasterData:InvalidCode`, `OpenTms.MasterData:ReferenceNotFound`, `OpenTms.MasterData:DuplicateLoadFactor`,
`OpenTms.MasterData:UnknownSortColumn`, `OpenTms.MasterData:NameRequired`, `OpenTms.MasterData:TooManyRows`. `OpenTms.HttpApi` maps the `OpenTms.MasterData:*`
conflict codes to **409** and the request-shape codes to **400** through `AbpExceptionHttpStatusCodeOptions`
(ABP's default for a business exception is 403, which the UI would misread as a permission problem). Validation failures are ABP's
400 with field errors; a stale `concurrencyStamp` is ABP's 409; a missing permission 403; a disabled
feature ABP's 403 with the feature error code.

## Migration strategy

One additive migration in the Master Data context, `MasterDataFirstRun`, applied by the DbMigrator to
the host database and every tenant database (the schema is shared; host rows stay empty until §4 is
built):

- Tables: `Currencies`, `Countries`, `AddressTypes`, `RelationCategories`, `TransportCharacteristics`,
  `Temperatures`, `Products`, `TransportUnits`, `TransportUnitLoadFactors`, `VehicleTypes`,
  `VehicleTypeTransportCharacteristics`, `GeographicalRegions`, `GeographicalRegionPostalCodeRanges`,
  `TemperatureIncompatibilities`, `ProductIncompatibilities`, `TransportUnitIncompatibilities`, all in
  schema `masterdata`.
- Indexes, all in this migration because the queries that need them ship with it: the unique
  `(TenantId, key)` indexes with `NULLS NOT DISTINCT` and the `IsDeleted = false` filter listed per
  entity; the partial unique indexes for the default currency and the address-type kinds; a plain
  `(TenantId)` index on every root table (every list is tenant-scoped; ABP adds none); `(CountryId)`
  on postal-code ranges; `(VehicleTypeId)` on load factors; `(DefaultCurrencyId)` on countries.
- Foreign keys and check constraints as listed under *Domain impact*.
- Expand only: nothing is renamed or dropped, no data is moved, no contract step follows. `Down`
  drops the tables.
- The seed runs in the same DbMigrator pass, after the migration, per tenant.

Verification: the DbMigrator applies it to the local host database and the `demo` tenant; the
PostgreSQL test module creates its three databases with the real migrator, so the migration is
exercised on every test run.

## UI design

**Routes and navigation.** `frontend/src/app/apps.config.ts` gives the existing Master data app its
views (nav entries, no new app, no mapping change), grouped as the old menu was:

| Group | Views (route under `/master-data`) |
|---|---|
| Relations (`MasterData:Nav.Relations`) | Address types `/address-types`, Relation categories `/relation-categories` |
| Transport (`MasterData:Nav.Transport`) | Products `/products`, Transport units `/transport-units`, Temperatures `/temperatures` |
| Fleet (`MasterData:Nav.Fleet`) | Vehicle types `/vehicle-types`, Transport characteristics `/transport-characteristics` |
| Regions and lists (`MasterData:Nav.Reference`) | Countries `/countries`, Currencies `/currencies`, Geographical regions `/geographical-regions` |

`src/apps/master-data/index.tsx` routes by pathname like the Administration app and redirects bare
`/master-data` to `/master-data/address-types`. The app is lazy-loaded like Administration (bundle
budget per route). When the feature is off, every view renders the `PermissionDenied` panel and the
nav entries stay listed, exactly as the Maintenance view does for its tenant feature (the nav pane has
no feature gating today; adding it is a shell change outside this design). Without the entity's view
permission the same panel shows (UI-PAGE-005).

**Code layout.** `src/domains/master-data/<entity>.ts` holds the hooks per entity (grid fetcher,
`use<Entity>`, create, update, delete, delete-many, lookup; `useMatchRegions`), all through the
generated client; `localized-name.ts` holds the editor helper. `src/apps/master-data/views/<Entity>View.tsx`
is the list page, `src/apps/master-data/components/<Entity>Dialog.tsx` the editor
(UI-DLG-009: one editor per entity, `mode: "create" | "edit"`, `readOnly` without `.Update`).

**List pages** (UI-PAGE-001): `AppCommandBar` + `OpenTmsGrid` (`gridId` `masterData.<entity>`), block
loading, `onOpen` opens the editor, `rowActions` = Edit, Delete (multi, destructive), command bar = New
(primary), Delete (`requiresSelection`, calls delete-many, result toast names deleted and skipped
counts). Filters in the toolbar slot: Currencies "Active only" (`Checkbox`, default on);
Temperatures, Products, Transport units "Show archived" (default off); Geographical regions a Country
`Combobox`. No export in this run. Default visible columns (UI-GRID-017; every other row field ships
hidden):

| List | Default columns |
|---|---|
| Currencies | ISO code, Name, Symbol, Minor units, Active (`BoolCell`), Default (`BoolCell`) |
| Countries | Alpha-2, Alpha-3, Name, Default currency (code), Time zone |
| Address types | Name, Kind (localized enum text) |
| Relation categories | Name, Description |
| Transport characteristics | Code, Name, Description |
| Temperatures | Name, Min (°C), Max (°C) (`NumberCell`, unit in header, UI-FMT-008), Archived |
| Products | Name, ADR (`BoolCell`), UN number, Class, Weight (kg), Loading metres (ldm), EPQ, Archived |
| Transport units | Name, SUTC code, L (cm), W (cm), H (cm), Weight (kg), Loading metres (ldm), EPQ factor, Exchangeable, Archived |
| Vehicle types | Name, Kind, Max EPQ, Max loading metres (ldm), Gross weight (kg) |
| Geographical regions | Name, Description, Ranges (`NumberCell`) |

Server order (no client default sort, UI-GRID-012): resolved name or name ascending.

**Editors** (UI-DLG-002/004/005/015, UI-FORM-008): `EntityDialog variant="editor"`, title
`t("MasterData:New<Entity>")` or `t("MasterData:<Entity>") + " " + <name or code>`, ribbon Save,
Close (added by the dialog), Delete (edit mode, `.Delete`), tabs General first, entity tabs, Record
identification last. Validation on blur and submit under the field (UI-FORM-006); server errors as a
destructive `Alert` in the body (UI-FB-004); success closes, toasts `MasterData:<Entity>Saved` and
invalidates the list (UI-FB-002). Delete from the ribbon or a row goes through `ConfirmDialog`
naming the record (UI-DLG-011).

| Editor | Size | Tabs and fields |
|---|---|---|
| Currency | `sm` | General: ISO code, Numeric code, Minor units, Symbol, Name (nl), Name (en), Description, Active. Default is shown as a badge after the title; the ribbon command **Set as default** (`.Update`, hidden when already default, confirmed) calls `set-default` |
| Country | `sm` | General: Alpha-2, Alpha-3, Numeric, Name (nl), Name (en), Default currency (`Combobox` over the currency lookup, active ones), Time zone (`Combobox` over `Intl.supportedValuesOf("timeZone")`, client-side search), Description |
| Address type | `sm` | General: Kind (`Select`), Name (nl), Name (en), Description |
| Relation category | `sm` | General: Name, Description |
| Transport characteristics | `sm` | General: Code, Name, Description |
| Temperature | `md` | General: Name, Description, Min (°C), Max (°C), Archived; **Do not combine with**: `MultiSelect` over the temperature lookup (self excluded) |
| Product | `md` | General, two `FormSection`s: *Product* (Name, Description, Archived) and *Defaults per line* (Quantity, Weight (kg), Colli, Volume (m³), Loading metres (ldm), EPQ, Length/Width/Height (cm)); **ADR**: ADR checkbox, UN number, Class, Packing group (`Select`), Description, fields disabled until ADR is checked; **Do not combine with**: `MultiSelect` over the product lookup |
| Transport unit | `md` | General: Name, SUTC code, Description, L/W/H (cm), Weight (kg), Loading metres (ldm), EPQ factor, Exchangeable, Archived; **Load factors**: `LinesGrid` (Vehicle type via lookup, Loading capacity factor, Max capacity; add/remove, unique vehicle type validated on submit); **Do not combine with**: `MultiSelect` |
| Vehicle type | `md` | General: Name, Kind (`Select`), Description, Max EPQ, Max loading metres (ldm), Default L/W/H (cm), Gross vehicle weight (kg); **Characteristics**: `MultiSelect` over the characteristics lookup |
| Geographical region | `md` | General: Name, Description; **Postal-code ranges**: `LinesGrid` with Country (`Combobox` cell over the country lookup), From, Till, Prefix; a row must have either from+till or a prefix (validated on submit, UI-FORM-007 marks the tab) |

Localized names: `LocalizedNameFields` (`src/apps/master-data/components/`) renders one `FormField`
per language of the application configuration (nl, en), the default culture required; the grid and
pickers show the resolved `name`.

**Library additions** (reuse order of the coding conventions; nothing inline in a page):
shadcn `Select`, `Textarea`, `Badge` and `Combobox` (base-nova on `@base-ui/react`); **`ReferenceCombobox`**
(`components/ui/reference-combobox.tsx`): a single-value picker over a static list with client-side
search, for countries, currencies and time zones (UI-LOOK-001); **`MultiSelect`** built in
`components/ui/multi-select.tsx` (component map §3, "build") on the Combobox in multiple mode: a
searchable list with one checkable row per option, comma-separated display truncated with `+N`
(UI-LOOK-007), props `{ id, value: string[], onChange, options: { id, label }[], disabled, "aria-invalid" }`,
with a component test. `LinesGrid` already exists and is used for the two child collections
(UI-GRID-014); it now reports cell edits to the form state and keeps Escape inside an open cell editor.
The shell's router gets the Master data group and list paths next to the Administration ones.

**Not built here, by precedent:** `EntityLookup` (GAP-01 in the component map needs a PO choice).
Every picker in this run is a static or bounded list (≤ 500 rows through `GetLookupAsync`), which
UI-LOOK-001 serves with `Combobox`/`MultiSelect`; see Assumptions for the product-count limit.
`useOpenRecord` and the `?open=` record host (UI-DLG-007) are not built either: the Administration app
opens records through dialog state, and this design follows it, marked
`DEVIATION(UI-DLG-007): record host not yet built; editors open from dialog state as in the
Administration app, approved in this design` pending gate 1 (see Assumptions).

**States** (UI-PAGE-005): loading `Skeleton` in the grid slot, the grid's localized empty overlay,
block error with retry, `PermissionDenied` for a missing view permission or a disabled feature.
Dialog loading its record shows `Skeleton` blocks (UI-FB-006).

**Optimistic updates:** none. Reference data changes are rare and every mutation closes a dialog and
refreshes the list; the mutation budget (p95 < 500 ms) is met without them.

**Manual:** `docs/manual/stamgegevens.md` (Dutch) describes the ten lists, the seed, the default
currency, archiving and the postal-code range rules (reviewer item 11).

## Test risk analysis

| Part | Class | Mandatory coverage (test engineer decides execution) |
|---|---|---|
| Tenant isolation: list, get, lookup, update, delete, delete-many and match never return or touch another tenant's rows; the seed writes into the current tenant only; the shared-tier tenant sees its own rows in the host database | **Critical** | PostgreSQL integration tests (tenant A, shared tenant, host): negative tests per operation on at least Currency, Product and GeographicalRegion |
| Authorization: every service method carries its permission; the feature flag blocks the services; the architecture test fails on a method without `[Authorize]` | **Critical** | architecture test (self-test with a deliberately unattributed fake in the test assembly), permission definition test (40 names, both sides), `RequiresFeature` test, controller policy test on the generated routes |
| Migration: applies on host and tenant databases, expand-only | **Critical** | PostgreSQL test module creates the databases with the real DbMigrator; a model-snapshot test asserts no pending model changes; the DbMigrator run on `demo` is part of done |
| Seed: idempotent (second run inserts nothing), never resurrects a soft-deleted row, leaves tenant edits alone, re-asserts ISO-fixed fields, EUR active and default, country → currency and timezone resolved, skips the host | **Critical** | PostgreSQL: run twice, count rows, delete a row and re-run, rename a row and re-run; unit test on the JSON files (every country's currency code exists, every timezone id is known, nl and en present) |
| Uniqueness and the single default: duplicate key refused with 409 and by the database index; a soft-deleted row frees its key; `SetDefault` swaps atomically; two concurrent `SetDefault`s leave exactly one default; one Visiting and one Postal address type | **Critical** | PostgreSQL (the partial indexes are PostgreSQL-only) |
| Delete and in-use checks, delete-many skipping, concurrency stamp 409, child collection replace semantics, incompatibility canonical pair and self-check, load-factor uniqueness, ADR rule, timezone validation | **High** | application-service tests on SQLite (localized entities on PostgreSQL) per entity |
| `LocalizedName`: fallback chain, culture normalisation, validation, JSON round trip, equality | **High** | unit tests in `OpenTms.Domain.Tests/Platform` |
| Postcode matcher | **High** | table-driven unit tests: `1234AB` in 1000–1999, `1000` at both bounds, `SW1A1AA` against a numeric range (no match), `SW` prefix with spaces and lower case, empty postcode, prefix stored with spaces, a range of another country ignored |
| Performance at ×5 volume: 5,000 products, 1,000 regions with 50,000 ranges, 900 currencies | **High** | PostgreSQL volume test asserting the paged list, lookup and match stay under the interactive-read budget (p95 < 300 ms) with a CI margin, and that the plans use the indexes |
| Frontend: ten views (four states, permission gating, feature gating), ten editors (validation, dirty guard, read-only, submit mapping, server error), `MultiSelect`, `LocalizedNameFields`, hooks | **Medium** | vitest component tests per view and editor; hook tests on the generated client mock |
| Localization: every key used by the frontend and backend exists in nl and en | **Low** | key-parity test over the `MasterData` resource |

Idempotency obligations: the seed contributor (above) and `DeleteManyAsync` (a second run with the
same ids deletes nothing and reports zero). No job and no webhook is added.

## Flag & rollout plan

- Flag `MasterData.FirstRun`, group `MasterData`, default `false`, per tenant.
- Activation order: the health tenant → friendly tenants → all, flipped by the release manager
  through feature management. Locally: `demo` on.
- Existing tenant data: none; the tables are new. The seed runs on migration for every tenant whether
  the flag is on or not, so the data is ready the moment a tenant is switched on.
- Rollback: switch the flag off; the tables stay (expand phase). A later run never depends on the
  flag being on: the flag gates the screens and services, not the data.

## Cost & SLO impact

- Cloud SQL: 16 small tables per database; about 450 seeded rows per tenant (under 200 KB with
  indexes); at 1,000 tenants under 200 MB in total. No new connections, jobs or processes; no
  cross-tenant work in a request.
- DbMigrator: the seed adds roughly one second per tenant database (three key reads, three batched
  inserts).
- Egress and external calls: none. Cloud Run min instances: unchanged. Per-tenant margin: unaffected.
- Budgets touched: interactive reads (list, lookup, match: p95 < 300 ms, every query paged and
  indexed; the match endpoint reads one country's ranges, at most a few thousand rows) and mutations
  (p95 < 500 ms; the largest write is a region with 50 ranges or a unit with 20 load factors in one
  transaction). Frontend: the Master data app is a lazy route; AG Grid stays lazy-loaded.

## Assumptions

Listed for gate 1 to confirm or correct; the build follows them as written.

1. **AO-12** (which domain owns vehicle positions and email status) is open. This design touches
   VehicleType only as static reference data and stores no telematics state, so it does not depend on
   AO-12; listed because the brief asks for it. **AP-06** (event naming) is not used: no event is
   published in this run.
2. `LocalizedName.DefaultCulture` is `"en"`, ABP's default resource culture here. If the PO prefers
   Dutch as the fallback, the constant changes and nothing else.
3. Soft delete stays on the base (§3.12) and is combined with FK `restrict` plus an in-service in-use
   check for the three known references. The alternative, hard delete for reference data so the
   database alone enforces it, was not chosen because §3.12 keeps soft delete.
4. The seed never updates tenant-editable fields of an existing row and never resurrects a
   soft-deleted row ("upsert on the natural key" read as: insert when missing, re-assert the
   ISO-fixed fields otherwise). If the PO wants ISO names re-applied on every run, that is one line.
5. `Description` is kept on all ten entities; `IsArchived` is kept on Temperature, Product and
   TransportUnit (OpenTMS has it there; pickers hide archived rows). `IsDefault` is dropped on
   Product, TransportUnit, VehicleType and Country (§3.8) and returns as a setting of the consuming
   module.
6. Expected volumes are the PBI's figures; the test engineer multiplies them by five.
7. Incompatibility pickers use `MultiSelect` over the bounded lookup (500 rows). A tenant with more
   than 500 products needs `EntityLookup` (GAP-01, a PO library choice) before the product picker is
   complete; the lookup is truncated and the editor shows a warning `Alert` when `take` was reached.
8. `useOpenRecord` and the `?open=` record host (UI-DLG-007) are not built in this run; editors open
   from dialog state as the Administration app does. Recommendation: build the record host with the
   first transactional entity (Orders), where deep links matter. Gate 1 confirms the marked deviation.
9. The nav pane does not gate entries by feature; with the flag off the entries are visible and the
   content shows the `PermissionDenied` panel, as the Maintenance view does. A `feature` property on
   `AppView` would be a small shell change for a later design.
10. ABP's conventional controllers produce the kebab-case routes listed; if a generated route differs,
    the OpenAPI file is the contract and the frontend follows it, the design's names being the intent.
11. Two complex query parameters on a GET are avoided by design: the entity filters are properties of
    the list request DTO.
12. The 409 mapping of the module's conflict codes is configured in `OpenTms.HttpApi` (host glue, like
    the route roots); the module itself stays free of ASP.NET Core packages.
13. The seed generator's sources (ISO 4217 list-one, ICU display names, tzdata `zone.tab`) are
    acceptable; the resulting JSON is reviewed in the PR and manual overrides are listed in the script.
14. Volume test timing assertions carry a CI margin (the budget is enforced mechanically by the nightly
    k6 run; the unit-level assertion is a tripwire, not the measurement).
15. The file number 41 stands in for the issue number; no issue was opened.

## Security quickscan

- **Permissions:** 40 new ABP permission definitions (view, create, update, delete × ten), both sides;
  every public service method carries one; the architecture test enforces it for every module from
  now on. The feature flag is a second gate on every service.
- **Input validation:** data annotations on every DTO (lengths, ranges, regexes for ISO codes and UN
  numbers, enums validated by ABP), `take` and `endRow` bounded, `sortBy` whitelisted, the time zone
  validated against the runtime's IANA database, referenced ids (currency, country, vehicle type,
  incompatibles, characteristics) checked to exist in the tenant before writing. Codes are
  normalised before uniqueness checks so `eur` and `EUR` cannot coexist.
- **Attack surface:** ten conventional controllers behind authentication, permissions and the
  feature; no anonymous endpoint (the OpenTMS region lookups were anonymous, §3.11); no file upload;
  no raw SQL; no outbound call.
- **Tenant isolation:** every entity is `IMultiTenant`; no filter bypass; the seed disables only the
  soft-delete filter for one read, documented at the call site.
- **Logging:** tenant id, entity ids and counts only; names and codes are not logged.
- **Personal data:** none. GDPR art. 15 (export) and art. 17 (erasure): not applicable, there is no
  data subject in any of the ten entities.
