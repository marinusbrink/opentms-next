# PBI: Platform master data: e-mail templates, report definitions, document types, number sequences

<!-- Written from the PO's instruction of 2026-10-16 ("build master data entities of platform: mailsjablonen,
     rapportdefinities, documentsoorten, nummerreeksen"), the assessment docs/input/masterdata-rebuild-assessment.md
     (§2 row 5, §2.1 Platform row, §2.2, §3.8, §3.9, §3.10, §4, §5 "Platform and integrations") and a read of the
     OpenTMS sources (paths relative to aspnet-core/src/). No GitHub issue; "49" is the next free number after PR #48.
     PO decision of the same instruction: fixed CMR texts (ConsignmentNoteFixedDescription) and transport profiles are
     NOT Platform; they belong to the transport domain and are built with it (see "Out of scope"). -->

## Story

As a tenant administrator I want to maintain, in the Master data app, the e-mail templates the system sends with,
the report definitions documents are rendered from, the kinds of documents the digital archive files under, and the
number sequences order, trip, invoice and purchase-order numbers are drawn from, because the Orders, Planning,
Financial and Integrations runs that send mail, print documents, archive files and number records all reference
these by id and cannot be built before they exist.

## Affected area

The Platform module (schema `platform`): four aggregates, their application services under `/api/platform/...`, one
published number generator, and a new **Platform** domain group in the Master data app's nav tree (PO 2026-10-16:
the Master data app holds every domain's master data, one group per domain).

**Out of scope (PO decision 2026-10-16):** `ConsignmentNoteFixedDescription` (vaste CMR-teksten), the CMR print
profile (`ConsignmentNoteProfile`, "Vrachtbriefprofiel") and `TransportProfile` (transportprofielen, assessment §4)
belong to the **transport domain** and are built with its first run, not here; the assessment, CLAUDE.md and the
architecture docs record this. Also out: sending mail, rendering documents, storing attachments (GAP-05), executing
transformations, inbound mailboxes (Integrations, assessment §2 row 6), and the type entities that will reference a
number sequence (Orders, Planning, Financial runs).

## Tenant dimension

Configurable per tenant; every row belongs to one tenant (`TenantId` nullable, assessment §4 parked). App-tree
features (tenants-and-editions D8): `Apps.MasterData.Platform` (the domain group, default **on**, as
`Apps.MasterData.General`) and four leaves, default **off**: `Apps.MasterData.Platform.EmailTemplates`,
`Apps.MasterData.Platform.ReportDefinitions`, `Apps.MasterData.Platform.DocumentTypes`,
`Apps.MasterData.Platform.NumberSequences`.

## Expected volumes

| Entity | Rows per tenant | Note |
|---|---|---|
| EmailTemplate | up to 200 | 9 purposes, a few variants each |
| EmailTemplateContent | 2 per template (nl, en) | bodies up to 200 KB |
| ReportDefinition | up to 200 | |
| DocumentType | up to 100 | |
| DocumentTypeTransformation | 0–4 per type | one per kind |
| NumberSequence | up to 50 | one per order/trip/invoice/purchase-order type |
| NumberSequenceCounter | sequences × periods | a monthly sequence adds 12 rows a year |

Hot path: `INumberGenerator.GetNextAsync`, once per created order, trip, invoice and purchase order (up to 2,000 a
day per tenant, bursts from imports of 500 orders).

## Known edge cases

- Two order imports drawing numbers from the same sequence at the same moment (must never give one number twice).
- The first number of a new year or month (the counter row does not exist yet, two callers create it at once).
- A caller's transaction rolls back after drawing a number (the number must not be lost or reused).
- A Scriban template with a syntax error; a body of 200 KB; a template in a language the tenant does not use.
- Making a second template the default for a purpose.
- A regex argument that is invalid or catastrophic.

## Privacy note

Templates may hold a company e-mail address in Sender/Reply-to (business data, not a natural person's PII by intent;
not logged). No personal data otherwise. Logs carry ids and counts only.

# Design: Platform master data

**PBI:** above (this file)
**Status:** built on the PO's instruction of 2026-10-16; the assumptions below are open for the PO's confirmation
**Date:** 2026-10-16
**Depends on:** tenants-and-editions (app-tree features, D15 domain groups), design 41 (`LocalizedName`, list
template), PR #48 (create in place, three-level nav).

## Domain impact

**Module:** Platform, schema `platform`. Four aggregates (`EmailTemplate`, `ReportDefinition`, `DocumentType`,
`NumberSequence`) plus the counter entity `NumberSequenceCounter`; one additive migration `PlatformMasterData` in the
Platform context. Platform references no domain module (rule 4); consumers in other modules reference these rows by
id without a database constraint and validate through `Application.Contracts` (assessment §2.2).

**Flow:** commands only (AD-18); no event in this run. It extends none of the three flows of `02-flows.md` yet; the
number generator is the building block the order-intake flow will call.

**Decisions relied on:** AD-02, AD-03, AD-06 (no jsonb except the `LocalizedName` of document types, as design 41),
AD-09 (generated documents are stored, so a changed report definition never changes a past document), AD-16, AD-18.

**Published interfaces (Application.Contracts):**
- `INumberGenerator.GetNextAsync(Guid numberSequenceId, DateOnly date)` → the formatted next number. Not an
  application service (no HTTP endpoint); runs in the caller's unit of work.
- The read side of each list for pickers in other modules: `GetLookupAsync` on each app service (permission of the
  list's read), as Master Data's lists.

### EmailTemplate (e-mailsjabloon; OpenTMS `OutboundEMailMessageTemplate`)

| Field | Type | Notes |
|---|---|---|
| `Name` | string(128), required | unique per tenant **regardless of case** (`NormalizedName`, PO 2026-10-16 rule as vehicles) |
| `Purpose` | enum `EmailTemplatePurpose` | `TransportOrder`, `TransportFile`, `Invoice`, `InvoiceReminder`, `PurchaseOrder`, `EtaNotification`, `DriverInstructions`, `DigitalArchive`, `ProformaOverview` (OpenTMS's free-text `EntityTypeName`, §3.10) |
| `IsDefault` | bool | at most one default per purpose (unique partial index, §3.8); setting it clears the previous default in the same transaction |
| `SenderAddress`, `SenderName`, `ReplyTo`, `To`, `Cc`, `Bcc` | string(512)? | Scriban expressions allowed (`{{ company.email }}`); `To` empty = the consumer's recipient |
| `SendCopyToSender` | bool | |
| `Description` | string(2500)? | |
| child **EmailTemplateContent** | `Culture` string(10), `Subject` string(512), `HtmlBody` text (≤ 200,000 chars) | one row per language (§3.10 "per-language content"); a row for the default language `en` is required, `nl` optional; languages limited to the configured ones (nl, en); unique (template, culture) |

Every Scriban field is parsed on save (`Scriban.Template.Parse`); a syntax error is refused with
`Platform:TemplateSyntaxError` naming the field, language and line. Placeholders are **Scriban** (`{{ … }}`), the one
placeholder system of §3.10; the typed model of each purpose (and so the list of placeholders) is published by the
module that sends that mail, with that module's run. HTML encoding happens when the model is built (§3.10).

### ReportDefinition (rapportdefinitie; OpenTMS `ReportDefinition`, UI "Bestandlayouts")

| Field | Type | Notes |
|---|---|---|
| `Name` | string(128), required | unique per tenant regardless of case |
| `Purpose` | enum `ReportPurpose` | `TransportOrderConfirmation`, `TransportFile`, `Invoice`, `PurchaseOrder`, `TransportUnitBalance` |
| `Culture` | string(10), required | a configured language |
| `Provider` | enum `ReportRenderingProvider` | `PdfGeneratorApi` only for now (§5: behind a rendering-provider abstraction) |
| `ProviderTemplateId` | string(128), required | the provider's template id |
| `FileNamePattern` | string(255), required | Scriban (`factuur-{{ number }}.pdf`), parsed on save; `.pdf` added when missing |
| `IsDefault` | bool | at most one default per (purpose, culture) (unique partial index) |
| `IsArchived` | bool | OpenTMS `IsDisabled`: hidden from pickers and automatic selection |
| `AvailableInCustomerPortal` | bool | OpenTMS `IsAvailableForSelfServicePortalUsers` |
| `Description` | string(2500)? | |

### DocumentType (documentsoort; OpenTMS `BinaryObjectType`, UI "Soort digitale documenten")

| Field | Type | Notes |
|---|---|---|
| `Code` | string(32), required | upper case, unique per tenant; the natural key the seed upserts on (§3.2) |
| `Name` | `LocalizedName` (jsonb) | nl/en, as AddressType (§2.1a) |
| `IsFreightDocument` | bool | shown in portals and exported in OTM |
| `IncludeWithInvoice` | bool | attached to the invoice mail |
| `ShowInListIndicator` | bool | OpenTMS `IncludeInGenericListView` |
| `IsArchived` | bool | hidden from pickers |
| `Description` | string(2500)? | |
| child **DocumentTypeTransformation** | `Position` int (1-based), `Kind` enum `DocumentTransformationKind` (`ImageToPdf`, `LinkByFileName`, `RenameByParentProperty`, `SplitPdfByReferencePattern`), `Argument` string(255)? | §5 "a typed pipeline": an enum, never a class name resolved by reflection; each kind at most once per type; `Argument` required for `RenameByParentProperty` and `SplitPdfByReferencePattern` (must compile as a .NET regex with a 1 s match timeout); at most 4 rows. Executed by the attachments run (GAP-05), not here |

Seed (DbMigrator, every tenant, upsert on `Code`, never overwriting tenant edits): `FREIGHT` "Vrachtdocument" /
"Freight document" (freight document, with invoice, list indicator) and `GENERAL` "Algemeen document" / "General
document". Code never matches on the name.

### NumberSequence (nummerreeks) and NumberSequenceCounter

OpenTMS keeps the numbering fields on each type entity (four drifted copies) and an atomic counter only for invoices
(`EntityNumbers/EntityNumberCounterService.cs:71-99`). Here the format is one Platform list the type entities will
reference by id (assessment §3.9: "one number-sequence service in Platform configured per type"; the PO's list of
2026-10-16 makes the sequence itself a master-data list, see Assumption 1).

| Field | Type | Notes |
|---|---|---|
| `Name` | string(128), required | unique per tenant regardless of case |
| `Scheme` | enum `NumberSequenceScheme` | `FourDigitYear`, `TwoDigitYear`, `FourDigitYearMonth`, `TwoDigitYearMonth`, `CounterOnly` (OpenTMS `EntityNumberCreationOption`, same order) |
| `Prefix`, `YearPrefix`, `YearSuffix`, `MonthPrefix`, `MonthSuffix` | string(16)? | the five affixes of OpenTMS's invoice type (the most complete copy) |
| `MinimumDigits` | int 1–12 | replaces the zeros-only `Format` string ("0000" → 4) |
| `Description` | string(2500)? | |
| child **NumberSequenceCounter** | `PeriodYear` int (0 = none), `PeriodMonth` int (0 = none), `LastValue` long | unique (`NumberSequenceId`, `PeriodYear`, `PeriodMonth`); 0 instead of NULL so the upsert's conflict target works on every provider |

Number = `Prefix` + [`YearPrefix` + `yyyy`|`yy` + `YearSuffix`] + [`MonthPrefix` + `MM` + `MonthSuffix`] + counter
padded to `MinimumDigits` (`NumberSequenceFormatter`, a pure function in `Domain.Shared`, mirrored in the frontend for
the editor's example). Example: `O-` + `26` + `-` + `0001` = `O-26-0001`.

**Generation:** one statement per call, inside the caller's transaction:
`INSERT … ON CONFLICT ("NumberSequenceId","PeriodYear","PeriodMonth") DO UPDATE SET "LastValue" = "LastValue" + 1
RETURNING "LastValue"`. The row lock serialises concurrent callers until commit; a rollback gives the number back, so
numbers are gapless per committed transaction. The period comes from the record's date (the caller passes it), not
from the clock. Repository `INumberSequenceCounterRepository` in `Domain`, EF implementation with raw SQL in
`EntityFrameworkCore` (PostgreSQL and SQLite ≥ 3.35 both support the statement).

**Guards:** once any counter row exists ("in use"), `Scheme`, the five affixes and `MinimumDigits` cannot change
(OpenTMS `OutboundInvoiceTypeAppService.cs:118-133`) and the sequence cannot be deleted; name and description stay
editable. The DTO carries `IsInUse` and `ExampleNumber` (for today).

### Feature flags and permissions

- Features (Platform `AppFeatures.MasterData.Platform`, mirrored in `apps.config.ts`): as in *Tenant dimension*. Each
  list's endpoints require the chain app → `Apps.MasterData.Platform` → its leaf; `GetLookupAsync` (pickers) needs only
  `Apps.MasterData` (D12). `INumberGenerator` is not feature-gated: it is called by other modules' services.
- Permissions, both sides (§4), in `PlatformPermissions.MasterData`:
  `Platform.EmailTemplates`, `Platform.EmailTemplates.Create`, `Platform.EmailTemplates.Update`, `Platform.EmailTemplates.Delete`,
  `Platform.ReportDefinitions`, `Platform.ReportDefinitions.Create`, `Platform.ReportDefinitions.Update`, `Platform.ReportDefinitions.Delete`,
  `Platform.DocumentTypes`, `Platform.DocumentTypes.Create`, `Platform.DocumentTypes.Update`, `Platform.DocumentTypes.Delete`,
  `Platform.NumberSequences`, `Platform.NumberSequences.Create`, `Platform.NumberSequences.Update`, `Platform.NumberSequences.Delete`.
  Seeded for the `admin` role on both sides by `AdministrationPermissionDataSeedContributor`.

### Deliberate differences from OpenTMS

| Area | Dropped or changed | Reason |
|---|---|---|
| E-mail template | free-text `EntityTypeName` | enum `Purpose` (§3.10) |
| E-mail template | one row per language with its own addresses; `IsDefault` recomputed per entity | one template, content per language; one enforced default per purpose (§3.8, §3.10) |
| E-mail template | three tag systems with `[Tag]` and `string.Replace` | Scriban, parsed on save; typed models from the consumers (§3.10) |
| E-mail template | `Tag` (Postmark), `TextBody` | not editable in OpenTMS; the text part is derived from the HTML by the sender |
| Report definition | `EntityTypeName` resolved by reflection | enum `Purpose` |
| Report definition | template editor URL, Copy, Reset to default, hard-coded default template ids | the rendering provider's adapter (document generation run); provider credentials are secrets (Integrations) |
| Report definition | no default; "first by language" | one enforced default per purpose and language |
| Document type | `Mailbox` | inbound mailboxes are Integrations (§2 row 6) |
| Document type | `ConvertToPdf` | no consumer; the `ImageToPdf` transformation covers it |
| Document type | transformations as class names (`TransformationName`) | enum kinds (§5, §3.10) |
| Document type | `FindOrCreate` by name from OTM imports | the import run matches on code (§3.15) |
| Number sequence | numbering fields copied on four type entities; three generators scanning the last number without a lock | one list, one atomic generator (§3.9) |
| Number sequence | zeros-only `Format` string | `MinimumDigits` |

## API contract

Conventional controllers, route root `platform`, the design-41 list shape (`ListRequestDto`, `GridResponse<RowDto>`,
detail, create and update DTOs with concurrency stamp, `BulkDelete` over a grid selection, `GetLookup`).

| Service | Route root | Extra operations |
|---|---|---|
| `EmailTemplateAppService` | `/api/platform/email-template` | — |
| `ReportDefinitionAppService` | `/api/platform/report-definition` | `POST /{id}/archive`, `POST /{id}/unarchive` |
| `DocumentTypeAppService` | `/api/platform/document-type` | `POST /{id}/archive`, `POST /{id}/unarchive` |
| `NumberSequenceAppService` | `/api/platform/number-sequence` | — |

- Rows: email template `id, name, purpose, isDefault, cultures (joined)`; report definition `id, name, purpose,
  culture, provider, isDefault, isArchived`; document type `id, code, name, isFreightDocument, includeWithInvoice,
  isArchived`; number sequence `id, name, scheme, exampleNumber, isInUse`. Filters: `purpose` (templates, report
  definitions), `includeArchived`. Wildcard over name (and code). Sort whitelists per row.
- Errors (`PlatformErrorCodes`, localized nl/en): `DuplicateKey`, `InUse`, `TemplateSyntaxError`,
  `DefaultLanguageContentRequired`, `UnsupportedCulture`, `InvalidRegex`, `TransformationArgumentRequired`,
  `DuplicateTransformation`, `NumberSequenceInUse`, `UnknownSortColumn`, `TooManyRows`, `NameRequired`.

## Migration strategy

One additive migration `PlatformMasterData` in the Platform context: tables `EmailTemplates`,
`EmailTemplateContents`, `ReportDefinitions`, `DocumentTypes`, `DocumentTypeTransformations`, `NumberSequences`,
`NumberSequenceCounters`. Indexes: per-tenant unique (`NULLS NOT DISTINCT`, soft-delete filter) on each
`NormalizedName` and on `DocumentTypes.Code`; partial unique default indexes (`(TenantId, Purpose) WHERE IsDefault`
and `(TenantId, Purpose, Culture) WHERE IsDefault`); unique (`EmailTemplateId`, `Culture`), (`DocumentTypeId`,
`Kind`), (`NumberSequenceId`, `PeriodYear`, `PeriodMonth`). Children cascade with their aggregate. Expand only;
`Down` drops the tables. The document-type seed is a data-seed contributor (DbMigrator and provisioning job).

## UI design

**Navigation** (`apps.config.ts`): a new domain group **Platform** (`/master-data/platform`, feature
`Apps.MasterData.Platform`) after General, with four lists directly under it: **E-mail templates**
(`/master-data/email-templates`), **Report definitions** (`/master-data/report-definitions`), **Document types**
(`/master-data/document-types`), **Number sequences** (`/master-data/number-sequences`). Dutch: Platform,
E-mailsjablonen, Rapportdefinities, Documentsoorten, Nummerreeksen.

Lists use `MasterDataListView` (OpenTmsGrid, wildcard search, command bar New / Delete selected, row actions Edit,
Archive/Unarchive where archivable, Delete). Editors use `MasterDataEditor`:

- **E-mail template** (`lg`): tab *General* (Name, Purpose `Select`, Default switch, Description), tab *Addresses*
  (Sender address, Sender name, Reply-to, To, Cc, Bcc, Send copy to sender), one tab per language (*English*,
  *Nederlands*: Subject, HTML body as a monospace source `Textarea` with a sandboxed preview, `iframe sandbox` with
  no scripts). See Assumption 3 for GAP-08.
- **Report definition** (`md`): Name, Purpose, Language, Provider, Provider template id, File name pattern, Default,
  Archived, Available in customer portal, Description.
- **Document type** (`md`): tab *General* (Code, Name per language, the three switches, Archived, Description), tab
  *Transformations* (`LinesGrid`: position by row order, kind `Select`, argument).
- **Number sequence** (`sm`): Name, Scheme, Prefix, Year prefix/suffix, Month prefix/suffix (shown only for the
  schemes that use them), Minimum digits, the live example ("Next number looks like O-26-0001"), Description. When in
  use, the numbering fields are read-only with an inline note.

No record pickers in these editors (only enum `Select`s), so UI-LOOK-004 adds nothing. Manual:
`docs/manual/stamgegevens.md` gets a Platform section (Dutch).

## Test risk analysis

| Part | Class | Mandatory coverage |
|---|---|---|
| Number generation (atomicity, period rollover, rollback gives the number back, formatter) | **Critical** | PostgreSQL: 50 parallel callers get 50 distinct consecutive numbers; first call of a period from parallel callers; rollback; formatter table tests |
| Tenant isolation of all four lists and of the counters | **Critical** | PostgreSQL negative tests: tenant B never reads, updates, deletes or draws from tenant A's rows (own database and shared tier) |
| Unique and default indexes, migration | **High** | PostgreSQL index tests incl. case-insensitive names and one default |
| Scriban/regex validation, guards when in use, default switching | **High** | SQLite service tests, table-driven |
| Permissions and feature chain on every method | **High** | authorization test per service; architecture test |
| Frontend editors and lists | **Medium** | vitest per editor (validation, in-use read-only, example number), nav/feature tests |
| Seed | **Medium** | idempotent re-run, tenant edits kept |

## Flag & rollout plan

Four leaves default off; a tenant (or edition) switches them on in the Tenants app. The generator and the lookups need
no flag. Rollback: switch the leaves off; the migration is additive.

## Cost & SLO impact

No new infrastructure. Generation: one indexed upsert, p95 < 20 ms; list reads p95 < 300 ms; mutations p95 < 500 ms.

## Assumptions

1. **Number sequences are a standalone Platform list** that the type entities (TransportOrderType, TransportFileType,
   OutboundInvoiceType, PurchaseOrderType) reference by `NumberSequenceId`. Assessment §3.9 said "number sequences
   stay on the type entities"; the PO's list of 2026-10-16 names *nummerreeksen* as Platform master data, which is
   read as superseding that wording while keeping its intent (configured per type, one service).
2. **No seeded e-mail templates and no seeded report definitions in this run.** Assessment §2.2 asks for default
   e-mail templates per language; their placeholders depend on the typed models the consuming modules publish, and the
   report definitions point at template ids in a provider workspace this project does not have. Each consuming run
   seeds its defaults.
3. **GAP-08 stays open:** the HTML body is edited as source with a sandboxed preview, not with a rich-text editor.
   A Scriban template with loops is source; a WYSIWYG editor arrives when the PO picks one (component map §4).
4. **Platform domain group without sub-groups:** four lists sit directly under *Platform*.
5. **Names unique regardless of case** for templates, report definitions and sequences, as for vehicles (PO
   2026-10-16).
6. **Document-type mailboxes move to Integrations** with the inbound mailboxes.

## Security quickscan

- **Permissions:** sixteen new definitions, both sides; every public method attributed (architecture test); four leaf
  features as the second gate.
- **Input validation:** lengths, enum ranges, configured cultures only, Scriban parsed (never rendered here), regex
  compiled with a match timeout, children bounded (2 contents, 4 transformations), body ≤ 200,000 characters.
- **Attack surface:** four conventional controllers behind authentication, permissions and features; no anonymous
  endpoint, no upload, no outbound call; the generator has no HTTP endpoint. The preview iframe is sandboxed without
  `allow-scripts`, so template HTML cannot run script in the app's origin.
- **Tenant isolation:** every entity `IMultiTenant`; the generator's raw SQL filters on a sequence id first read
  through the tenant-filtered repository, so a foreign id is `EntityNotFound`.
- **Logging:** ids and counts only; never template bodies, addresses or generated numbers.
