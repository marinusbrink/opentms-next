# PBI: Planning & Execution master data: transport trip type

<!-- Written on the PO's instruction of 2026-10-22: "bouw stamgegevens die een order en trip nodig hebben …
     TransportFileType wordt TransportTripType, TransportFile wordt TransportTrip". Assessment
     docs/input/masterdata-rebuild-assessment.md §2 row 4, §2.1 "Later runs: TransportFileType", §3.8, §3.9. -->

## Story

As a planner I want every transport trip (OpenTMS: transport file, *rit*) to have a type that sets its number format
and how it is normally executed, because trips are numbered per type and the planboard and the cost calculation treat
an own-fleet trip differently from a subcontracted one.

## Affected area

Planning & Execution module, schema `planning`: aggregate `TransportTripType`, its application service under
`/api/planning/transport-trip-type`, its seed, and the list in the Master data app under a new domain group
**Planning & Execution → Trips**.

## Tenant dimension

Configurable per tenant. Features `Apps.MasterData.PlanningExecution` (on), `Apps.MasterData.PlanningExecution.Trips`
(on), `Apps.MasterData.PlanningExecution.Trips.TransportTripTypes` (off by default, as every master-data list).

## Expected volumes

At most 50 types per tenant; usually 2–5.

## Known edge cases

Duplicate code; deleting or archiving the default type; two defaults; a numbering format without digits; continuing
OpenTMS's numbering; deleting a type that trips use (no trips yet: the check arrives with the trip design).

## Privacy note

None.

---

# Design: Planning & Execution master data, transport trip type

**PBI:** above (this file)
**Status:** approved (gate 1, PO instruction 2026-10-22; Assumptions are the open details)
**Date:** 2026-10-22
**Depends on:** design 50 (reference-data base, number sequences).

## Domain impact

**Module:** Planning & Execution, schema `planning`, first entity of the module; migration `PlanningTransportTripTypes`.
**Decisions relied on:** AD-02, AD-06 (`LocalizedName` as jsonb, the design-41 precedent), AD-18. No event; extends none
of the flows. **Naming (PO 2026-10-22):** OpenTMS *TransportFileType* is **TransportTripType**; OpenTMS *TransportFile*
is **TransportTrip** (glossary updated).

| Field | Type | Rule |
|---|---|---|
| `Code` | string(16), required | upper case, unique per tenant |
| `Name` | `LocalizedName` (jsonb) | the default culture's text required |
| `IsDefault` | bool | exactly one default per tenant (unique partial index, §3.8); the default cannot be archived or deleted; `set-default` moves it |
| `Numbering` | `NumberSequenceFormat` (owned columns) | design 50; sequence key `PlanningExecution.TransportTripType:<id>` |
| `DefaultExecution` | enum `TripExecution { OwnFleet = 1, Subcontracted = 2 }` | what a new trip of this type starts with |
| `SendToFleetManagementSystem` | bool | default true; a trip of this type is offered to the on-board computer when planned (the sync itself is Integrations) |
| `IsArchived` | bool | hides the type from pickers |
| `Description` | string(2500)? | |

### Deliberate differences from OpenTMS

| Dropped or changed | Reason |
|---|---|
| Copy-pasted number-sequence columns | Platform `NumberSequenceFormat` and one generator (§3.9) |
| Route-optimisation mode, default carrier and depots as columns | the routing engine (AD-12) and the trip design decide them; nothing reads them yet |
| `QuestionnaireId` | the questionnaire run (§3.10); no questionnaire exists |
| `IsDefault` without a constraint next to a tenant setting | one enforced default (§3.8) |

## API contract

`TransportTripTypeAppService`, route `/api/planning/transport-trip-type`, the design-41/50 shape:
`GET` (list: `startRow, endRow, wildcardSearch, sortBy, sortDir, includeArchived`), `GET /{id}`, `GET /lookup`,
`POST`, `PUT /{id}`, `DELETE /{id}`, `POST /bulk-delete`, `POST /{id}/set-default`.

- `TransportTripTypeRowDto`: `id, code, name, defaultExecution, numberingExample, isDefault, isArchived`; search over
  code and name; sort whitelist `code, name, defaultExecution, isDefault, isArchived`.
- `TransportTripTypeDto`: all fields, `names` (map), `numbering`, `nextNumber`, `nextNumberPreview`, audit, stamp.
- Create/Update: `code, names, numbering, nextNumber?, defaultExecution, sendToFleetManagementSystem, isArchived,
  description` (+ `concurrencyStamp`). A new tenant's first type becomes the default.
- Errors: Platform `DuplicateKey`, `DefaultRequired` (archive/delete of the default), `InvalidNumberSequence`,
  `NextNumberTooLow`.
- Permissions: `PlanningExecution.TransportTripTypes`, `.Create`, `.Update`, `.Delete`; `set-default` is `.Update`.
  Lookup needs only `Apps.MasterData` plus the read permission (pickers on the trip screen later).

## Migration strategy

Expand only: table `planning."TransportTripTypes"`; unique `(TenantId, Code)` and the one-default index, both
`NULLS NOT DISTINCT` and ignoring soft-deleted rows. Seed (DbMigrator, per tenant, idempotent on `Code`): `STD`
"Standaard" / "Standard", default, own fleet, numbering `TR`, year, `-`, 5 digits (`TR-2026-00001`).

## UI design

Nav: domain group **Planning & Execution** (`/master-data/planning`) → group **Trips** (`/master-data/trips`) →
**Transport trip types** (`/master-data/transport-trip-types`). List (`MasterDataListView`): Code, Name, Default
execution, Number example, Default (badge), Archived (hidden). Row actions Edit, Set as default, Delete; command bar
New, Delete selected. Editor (`EntityDialog size="md"`): *General* (Code, Name per language, Default execution,
Send to fleet management system, Archived), *Numbering* (`NumberingFields`, design 50), *Remarks*, Record
identification. Badge *Default* after the title.

## Test risk analysis

| Part | Class | Coverage |
|---|---|---|
| Tenant isolation | **Critical** | PostgreSQL negative test |
| Permissions and features | **Critical** | definition, flag-off and architecture tests |
| Unique code, one default | **Critical** | PostgreSQL index tests |
| Default rules (first becomes default, set-default moves it, default not archivable/deletable) | **High** | SQLite service tests |
| Numbering preview and next number upwards only | **High** | SQLite service tests |
| Seed idempotent | **High** | PostgreSQL seed test |
| Frontend list, editor, numbering preview | **Medium** | vitest |
| Localization nl/en parity | **Low** | key test |

## Flag & rollout plan

The list feature defaults off; the seed gives every tenant one default type. Rollback: feature off.

## Cost & SLO impact

Negligible: one small table. List and lookup p95 < 300 ms, mutations < 500 ms.

## Assumptions

1. `DefaultExecution` (own fleet or subcontracted) and `SendToFleetManagementSystem` are the only behaviour settings
   of a trip type now; route optimisation, default carrier and questionnaires come with the designs that read them.
2. Trip numbers are per type and gapless (design 50, Assumption 1).
3. The in-use check on delete arrives with the TransportTrip design.

## Security quickscan

Four permissions plus the feature chain; code and enum validation; tenant-scoped; no raw SQL in the module; no
personal data; logs ids and counts.
