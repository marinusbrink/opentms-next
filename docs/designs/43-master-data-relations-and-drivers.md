# PBI: Master Data, second run: Relation and Driver

<!-- Written from the assessment docs/input/masterdata-rebuild-assessment.md (§2 task split, §3.1, §3.2,
     §3.15, §4) and the PO's instruction of 2026-10-11 to continue with the next Master Data entities after
     design 41. No GitHub issue was opened; "43" is the next free number after PR #42. -->

## Story

As a planner, an order clerk or an administrator I want one relation record for every party we deal
with (customers, carriers, creditors, depots and ourselves) with its addresses, contact persons and
bank accounts, and a driver record for every person who drives for us, because orders, trips,
invoices and the driver app all refer to these two records by id and none of them can be built
before they exist.

## Affected area

The Master Data domain: `Relation` with its children `RelationAddress`, `RelationContactPerson`,
`RelationBankAccount` and its memberships (categories, geographical regions, organisation units),
`Driver` with its qualifications, the relation matching service of assessment §3.15, their
application services under `/api/master-data/...`, and the Relations and Drivers screens of the
Master data app. It builds on design 41 (Country, AddressType, RelationCategory, GeographicalRegion).

## Tenant dimension

Configurable per tenant; every row belongs to one tenant. A new per-tenant feature flag
`MasterData.Relations`, default off. Host-level rows stay parked (assessment §4): `TenantId` nullable,
nothing assumes a tenant.

## Expected volumes

Per tenant, steady state (the test engineer multiplies by five):

| Entity | Rows | Note |
|---|---|---|
| Relation | up to 20,000 | a forwarder's customer and address book; most have one role |
| RelationAddress | 1.5 per relation (30,000) | visiting and postal; depots have one |
| RelationContactPerson | 2 per relation (40,000) | |
| RelationBankAccount | up to 1 per relation | carriers and creditors only |
| Driver | up to 500 | own drivers plus the carriers' regular drivers |
| DriverQualification | 3 per driver | licence, code 95, ADR |

Busiest screen: the relation list (20,000 rows, server-side paged). The relation lookup from an order
line (search by name or code) is the hottest read of the whole product later; the matching service
runs once per incoming order from an import. No external parties.

## Known edge cases

- A relation with several roles (a customer that is also a carrier); exactly one own-company relation
  per tenant; removing the own-company role from the only one.
- Duplicate natural keys: relation code, VAT number, chamber-of-commerce number, IBAN per relation,
  one address per address type per relation, one user account per driver.
- Matching an imported party: exact VAT or chamber-of-commerce hit, name plus postcode hit, several
  candidates, no candidate.
- A driver whose employer relation loses its carrier role, or is archived.
- Personal data: a contact person or driver who asks for export or erasure while trips in history
  reference the driver id.
- An address without coordinates, an address in a country without a time zone.
- Two users editing the same relation (concurrency stamp); an address removed while an order already
  references it (later runs: references are ids, see design).

## Privacy note

Yes. `RelationContactPerson` and `Driver` hold personal data (names, phone numbers, e-mail
addresses; a driver's licence and certificate numbers and dates). The design's security quickscan
gives the GDPR art. 15 and art. 17 answers and the mechanism. Relation itself holds company data;
a sole trader's relation name can be a person's name, which the export covers through the contact
person search.

---

# Design: Master Data, second run: Relation and Driver

**PBI:** above (this file); assessment `docs/input/masterdata-rebuild-assessment.md`
**Status:** approved (gate 1, PO 2026-10-11: assumptions 1, 3, 4 and 8 confirmed; GAP-03 decided as the `DatePicker` below). Revised by the PO on 2026-10-12 after the old OpenTMS relation screen: the General tab mirrors the old one with the default address inline ("all addresses equal", stored as an address without an address type), roles as a `MultiSelect`, a `Blocked` flag with a reason, a Map / Remarks section; fax, emergency number, instant messenger and Skype stay dropped.
**Date:** 2026-10-11
**Depends on:** design 41 (PR #42) merged: Country, AddressType, RelationCategory, GeographicalRegion,
`LocalizedName`, the Master data app and the shared list view and editor.

> Everything in this design follows the KEEP and CHANGE decisions of the assessment; the section is
> cited where a choice differs from OpenTMS. The one open library choice this design needs is
> **GAP-01 (`EntityLookup`)**, listed under *Assumptions* as the first item: without a server-searched
> picker, no screen can pick a relation out of 20,000.

## Domain impact

**Module:** Master Data (`backend/modules/master-data`), schema `masterdata`. Two new aggregates
(Relation, Driver) with their children, one additive migration.

**Flow:** the application services are commands (AD-18); no event is published in this run
(`03-events-and-outbox.md`: add an event when something reacts). The Orders and Financial runs that
need "relation archived" or "relation changed" add the ETO under `MasterData.Domain.Shared/Events`
with their design.

**Decisions relied on:** AD-02, AD-03, AD-06 (no jsonb in this run: every part is relational), AD-16,
AD-18. AD-07 and AD-08 untouched: no money, no derived value. **AO-12** is not depended on: no vehicle
position or device data lives here (the device binding of a driver is the Integrations run, assessment
§2 row 6 and §3.15).

**Cross-module references** (ids without constraint, validated through the owner's published surface):
`Driver.UserId` → ABP identity user (Platform), `RelationOrganizationUnit.OrganizationUnitId` → ABP
organisation unit (Platform). Both are host-managed tables outside the `masterdata` schema, so no
foreign key (module rule 1). In-module references are real foreign keys. Users are validated and
searched through ABP's `IIdentityUserLookupAppService` (its `AbpIdentity.UserLookup` permission is
needed next to `Drivers.Update` to link an account); organisation units through ABP's
`IOrganizationUnitRepository` (a tenant-filtered framework repository; the open-source Identity module
publishes no lookup service for them). Both are framework packages (`Volo.Abp.Identity.*`), not another
OpenTms module, so module rule 2 is not touched.

### Relation

The one Relation of assessment §3.1 (KEEP), narrow: identity, roles, contact details, legal
identifiers, and its children. Everything OpenTMS kept on the relation for invoicing, pricing,
intake, planning, portals and notifications (about 130 of its 160 columns) lives in the module that
uses it, keyed by RelationId (§2, §3.1): `RelationFinancialSettings`, `RelationOrderSettings`,
`RelationPlanningSettings` arrive with those runs.

| Field | Type | Rule |
|---|---|---|
| `Code` | string(32), required | the relation's short code, upper-cased, **unique per tenant** (§3.2); entered by the user in this run, assigned by the Platform number-sequence service (§3.9) once it exists |
| `Name` | string(128), required | stored once (§3.1: no `FullName`/`SearchName`); searched through `SearchKey` |
| `SearchKey` | string(160), generated by the entity | lower-case, diacritics folded, punctuation removed: `Name` plus `Code`; indexed, never shown |
| `Roles` | `RelationRoles` flags enum: `Customer = 1`, `Carrier = 2`, `Creditor = 4`, `Depot = 8`, `OwnCompany = 16` | at least one role; **exactly one own company per tenant**: unique partial index on `(TenantId)` where `("Roles" & 16) = 16` and not deleted (§3.1) |
| `IsArchived` | bool | hides the relation from pickers and the matcher; `IsBlocked` is **dropped** (§3.1: nothing enforced it; a credit hold is a Financial setting, an intake block an Orders setting) |
| `Phone`, `Mobile`, `Email`, `Website` | string(32/32/256/256)? | e-mail validated by shape; fax, instant messenger and Skype dropped |
| `ChamberOfCommerceNumber` | string(32)? | normalised (digits and letters, upper), **unique per tenant when set** |
| `VatNumber` | string(32)? | normalised (no spaces or dots, upper), **unique per tenant when set**; shape checked per country prefix (two letters plus 2–13 characters), no VIES call |
| `PreferredCulture` | string(10)? | the language of documents and mails for this relation ("nl", "en"); validated against the configured languages |
| `Description` | string(2500)? | internal remarks (the base field) |

Children, all owned (`Entity<Guid>`, `IMultiTenant`, cascade from the relation; replace semantics on
update as in design 41):

| Child | Fields | Rules |
|---|---|---|
| **RelationAddress** | `AddressTypeId` FK → AddressTypes (restrict), **null for the relation's default address** ("all addresses equal", PO 2026-10-12: it serves every type without an address of its own; at most one per relation, unique `(RelationId, AddressTypeId)` with NULLS NOT DISTINCT) · `CountryId` FK → Countries (restrict) · `Street` string(128) · `HouseNumber` string(16)? (number and suffix as typed: "12a", "12-14") · `AddressLine2` string(128)? · `PostalCode` string(16) · `City` string(128) · `Latitude`, `Longitude` double? · `TimeZoneId` string(64) | **one address per address type per relation** (unique `(RelationId, AddressTypeId)`), so "the visiting address" is one row found by kind through AddressType; `PostalCode` stored normalised (upper, no spaces) next to the typed form? No: stored as typed, compared normalised by the matcher; `TimeZoneId` defaults to the country's primary zone and is validated as an IANA id; coordinates come from a geocoder later (Planning), here optional input |
| **RelationContactPerson** | `FirstName` string(64) · `MiddleName` string(32)? · `LastName` string(64) · `Role` enum `ContactPersonRole { Other = 0, Planning, Invoicing, Sales, Management }` (§3.1: "with a role") · `JobTitle` string(64)? · `Department` string(64)? · `Email`, `Phone`, `Mobile` | no uniqueness (people share names); personal data, see quickscan |
| **RelationBankAccount** | `Iban` string(34) · `Bic` string(11)? · `BankName` string(64)? · `AccountHolder` string(128)? · `IsDefault` bool | IBAN normalised (upper, no spaces) and checksum-validated (ISO 7064 mod 97); **unique per relation**; **one default per relation** (unique partial index) |
| **RelationCategoryMembership** | `RelationId`, `RelationCategoryId` FK → RelationCategories (cascade) | composite key (§3.3) |
| **RelationGeographicalRegion** | `RelationId`, `GeographicalRegionId` FK → GeographicalRegions (cascade) | composite key; the explicit membership next to the postcode match (assessment §2 row 1 "relation membership") |
| **RelationOrganizationUnit** | `RelationId`, `OrganizationUnitId` (Platform id, no constraint) | composite key; which organisation units may use the relation; empty = all (as OpenTMS) |

**Delete:** soft delete on the base (§3.12) plus the in-service check of design 41: a relation that is
the employer of a driver, or that other modules reference once they exist, is refused with
`OpenTms.MasterData:InUse`. Until the Orders run, archiving is the normal way to retire a relation.

### Driver

Its own aggregate (§3.1 CHANGE: "not a Relation in a category").

| Field | Type | Rule |
|---|---|---|
| `Code` | string(32), required | employee or driver number, **unique per tenant** |
| `FirstName`, `MiddleName`?, `LastName` | string(64/32/64) | |
| `Initials` | string(16)? | |
| `Phone`, `Mobile`, `Email` | as Relation | |
| `PreferredCulture` | string(10)? | the driver app's language |
| `EmployerRelationId` | Guid? FK → Relations (restrict) | the carrier or the own company the driver drives for; the relation must carry the Carrier or OwnCompany role |
| `UserId` | Guid? (Platform identity user, no constraint) | the driver app login; **unique per tenant when set**; existence checked through ABP's `IIdentityUserLookupAppService` (Platform published surface) |
| `IsArchived` | bool | |
| `Description` | string(2500)? | |

Child **DriverQualification** (owned, cascade): `Kind` enum `DriverQualificationKind { DrivingLicenceB,
DrivingLicenceC, DrivingLicenceCE, Code95, AdrCertificate, TachographCard, ForkliftLicence, Other }`,
`Number` string(64)?, `ValidFrom` date?, `ValidUntil` date?, `Description` string(256)?. Planning will
refuse an ADR trip without a valid ADR certificate; that rule is Planning's, the fact is here.

**Device binding** (§3.15) is not on the driver: the Integrations run binds a vendor device to a driver
or vehicle id through one workflow with a unique (tenant, vendor, deviceId).

### The relation matching service (§3.15)

`IRelationMatcher` in `Application.Contracts` (the published surface other modules and the intake
agent call; a synchronous call into Master Data that the calling module's design names, module rule
3), implemented in `Application`:

```
RelationMatchRequest { Code?, VatNumber?, ChamberOfCommerceNumber?, Email?, Name?, PostalCode?, CountryId?, Roles? }
                     (Email is accepted for the contact-person rule of a later run; no rule of this run reads it)
RelationMatchResult  { Matches: [{ RelationId, Name, Code, Kind: Exact | NameAndPostalCode | NameOnly, Score 0–100 }] }
```

Order of evaluation, first non-empty result wins: exact code; exact VAT number; exact chamber-of-
commerce number; normalised name (contained in the search key) **and** normalised postcode (any address) in the country; normalised
name alone, at most ten candidates whose search key *contains* the normalised name, shortest search key
first (the closest name; a score of 40 to 60 tells the caller how much of the key the name covers).
Archived relations are excluded;
`Roles` narrows to relations carrying one of the roles. The matcher never creates anything: an import
either uses a match or hands the party to the intake flow as a *proposal* (design of the Orders run).

**Search index.** `SearchKey` gets a B-tree index for prefix search and a **trigram GIN index**
(`pg_trgm`) for contains, because 20,000 relations with "contains" on name is the hottest lookup
(the index serves `LIKE '%…%'`; no similarity operator is used, so no extra EF package is needed). The migration runs `CREATE EXTENSION IF NOT EXISTS pg_trgm` in every database
(Cloud SQL supports it); see Assumptions.

### Feature flag and permissions

- Feature `MasterData.Relations` (group `MasterData`, default `false`, per tenant) on every new
  service; the first-run flag stays separate so the lists of run 1 can be live before this one.
- Permissions, both sides: `MasterData.Relations` (+ `.Create`, `.Update`, `.Delete`) and
  `MasterData.Drivers` (+ `.Create`, `.Update`, `.Delete`, `.Anonymise`). The matcher is `MasterData.Relations`.

### Deliberate differences from OpenTMS

| Area | Dropped or changed | Reason |
|---|---|---|
| Relation | `SearchName` | one indexed `SearchKey` over name, full name and code (§3.1); `FullName` is back on the PO's request (2026-10-12), optional, 128 characters |
| Relation | `IsMe`, categories with system flags | `Roles` flags with the own-company partial index (§3.1) |
| Relation | `IsBlocked` | kept as a flag with a `BlockReason` enum on the PO's request (2026-10-12, the old "Is geblokkeerd"); what a block stops is decided by the Financial and Orders runs (§3.1) |
| Relation | `CurrencyId`, `VatId`, GL accounts, invoicing options, 48 charge-by and price-list columns, self-billing, purchase orders, incoming invoices | `RelationFinancialSettings` in Financial (§2, §3.1) |
| Relation | portal users, auto-accept, ETA notifications, forwarding, questionnaires, time slots, default waypoints, access codes, door keys, vehicle types able to access | Orders and Planning settings per RelationId (§2) |
| Relation | `SurchargesAndDiscounts`, `AvailableProducts`, `AvailableVehicleTypes` and the other delimited lists | join tables where the owning module needs them (§3.3) |
| Relation | `TransportCompanyId`, `RelationGroupId`, `CarrierCode` | the serving carrier is a planning setting; RelationGroup is dropped by the PO (§2.1); a partner's carrier code is Integrations mapping |
| Relation | `Iban`, `BankName`, `Bic` columns | `RelationBankAccount` child with IBAN validation (§3.1) |
| Relation | `FaxNumber`, `EmergencyNumber`, `InstantMessagerAddress`, `SkypeAddress` | not used; an emergency number is a depot contact person with a role |
| RelationAddress | `Number` int + `NumberSuffix`, `AddressLine1` | `HouseNumber` string as typed, `Street`; `TimeZoneId` as IANA (§3.1) |
| RelationAddress | several addresses of one type | one per address type per relation, found by kind (§3.4: "which address of the relation" is explicit) |
| RelationContactPerson | no role | `Role` enum (§3.1) |
| Driver | a Relation in the driver category | own aggregate with employer, user account and qualifications (§3.1) |
| Driver | device ids on the relation (`FleetManagementSystemDeviceId`) | Integrations binding workflow (§3.15) |
| Matching | three `FindOrCreate` implementations, one hard-coded to NL postcodes | one matcher, never creates, postcode normalised per country (§3.15) |

## API contract

Conventional controllers, route root `master-data`, the design-41 shape (`<E>ListRequestDto` flat
query, `GridResponse<<E>RowDto>`, detail DTO, create and update DTOs with concurrency stamp, lookup,
bulk delete). OpenAPI and the typed client are regenerated.

| Service | Route root | Extra operations |
|---|---|---|
| `RelationAppService` | `/api/master-data/relation` | `GET /lookup?term=&take=&roles=` (server search over `SearchKey`, pages of 25 for `EntityLookup`: `skip`, `take`), `GET /{id}/addresses`, `POST /match` (`RelationMatchRequest` → `RelationMatchResult`), `POST /{id}/archive`, `POST /{id}/unarchive` |
| `DriverAppService` | `/api/master-data/driver` | `GET /lookup?term=&take=&employerRelationId=`, `GET /user-lookup-page?term=&skip=&take=` (the tenant's users for the account picker, pages of 25, `Drivers.Update`), `POST /{id}/archive`, `POST /{id}/unarchive`, `POST /{id}/anonymise` (GDPR art. 17, `Drivers.Anonymise`) |

Request and DTO shapes:

- `RelationListRequestDto`: `roles?` (flags), `includeArchived` (default false), `categoryId?`,
  `organizationUnitId?`; `wildcardSearch` over `SearchKey` (name, code), VAT number, chamber-of-commerce
  number, city of any address. `sortBy` whitelist: `name`, `code`, `city` (the visiting address),
  `vatNumber`, `chamberOfCommerceNumber`, `phone`, `email`, `isArchived`.
- `RelationRowDto`: `id, code, name, roles, isArchived, vatNumber, chamberOfCommerceNumber, phone,
  email, city, countryCode` (the visiting address by kind, else the first), `categoryNames`.
- `RelationDto`: the fields above plus `description, preferredCulture, website, mobile`, `addresses[]`
  (`id, addressTypeId, addressTypeName, countryId, countryCode, street, houseNumber, addressLine2,
  postalCode, city, latitude, longitude, timeZoneId`), `contactPersons[]`, `bankAccounts[]`,
  `categoryIds[]`, `geographicalRegionIds[]`, `organizationUnitIds[]`, audit and concurrency stamp.
- `RelationCreateDto`/`RelationUpdateDto`: the writable fields and the three child collections with
  `id?` per row (replace semantics), the three id lists.
- `DriverRowDto`: `id, code, firstName, lastName, employerRelationId, employerName, mobile, email,
  isArchived, hasUser, qualificationsExpiring` (count expiring within 90 days).
- `DriverDto`, `DriverCreateDto`, `DriverUpdateDto`: the fields and `qualifications[]`.
- `MasterDataLookupDto` for every lookup, as in run 1, plus `RelationLookupDto { id, code, name, city,
  countryCode, roles }` for the richer option rows of UI-LOOK-003.

Errors (mapped to 409 and 400 in `OpenTms.HttpApi` as in run 1): `DuplicateKey` (code, VAT, CoC, IBAN,
address type, user), `InUse`, `OwnCompanyAlreadyExists`, `RolesRequired`, `EmployerRoleRequired`,
`InvalidIban`, `InvalidVatNumber`, `InvalidTimeZone`, `UserNotFound`, `AnonymisedRecordIsReadOnly`.

## Migration strategy

One additive migration `MasterDataRelationsAndDrivers` in the Master Data context, after design 41's:
tables `Relations`, `RelationAddresses`, `RelationContactPersons`, `RelationBankAccounts`,
`RelationCategoryMemberships`, `RelationGeographicalRegions`, `RelationOrganizationUnits`, `Drivers`,
`DriverQualifications`; the `pg_trgm` extension; indexes in the same migration: the per-tenant unique
indexes (`NULLS NOT DISTINCT`, soft-delete filter) on relation code, VAT number, CoC number, driver
code and driver user; the own-company and default-bank-account partial indexes; unique
`(RelationId, AddressTypeId)` and `(RelationId, Iban)`; B-tree on `(TenantId, SearchKey)` and the
trigram GIN on `SearchKey`; `(TenantId, Roles)`; `(EmployerRelationId)`; `(CountryId)` and
`(TenantId, PostalCode)` on addresses for the matcher. Expand only; no contract step; `Down` drops the
tables (the extension stays).

## UI design

**Navigation.** Two new views in the Master data app: group *Relations* gets **Relations**
(`/master-data/relations`, first in the group) and group *Fleet* gets **Drivers**
(`/master-data/drivers`). Both behind the feature; the panels of design 41 apply.

**Relations list** (shared list view): default columns Code, Name, Roles (localized, comma list),
City, Country, VAT number, Phone, E-mail; hidden: CoC number, Categories, Archived. Filters in the
toolbar: Role (`Select`), Category (`ReferenceCombobox`), Show archived. Row actions: Edit, Archive /
Unarchive, Delete. Command bar: New relation, Delete selected.

**Relation editor** (`EntityDialog size="lg"`, UI-DLG-003: a header block plus lines), tabs:

1. **General** (PO 2026-10-12): one *Relation* block with row 1 Roles `MultiSelect` + the Archived
   and Blocked switches + the **Block reason** `Select`, shown only while blocked (enum
   `RelationBlockReason`: payment arrears, credit limit exceeded, insolvency, dispute, other; required
   while blocked, None otherwise; a tenant-maintained list is for the Financial run to decide), row 2
   Categories `MultiSelect` + Geographical regions `MultiSelect`; row 3 the *Name* block (Code + Name,
   then Full name) stacked above the *Contact information* block (Phone, Mobile, E-mail, Website) on the
   left, next to the *Addresses* block: **All addresses equal** with the default address inline (Country
   `ReferenceCombobox`, Postal code, City, Street, House number, Address line 2, Time zone prefilled
   from the country, Longitude, Latitude) or, when off, a hint that the addresses live on the
   Addresses tab, then a *Map* / *Remarks* sub-tab strip filling the block: the map is an
   OpenStreetMap embed of the entered coordinates (no geocoder until Planning; the provider is a
   reversible choice, one URL and no package), Remarks is the Description.
2. **Administration**: *Legal* (VAT number, Chamber of commerce), Preferred language, Organisation
   units `MultiSelect` (DEVIATION(UI-LOOK-001): a relation belongs to several units and `TreeSelect` is
   single-valued; a multi-valued tree picker is a library gap for the component map).
3. **Financial**: the bank accounts `LinesGrid` (IBAN, BIC, bank, holder, default; exactly one default);
   the Financial run adds its relation settings on this tab.
4. **Addresses**: the per-type addresses, which override the default address for their type:
   `LinesGrid` (type, street, house number, postal code, city, country) with Add address
   opening `AddressDialog` (`EntityDialog size="md"`, editor variant, on top; eleven fields in two
   columns, above the `sm` tier of UI-DLG-003): the full address form with Country `ReferenceCombobox`,
   Address type `Select`, Time zone `ReferenceCombobox` prefilled from the country (and following a
   changed country while the user has not chosen a zone). One address per type: the type picker hides types already used.
3. **Contact persons**: `LinesGrid` (name, role, e-mail, phone) with Add contact person opening
   `ContactPersonDialog` (`md`, nine fields in two columns).
4. **Bank accounts**: `LinesGrid` edited inline (IBAN, BIC, bank, holder, default) as the ranges of
   design 41; IBAN checked on submit.
5. Record identification.

Archive and Unarchive are ribbon commands (UI-ACT-002, reversible, confirmed). Badges after the title:
the roles and *Archived* (UI-DLG-012).

**Drivers list**: Code, Name, Employer, Mobile, E-mail, Qualifications expiring (`NumberCell`),
Archived (hidden). Filter: Employer (`EntityLookup`), Show archived.

**Driver editor** (`md`): General with sections *Driver* (Code, First name, Middle name, Last name,
Initials, Archived), *Contact* (Phone, Mobile, E-mail, Preferred language), *Employment* (Employer
`EntityLookup` over relations with the Carrier or OwnCompany role, with "Add new relation…" per
UI-LOOK-004; User account `EntityLookup` over ABP users of the tenant); tab **Qualifications**:
`LinesGrid` inline (kind `Select`, number, valid from, valid until with the `DatePicker` cell editor);
Record identification. Anonymise is a
ribbon command behind `Drivers.Anonymise`, destructive, confirmed, that replaces the person fields by
"Anonymised" and clears contact details, user link and qualifications; the record then opens read-only.

**Library.** `EntityLookup` (GAP-01 decided as option (a): the Combobox of run 1 with a TanStack
`useInfiniteQuery` list and an IntersectionObserver sentinel; 250 ms debounce, 2 characters, pages of
25, infinite scroll, `onCreateNew`, `onEdit`); `DatePicker` (GAP-03 decided as option (a), see
Assumptions); `CheckboxGroup` for the roles (small, `components/ui`); `TreeSelect` exists. The
component map's §4 rows for GAP-01 and GAP-03 are closed by these decisions (canonical copy in
`dev-department/conventions/ui/`, the PO's edit). No new shell change.

**Optimistic updates:** none; archive and unarchive refresh the grid after the call.

**Manual:** `docs/manual/relaties.md` and `docs/manual/chauffeurs.md` (Dutch).

## Test risk analysis

| Part | Class | Mandatory coverage |
|---|---|---|
| Tenant isolation of relations, children, drivers, the lookup and the matcher | **Critical** | PostgreSQL negative tests per operation; a matcher request from tenant B never sees tenant A |
| Authorization and the feature flag (covered by the architecture test of run 1 for attributes; definitions and the flag in tests) | **Critical** | definition tests, flag-off test |
| Migration incl. `pg_trgm` and every index | **Critical** | PostgreSQL migration test as run 1 |
| Uniqueness: code, VAT, CoC, driver code, user, IBAN per relation, address per type; the own-company and default-account partial indexes | **Critical** | PostgreSQL index-violation tests through the repository, friendly 409 through the service |
| Personal data: anonymise leaves no name, contact or qualification; the record stays referable by id; art. 15 export selector returns a driver and contact persons by e-mail | **Critical** | service tests |
| Matcher: order of rules, normalisation (postcode per country, diacritics), archived excluded, role filter, at most ten name candidates, shortest first | **High** | PostgreSQL (trigram) tests, table-driven |
| CRUD, children replace semantics, role rules (at least one, employer role), IBAN and VAT shape, time-zone default from country | **High** | SQLite service tests |
| Volume ×5: 100,000 relations with 150,000 addresses: list, lookup by name fragment, matcher, driver list | **High** | PostgreSQL volume test with cold and warm runs, index use |
| Frontend: two lists, the relation editor with its three child dialogs, the driver editor, `EntityLookup`, `CheckboxGroup` | **Medium** | vitest |
| Localization parity | **Low** | key test |

Idempotency: anonymise twice is one effect; bulk delete twice deletes zero the second time. No job, no
webhook.

## Flag & rollout plan

`MasterData.Relations`, default off, health tenant → friendly tenants → all. No existing data (new
tables). Rollback: flag off, tables stay. The OpenTMS data migration is its own brief (assessment §7):
relations and drivers from OpenTMS need a mapping, not a copy.

## Cost & SLO impact

- Cloud SQL: nine tables; at 20,000 relations about 25 MB per large tenant with indexes (the trigram
  index is the largest part, about 10 MB); 1,000 tenants mostly far smaller. No jobs, no external calls,
  no egress. Min instances unchanged; per-tenant margin unaffected.
- Budgets: interactive reads (list, lookup, matcher) p95 < 300 ms; the lookup is index-only on
  `SearchKey`; the matcher is three index probes plus one trigram-indexed contains scan with a limit. Mutations
  p95 < 500 ms; the largest write is a relation with ten addresses, twenty contacts and three accounts in
  one transaction.

## Assumptions

1. **GAP-01 is decided as option (a) of the component map:** `EntityLookup` on the shadcn Combobox
   with a TanStack `useInfiniteQuery` list and an IntersectionObserver sentinel. The relation picker
   in the driver editor, the employer filter and every later order screen need it; this design builds
   it. If the PO prefers (b) or (c), the build changes only inside `components/ui/entity-lookup.tsx`.
2. **GAP-03 is decided (PO, gate 1) as option (a) of the component map:** `DatePicker` in
   `components/ui/date-picker.tsx` on the shadcn `Calendar` (react-day-picker) in a `Popover`, with a
   masked `Input` for typing in the culture's date format (UI-FMT-004: `DD-MM-YYYY`, week starts Monday,
   typing validated on blur, the calendar a convenience). Date-only values travel as `YYYY-MM-DD`. The
   qualification dates use it, inline in the lines grid through an AG Grid cell editor that hosts the
   same component. `DateTimePicker` and `TimeInput` follow the same pattern when a feature needs them.
3. `pg_trgm` may be created by the migration in every database (Cloud SQL and local PostgreSQL ship
   it; the DbMigrator connects as the migration user).
4. Relation code is user-entered and unique until the Platform number-sequence service (§3.9) exists;
   adding auto-numbering later is additive.
5. One address per address type per relation. A second visiting address is a second relation (a
   location), as the assessment's depot role intends.
6. The own-company relation is created by the tenant administrator through the normal editor (role
   checkbox); provisioning does not seed it.
7. Driver's `UserId` and the organisation-unit ids are Platform ids without constraint; existence is
   checked on write, not maintained afterwards (a deleted user leaves a dangling id that the driver
   editor shows as "user not found").
8. Art. 15 export: this design delivers the per-entity selector (`IPersonalDataSource` in
   `Application.Contracts`: given an e-mail or user id, the matching contact persons and drivers), not
   the Platform export job that assembles the file across modules; that job is a Platform design.
9. Positions and device data stay out (AO-12 open).
10. Volumes are the PBI's; the test engineer multiplies by five.
11. File number 43 stands in for the issue number.

## Security quickscan

- **Permissions:** eight new definitions plus `Drivers.Anonymise`, both sides; every public method
  attributed (enforced by the run-1 architecture test); feature flag as second gate.
- **Input validation:** lengths and shapes (IBAN mod 97, VAT prefix shape, e-mail, IANA zone, culture
  from the configured languages), referenced ids checked in the tenant, child collections bounded
  (50 addresses, 100 contact persons, 10 bank accounts, 20 qualifications per record), lookup pages
  bounded at 25, matcher candidates at 10.
- **Attack surface:** two conventional controllers behind authentication, permissions and the feature;
  the matcher is a read; no anonymous endpoint, no upload, no raw SQL (the contains query is a plain LINQ
  `Contains`, which Npgsql translates to `LIKE` and the trigram index serves), no outbound call.
- **Tenant isolation:** every entity `IMultiTenant`; no filter bypass anywhere.
- **Logging:** ids and counts only; never names, e-mail addresses, IBANs or phone numbers.
- **Personal data (GDPR):** `RelationContactPerson` and `Driver` are personal data. **Art. 15:** the
  module publishes `IPersonalDataSource.FindAsync(email | userId)` returning the records as DTOs; the
  Platform export job (later) calls every module's source. **Art. 17:** `DriverAppService.AnonymiseAsync`
  replaces names with "Anonymised", clears initials, phone, mobile, e-mail, user link, description and
  qualifications, keeps the id and code so trip history stays consistent, and marks the record
  read-only (`AnonymisedRecordIsReadOnly` on later writes); a contact person is hard-deleted on request
  through the relation editor (no history references it). Bank accounts of a sole trader are company
  data for the ledger and stay. Retention of the audit log entries that carry old values is the
  Platform logbook's concern and is named here so the Platform design covers it.
