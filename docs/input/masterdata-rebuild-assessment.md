# Master data rebuild: assessment of the current OpenTMS implementation and PO decisions

Date: 2026-10-11. Scope: every entity shown under *Stamgegevens* in OpenTMS, except transport order templates and price lists. Purpose: input for the tasks that build the master data entities in opentms-next, so that known bad decisions are not cloned. All paths are relative to `aspnet-core/src/` unless stated otherwise.

Section 3 records the PO's decision per finding (2026-10-11). Items marked **KEEP** are deliberate and must be written into the design doc as such; items marked **CHANGE** are requirements for the rebuild.

## 1. The entities in scope (36)

| Group (OpenTMS menu) | Entities |
|---|---|
| Relations | Relation (+ RelationAddress, RelationContactPerson, RelationCategory, bank details), RelationGroup, Category, AddressType, ProductRelationCode |
| Fleet | Vehicle, VehicleType, FleetManagementSystemDevice, Capability |
| Invoicing | DiscountAndSurcharge, Vat, Currency (+ CurrencyDayRate), IncomingInvoiceType, AdditionalService, InvoiceReminderProfile (+ Scheme), InvoiceType, PurchaseOrderType |
| Transport | Product, TransportUnit, WaypointType, TimeSlot, TemperatureRange, TransportOrderType, TransportFileType |
| Other | Country, GeographicalRegion, OutboundEMailMessageTemplate, ReportDefinition, BinaryObjectType (+ Transformation), Questionnaire (TransportFileQuestionnaire), CredentialStore, ConsignmentNoteFixedDescription, ExchangeGateway |

Excluded as requested: TransportOrderTemplate, PriceList (and the price-list adjusting wizard). The discounts/surcharges bulk wizard and the migrate/merge wizards are discussed because they exist only to repair modelling problems.

## 2. Placement in opentms-next and task split (PO decision: one task per domain)

opentms-next's PO-owned domain map defines Master Data as *relations, addresses, vehicles, drivers*. Module boundary rule 1 gives every module its own PostgreSQL schema, DbContext and migrations history. The PO agreed to the domain split below and to **one task per domain**, each with its own design doc (gate 1) and its own migration. **PO decision 2026-10-11 (revised): the first run is the set of leaf entities listed in section 2.1, which spans four modules; everything else follows later.** The first run ships with an initial seed (section 2.2).

| Task order | opentms-next module | Entities | Why |
|---|---|---|---|
| 1 | **Master Data** (in scope now) | Relation (one entity, all roles), Address, ContactPerson, BankAccount, AddressType, Category, Driver (new, separate), Vehicle, VehicleType, Capability, Product, TransportUnit (packaging type only), TemperatureRange, Country, Currency (code list), ProductRelationCode (customer article code; all its references are Master Data, so it gets real FKs here), GeographicalRegion (postcode ranges per country plus relation membership; the default via-waypoint stays Planning configuration) | Shared reference data used by several domains; other domains need RelationId/CurrencyId first |
| 2 | **Financial** | Vat, DiscountAndSurcharge, AdditionalService, InvoiceType, IncomingInvoiceType, PurchaseOrderType, InvoiceReminderProfile, CurrencyDayRate, TransportUnit balances/transactions, **RelationFinancialSettings** (per RelationId) | Ledger accounts, numbering, pricing rules, dunning; consumed only by invoicing, pricing and payables |
| 3 | **Orders** | TransportOrderType, WaypointType, TimeSlot, **RelationOrderSettings** | Numbering, intake mailbox, default stops, required fields, stop-activity semantics, delivery-window templates |
| 4 | **Planning & Execution** | TransportFileType, Questionnaire, default via-waypoints per region, **RelationPlanningSettings** (dock, time slots) | Route optimisation, stop creation, FMS sync, planboard grouping, driver-app forms |
| 5 | **Platform** | OutboundEMailMessageTemplate, ReportDefinition, BinaryObjectType, ConsignmentNoteFixedDescription, number-sequence service, TransportProfile | Mail, document generation, document categories, cross-module services |
| 6 | **Integrations** | CredentialStore, ExchangeGateway, FleetManagementSystemDevice (vendor binding), inbound mailboxes | External systems and secrets |

### 2.1 First run (PO decision 2026-10-11): leaf entities, grouped by owning module

PO names are used from here on: Capability becomes **TransportCharacteristics**, Category becomes **RelationCategory**, TemperatureRange becomes **Temperature**, Vat becomes **VatPercentage**, IncomingInvoiceType becomes **InboundInvoiceType**, InvoiceType becomes **OutboundInvoiceType**, DiscountAndSurcharge becomes **DiscountOrSurcharge**, InvoiceReminderProfile becomes **OutboundInvoiceReminderProfile**, OutboundEMailMessageTemplate becomes **OutboundEmailTemplate**. RelationGroup is dropped.

| Module (own schema, own migration) | Entities in the first run | References they carry (from the OpenTMS entity classes) |
|---|---|---|
| **Master Data** | Currency, AddressType, RelationCategory, TransportCharacteristics, Temperature, Product, TransportUnit, VehicleType, GeographicalRegion, **Country** (added, see note 1) | VehicleType → TransportCharacteristics; TransportUnit → VehicleType (load factors); GeographicalRegion → Country; Country → Currency and VatPercentage (ids only, the latter cross-module); Product and Temperature and TransportUnit → themselves ("do not combine with") |
| **Financial** | AdditionalService, VatPercentage, InboundInvoiceType, OutboundInvoiceType, DiscountOrSurcharge, OutboundInvoiceReminderProfile | DiscountOrSurcharge → GeographicalRegion (cross-module, id only); OutboundInvoiceReminderProfile scheme → OutboundEmailTemplate (cross-module, id only); InboundInvoiceType → Relation (see note 2) |
| **Orders** | TransportOrderType, TimeSlot, **WaypointType** (added) | TransportOrderType → WaypointType via `DefaultWaypoints` (child table, both in this run); WaypointType → itself (`SuccessiveWaypointTypeId`), RelationCategory (`WaypointTypeCategory`, cross-module id), FMS codes (child table); its `StationingRelationId` and the two questionnaire ids wait for the Relation and Planning runs (note 3); organisation unit (Platform id) |
| **Platform** | ConsignmentNoteFixedDescription, OutboundEmailTemplate | none |

Notes:

1. GeographicalRegion cannot exist without Country (`GeographicalRegionPostalCodeRange.CountryId`), and Country's own references (Currency, VatPercentage) are both in this run, so Country is added to the first run.
2. `InboundInvoiceType.RelationId` is only read for display in OpenTMS; leave it out of the first run and add it in the Relation run if it is still wanted.
3. WaypointType is in the first run (PO decision) without its non-leaf fields: `StationingRelationId` is added in the Relation run, `QuestionnaireId`/`TransportFileQuestionnaireId` in the Planning run. With WaypointType present, `DefaultWaypoints` on TransportOrderType becomes a child table now, and the transaction-time child tables of Product and TransportUnit (`ProductTransactionTime`, `TransportUnitTransactionTime`, both → WaypointType, cross-module ids) can be built now as well.
4. Cross-module references (DiscountOrSurcharge → GeographicalRegion, reminder scheme → OutboundEmailTemplate, Country → VatPercentage) are ids without a database constraint, validated through the owning module's `Application.Contracts`. Build Master Data and Platform first, then Financial and Orders, or build all four in parallel and validate ids once both sides exist.

Execution: one brief per module in the first run (four briefs, four design docs, four migrations), each additive so the later runs stack without a contract step. Later runs: TransportFileType; Relation (+ children) and Driver; Vehicle and ProductRelationCode; then the remaining entities of section 2.

### 2.1a Start: the ten Master Data entities only (PO decision 2026-10-11)

The very first brief builds only the Master Data row of the table above: Currency, Country, AddressType, RelationCategory, TransportCharacteristics, Temperature, Product, TransportUnit, VehicleType, GeographicalRegion. Financial, Orders and Platform follow as separate briefs.

Seed requirements (PO):

| Entity | Seed | Fields to seed |
|---|---|---|
| Currency | every ISO 4217 currency, with `IsActive` true for EUR only | ISO 4217 code (unique per tenant), numeric code, minor units, symbol, `LocalizedName` (nl/en), `IsActive`; EUR is the tenant default. Pickers show active currencies only; the tenant activates others |
| Country | every ISO 3166-1 country | alpha-2 (unique per tenant), alpha-3, numeric, localized name (nl/en), default currency as a real foreign key to Currency (every ISO currency exists), primary IANA timezone |
| AddressType | visiting address, postal address | `Kind` enum (Visiting, Postal) so code never matches on name; localized name (nl/en); tenant may add more kinds-less types later |

PO decision 2026-10-11 on Country → Currency: all ISO 4217 currencies are seeded with `IsActive` only for EUR, so Country keeps a real foreign key to Currency. Integrations still match on the ISO code (section 3.7).

**Localized names.** ABP open source has `IStringLocalizer` with JSON resources for static texts and the request culture middleware (`Accept-Language`, nl/en configured in `OpenTmsDomainSharedModule`). Runtime-editable texts in the database are the commercial Language Management module, which is not in the project. ABP has no built-in localized entity property. Using localization keys as names works for seeded rows only, not for tenant-created types, so:

- Platform `Domain.Shared` gets one reusable value object `LocalizedName` (a culture → text map, stored as JSONB per AD-06, with a `Resolve(culture)` that falls back to the default language). The two existing JSONB usages set the precedent.
- AddressType, Country and Currency use it for `Name` (PO approved 2026-10-11). Tenant-created rows fill the same map.
- Application services return the resolved name for `CultureInfo.CurrentUICulture` plus the full map for editors; grids and pickers sort on the resolved name. Editors show one field per configured language.
- Seed data lives as embedded JSON in the module's `Domain.Shared` and is applied by an ABP data-seed contributor from the DbMigrator, idempotently (upsert on the natural key).

### 2.2 Initial seed for the first run

opentms-next allows seeding only in the DbMigrator (ABP data-seed contributors, run per tenant database). Each first-run module delivers a seed contributor for its own entities; for Master Data: countries (ISO alpha-2/alpha-3/numeric, default currency, timezone as IANA id), currencies, address types (visiting, postal, loading with their system kind), default categories, vehicle types, packaging types (transport units) and temperatures; Financial seeds VAT percentages (NL 21/9/0, reverse charge) and a default outbound invoice type; Orders seeds a default order type and time slots; Platform seeds the default email templates per language. Do not copy from the OpenTMS `DefaultTenantDataBuilder`: its pallet factors are wrong (Europallet EPQ 0.1, one-way pallet 1, "full load" seeded as a unit, `TenantDataBuilders/Default/DefaultTenantDataBuilder.cs:830-841`) and the Dutch regions are 4,700 single four-digit postcodes instead of ranges (`:1213-1240`). Countries use Windows timezone ids today (`TimeZoneCalculator.cs:12-28`); seed IANA ids.

Rules every task inherits:

- Cross-module references are ids without a database constraint (as AO-11 already states). The referencing module validates existence through the owning module's published contract (`Application.Contracts`), never through its DbContext.
- Relation-specific settings live in the module that uses them, keyed by RelationId. This keeps "Relation is everything" (PO decision) while Financial, Orders and Planning keep real foreign keys to their own entities and the Relation table stays narrow.
- The host-level requirement in section 4 is parked (PO decision 2026-10-11); design the tenant column as nullable so it can be added later without a contract migration.
- Every task follows gate 1: PBI (`dev-department/templates/pbi.md`), design doc with all nine sections citing AD-06, AD-07, AD-08, AD-16 and listing AO-12 under assumptions where vehicles or devices are touched, then "build exactly the approved design".

## 3. Findings and PO decisions

### 3.1 Relation modelling: KEEP one Relation, CHANGE the mechanics around it

OpenTMS: `Relation` is customer, carrier, creditor, location, driver and the tenant itself (`IsMe`); roles are derived from user-editable categories with system flags (`BusinessEntities/Category/Category.cs:35-60`); `Relation.cs` has about 160 properties.

PO decision: Relation stays one entity covering all roles. Driver becomes its own entity.

Requirements that follow:

- Roles are an explicit typed set on Relation (customer, carrier, creditor, depot, own company), not derived from categories. Categories stay free classification. Exactly one relation per tenant carries the own-company role (unique partial index).
- Driver is its own aggregate: person data, optional link to a Relation (employer/carrier), optional link to a user account, device binding. Not a Relation in a category.
- Name stored once (OpenTMS stores `Name`, `FullName`, `SearchName`). Search uses an index, not three columns.
- Bank accounts as a child table; contact persons with a role; address with structured fields and an IANA timezone id.
- Per-domain settings of a relation live in that domain (section 2), not as columns on Relation.
- `IsBlocked` exists in the OpenTMS UI but nothing enforces it (`Relation.cs:21`); define what it means or drop it.
- No merge wizard in v1. Duplicates are prevented by uniqueness (section 3.2) and one matching service (section 3.15).

### 3.2 Referential integrity: CHANGE

OpenTMS: the whole `FiLogicDbContext` configures 5 foreign-key relationships; no unique indexes on relation name, debtor number, VAT number, chamber-of-commerce number, licence plate, ISO codes or capability code. Delete integrity is hand-written counts with three different lists of "who references a relation". Merge is a reflective `INFORMATION_SCHEMA` scan with dynamic `UPDATE` and no transaction that skips every `tblRelation*`/`tblVehicle*` child table (`EntityFrameworkCore/Repositories/MergeRepositoryBase.cs:83`).

Requirements: real foreign keys inside every module; unique constraints per tenant on natural keys (debtor number, VAT number, CoC number, licence plate, ISO codes, codes of all code-list entities); delete behaviour declared in the model (restrict or cascade), not in app-service counts. Unique indexes must include TenantId and handle host rows (section 4).

### 3.3 Lists as delimited strings: CHANGE

OpenTMS: `Relation.SurchargesAndDiscounts` (`|`), `VehicleTypesAbleToAccess` (`;`), portal user lists, `Vehicle.DefaultDriverIdentifiers`, `Product.DoNotCombineWith`, `TransportOrderType.DefaultWaypoints`, `ConsignmentNoteProfile.Field6Option`; three different parsers; some lists also exist as join tables in parallel (`RelationDiscountAndSurcharge`).

Requirement: join tables with unique constraints, or typed value objects in JSONB where AD-06 applies. Never both.

### 3.4 Orders reference master data by id, no snapshots: KEEP, with versioning rule

OpenTMS: stops, legs and waypoints hold a `RelationId` without an address type; invoices hold `VatId` without percentage; D&S values are read live at calculation time.

PO decision: references by id stay; master data that financial documents depend on becomes non-editable.

Requirements that follow:

- AD-07 and AD-08 already store calculated amounts on rate lines, so money history is safe by architecture. The design doc must say so explicitly.
- "Non-editable" is defined per field, not per entity. Descriptive fields (phone, typo in a street name, contact person) stay editable. Value-bearing fields (VAT percentage, D&S value, price) become validity rows: a change creates a new row with `ValidFrom`, the old row gets `ValidTill`. Consumers reference the row that was valid at the time.
- Stop references carry the address type (visiting, loading) so "which address of the relation" is explicit.
- Generated documents (CMR, invoice PDF) are stored per AD-09, so the historical document itself never changes.

### 3.5 `double` for money and measures: KEEP, name the unit

PO decision: keep `double`, no separate unit model.

Requirements that follow: the unit is part of the column and property name (`WeightKg`, `LengthCm`, `VolumeM3`, `TemperatureCelsius`, `LoadingMeters`), so nothing guesses by magnitude as `RouteVehicleSpecMapper.cs:40` does today. Recommendation to reconsider once at design time: `decimal` for money columns only (PostgreSQL `numeric`, no cost) to avoid cent-rounding differences in totals. If `double` is kept for money, the design doc records it as a deliberate choice.

### 3.6 Tenant fallback `TenantId ?? 1`: CHANGE to ABP's native host context

OpenTMS: `AbpSession.TenantId ?? 1` appears 324 times in `FiLogic.Application`; tenant 1 doubles as host.

Better solution, which also serves the host-level requirement: in ABP the host is `TenantId = null`. Entities implement `IMultiTenant` with a nullable `TenantId`; the data filter returns host rows in host context and tenant rows in tenant context, with no fallback anywhere. Crossing contexts (onboarding copies host rows into a tenant) is done explicitly with `ICurrentTenant.Change(tenantId)`, visible at the call site and allowed by constitution rule 1. No `?? 1` and no `IDataFilter.Disable` in master data code.

### 3.7 Global reference data copied per tenant: KEEP, with a stable key

OpenTMS: Country (about 250 rows), Currency and VAT are seeded into every tenant with fresh GUIDs; `Iso3166_2` actually holds alpha-2 codes and is not unique.

PO decision: copies per tenant stay (consistent with database-per-tenant and with transport profiles).

Requirements that follow: ISO code is the natural key, unique per tenant, correctly named (`IsoAlpha2`, `IsoAlpha3`, `IsoNumeric`); integrations match on ISO code, never on id. When copying from a host profile, keep the same id (possible with database-per-tenant) or store a `SourceId`, so profile updates can be diffed later.

### 3.8 Defaults in two places: CHANGE

OpenTMS: `IsDefault` columns on Currency, Vat, InvoiceType, PurchaseOrderType, VehicleType with no single-default constraint, next to tenant settings such as `DefaultCurrencyId`; different code paths read different ones.

Requirement: one place per default, enforced (unique partial index on `IsDefault = true` per tenant, or the setting, not both).

### 3.9 Workflow configuration on type entities: KEEP number sequences, CHANGE the rest

OpenTMS: TransportOrderType, TransportFileType, InvoiceType and PurchaseOrderType carry number sequences (copy-pasted, drifted: PurchaseOrderType lacks year/month prefix; PurchaseOrders still use a generator without locking, `PurchaseOrderNumberGenerator.cs:24-48`); required-field lists as reflection property names; default stops as a GUID string; route optimisation mode, FMS sync flags, default carrier and depots as columns.

PO decision: number sequences stay on the type entities.

Requirements that follow: one number-sequence service in Platform (the atomic `EntityNumberCounter` pattern, `EntityNumbers/EntityNumberCounterService.cs:79-90`, is sound and is the model), configured per type by prefix/format/year/month, used by all four types; unique index on every generated number. The other settings become typed configuration: required fields as an enum set, default stops as a child table, modes as enums.

### 3.10 Behaviour driven by free strings: CHANGE, proposal

OpenTMS: strategy class names on InvoiceType resolved by comparing `.Name` (`Invoice/InvoiceLineCreationServiceFactory.cs:19-26`); questionnaire processing rules as `key=PropertyName` text applied by reflection onto any freight property (`TransportFileQuestionnaireResultTransportOrderFreightUpdateBackgroundWorker.cs:225-246`); `EntityTypeName` strings with hard-coded switches; email placeholders via chained `string.Replace` in two diverging resolvers without HTML encoding (`EmailTemplateProcessingService.cs:178-200`, `StringTagReplaceHelper.cs:21-81`).

Proposal:

- Strategies and entity types become enums in the owning module's `Domain.Shared`; factories switch on the enum.
- Questionnaires get a versioned definition (questions with key, type, required, options). A processing rule maps a question key to a fixed enum of allowed targets (delivered quantity, weight, signature, remark, damage flag), executed by a domain service with an explicit switch. Answers are JSONB validated against the definition version. No reflection.
- Email templates use ABP Text Templating (Scriban, already in the framework) with per-language content. The owning module publishes a typed template model through its `Application.Contracts`; values are HTML-encoded when the model is built, so template authors cannot forget it. One placeholder system.

### 3.11 Authorisation not enforced: CHANGE

OpenTMS: fine-grained `MasterDataApp_*` permissions exist but 87 app services use bare `[AbpAuthorize()]` versus 76 with a specific permission; `GeographicalRegionAppService` lookups are `[AbpAllowAnonymous]`; two constants hold wrong keys (`AppPermissions.cs:152-153`).

Requirements: a permission group per module with read/create/update/delete per entity in `Application.Contracts/Permissions/`, `multiTenancySides: Both` (host-level requirement); every app-service method carries its permission; an architecture test in `test/OpenTms.ArchitectureTests` fails the build on any public app-service method without an authorize attribute.

### 3.12 UI concerns on the domain base class: CHANGE

OpenTMS: `Flags` colour bitmask on every row, `IncludeInGenericListView`, `VisibleInPlanboard`, `DisplayColor`. Keep audit, tenant and soft delete on the base; tags and colours as a separate per-user feature if wanted at all.

### 3.13 Generic grid pipeline: CHANGE

OpenTMS: `GetAllDtoQuerySelector` does `LIKE '%x%'` over every projected column, resolves everything through `IocManager.Instance`, mutates the shared input, enriches each page with extra queries; `vwRelation` joins the postal address while pickers use the visiting address, and its address-type subquery has no tenant filter (`vwRelation.sql:24`).

Requirement: paged, indexed queries per entity with an explicit filter contract (coding conventions already demand this); search over a defined set of columns with trigram or full-text index.

### 3.14 Mass-assignable DTOs and client-supplied keys: CHANGE

OpenTMS: edit DTOs are `[AutoMapTo]` straight onto entities; writes are upserts keyed on the client's id. Requirement: separate Create/Update DTOs, server-generated ids, explicit mapping, concurrency stamp.

### 3.15 Master data silently created by imports and telemetry: CHANGE

OpenTMS: `FindOrCreate` by name on Relation (three implementations with different matching rules, one hard-coded to NL postcodes, `RelationAppService.cs:1420`), Product, TransportUnit, TimeSlot; vehicles auto-created by four FMS code paths, one writing the device id into `LicencePlate`.

Requirement: one matching service per entity in the owning module (exact natural key first, then normalised name plus postcode); imports either match or create a *proposal* for review; devices are bound through one explicit workflow with a unique (tenant, vendor, deviceId).

## 4. Host-level master data and transport profiles (PARKED, PO decision 2026-10-11)

Not built now. Recorded here so the Master Data design does not block it: entities must also be able to exist at host level later, so that transport profiles with their own master data can be defined and used by the onboarding wizard. The only thing the first task does for this is keep `TenantId` nullable (`IMultiTenant`) and avoid any assumption that a row always has a tenant.

Design that fits the architecture:

- Entities implement `IMultiTenant` with nullable `TenantId`. Host rows have `TenantId = null` and live in the host database's module schemas (which already exist for shared-tier tenants). The ABP data filter handles visibility; no filter bypass needed.
- Platform owns a host-level `TransportProfile` aggregate (name, description, active). Every master data entity in every module gets a nullable `TransportProfileId`, filled only on host rows. Rows with a null profile id on host level are the base set shared by all profiles.
- Onboarding (the Platform tenant-provisioning service, today a stub) copies base rows plus the chosen profile's rows into the new tenant database inside `ICurrentTenant.Change(newTenantId)`, keeping the source ids (or recording `SourceId`) so later profile changes can be diffed.
- Unique indexes: PostgreSQL treats NULLs as distinct, so `(TenantId, Code)` would allow duplicate host rows. Use `NULLS NOT DISTINCT` (PostgreSQL 15+) or an index on `COALESCE(TenantId, <empty guid>)`.
- Permissions use `multiTenancySides: Both`; the Master data app is visible for host users.
- Each of the six tasks must include this requirement in its design doc, because profiles contain VAT rows, order types and email templates as much as relations and vehicles.

## 5. Per cluster: what to model differently (unchanged by the decisions above)

**Fleet.** Vehicle with a kind (tractor, trailer, rigid, van) and a Combination/coupling entity instead of three fixed slots plus `IsPrimaryVehicle` (`Vehicle.cs:41-45`, `TransportFile.cs:71-75`); licence plate as a value object (an unused `DutchLicensePlate` exists); Euro class as an enum; registry (RDW) data as a separate read-only block; telematics state (`LastSeen*`) and positions outside master data (AO-12 still open). Capabilities: one catalogue, join tables with unique constraints, explicit override semantics between type and vehicle (today dimensions are copied once but capabilities inherited live).

**Products and packaging.** ADR as a reference to a dangerous-goods catalogue instead of 12 free-text fields (only UN number, class and description are read today). TransportUnit split into packaging type (Master Data), load factors (Planning) and the pallet ledger (Financial). Incompatibility rules (temperature, product, unit) exist three times today; one domain service.

**Reference lists.** Vat as a tax category with validity rows and ledger accounts; connectors map by category, not by comparing doubles (`Snelstart.cs:327`, `Minox.cs:124`). TimeSlot with timezone semantics and a correct overnight rule (`TimeSlotHelper.cs:30` is wrong today). GeographicalRegion with proper postcode-range matching (today a postcode without leading digits matches every numeric range, `GeographicalRegionAppService.cs:214`) and a defined recompute trigger for membership.

**Financial configuration.** D&S with an enum for value kind (fixed, percentage, per km) instead of two booleans, `DaysOfWeek` flags instead of seven columns, validity rows, a currency on fixed amounts, and a stable code so invoicing never matches on display name (today `FreightPrice.Description == DiscountAndSurcharge.Name`, `BaseInvoiceLineCreationService.cs:174-180`). AdditionalService referenced by FK from order lines. InvoiceReminderProfile only when dunning is actually built; today no worker reads it. CurrencyDayRate only when FX conversion is actually used; today nothing reads it.

**Platform and integrations.** ReportDefinition behind a rendering-provider abstraction (today a pointer to pdfgeneratorapi.com with hard-coded template ids). Secrets in a vault or envelope-encrypted, never in slots `Credentials1..3` and never returned by a read endpoint. BinaryObjectType transformations as a typed pipeline.

## 6. Things to act on in the current OpenTMS now, independent of the rebuild

- Secrets in source: an API key in `CurrencyExchangeRateBackgroundWorker.cs:29`, credentials in `WebApiCredentialRepository.cs:22-23`, a connection string in `FiLogic.LegacyTms/LegacyTmsService.cs:12`, a cipher passphrase in `AppConsts.cs`. Rotate and move to configuration.
- Secrets returned to the browser: `CredentialEditDto.Credentials1..3`, `ExchangeGatewayDto.Password` and `BearerClientSecret`. Stored in plaintext.
- FMS webhooks accept a forgeable base64 device token and can auto-register devices (`FleetManagementSystemAppService.cs:2199` and the anonymous endpoints around 276-640); tokens are logged.
- TLS and SSH host verification disabled for exchange gateways (`FtpExchangeGateway.cs:29`, `SftpExchangeGateway.cs:41`).
- Anonymous region lookups (`GeographicalRegionAppService.cs:163,190`).
- Bugs: D&S region links never saved on create (nested unit of work never completed, `DiscountAndSurchargeAppService.cs:118-125`); reminder schemes orphaned on delete (`InvoiceReminderProfileAppService.cs:126`); vehicle org-unit removal hits every vehicle (`VehicleAppService.cs:949`); overnight time slots; UK/IE postcodes match every region; merge wizards skip child tables.

## 7. Shape of each task brief for the opentms-next agent

- One brief per module; for the first run that is four briefs (section 2.1). State the placement table and the KEEP decisions of section 3 as PO decisions so the agent does not stop on them; name the out-of-scope entities explicitly.
- Require gate 1 first (PBI, then design doc with all nine sections), then implementation against the approved design: one migration in the module's schema with its indexes, feature flag default off, permissions per entity with the architecture test, localisation nl/en, OpenAPI and typed client regenerated. Host-level rows and `TransportProfileId` are parked (section 4).
- Hand this document along; the agent cites the section number when a design choice deliberately differs from OpenTMS.
- Reserve a separate brief for the data migration from OpenTMS: duplicate relations and vehicles, delimited strings, and the inverted `ExternalIdentifier` key/value need a mapping step, not a copy.
