# PBI: Platform, a shared base for every domain's reference data and one number-sequence service

<!-- Written on the PO's instruction of 2026-10-22 ("bouw stamgegevens die een order en trip nodig hebben", designs
     51–53) and assessment docs/input/masterdata-rebuild-assessment.md §3.9 ("one number-sequence service in Platform
     … used by all four types"). No GitHub issue; "50" follows design 49. -->

## Story

As a developer of Orders, Financial and Planning & Execution I want the list, lookup, create, update, delete and bulk
delete contract that Master Data established (design 41) available to my module, so the Master data app shows every
domain's lists the same way without each module copying it. As an order clerk, an invoicing clerk and a planner I want
order, trip and invoice numbers that follow the format of their type (prefix, year, month, digits), never repeat and
never skip, because customers, carriers and the tax office read them.

## Affected area

Platform: `ReferenceData` base types in Domain, Application.Contracts and Application; `NumberSequences` in
Domain.Shared, Domain and EntityFrameworkCore; one Platform migration. Frontend: a generic hook factory in
`src/lib/reference-data/`. No screen of its own; designs 51–53 use both.

## Tenant dimension

Counters are per tenant (and per sequence). No feature flag: no user-visible behaviour of its own.

## Expected volumes

Counters: one row per numbered type and period (≈ 10 types × 12 months a year per tenant). Generation: up to 5,000
numbers a day per tenant (orders, trips, invoices), peaks of 20 a minute on one type.

## Known edge cases

- Two orders of one type saved at the same moment: two different numbers, no gap.
- A save that rolls back after taking a number: the number is free again (gapless).
- Year and month change at midnight Dutch time, not UTC.
- A format changed after numbers were issued; a tenant migrating from OpenTMS that must continue at its last number.
- Lowering the next number below an issued one.

## Privacy note

None: counters hold keys and integers.

---

# Design: Platform, reference-data base and number sequences

**PBI:** above (this file); assessment §3.8, §3.9, §3.13, §3.14
**Status:** approved (gate 1, PO instruction 2026-10-22; the PR is the review of the details, Assumptions first)
**Date:** 2026-10-22
**Depends on:** design 41 (the contract this generalises).

> **Platform change — risk class critical by default.**

## Domain impact

**Module:** Platform. One new table (`platform."NumberSequenceCounters"`); no domain entity. **Decisions relied on:**
AD-02, AD-03, AD-16 (the counter commits in the same transaction and database as the numbered fact), AD-18. Extends
none of the three flows (commands only).

### Reference-data base

The design-41 template, lifted into Platform so Orders, Financial and Planning & Execution use one contract:

| Type | Project | Content |
|---|---|---|
| `ReferenceData.ReferenceDataAggregateRoot` | Domain | `FullAuditedAggregateRoot<Guid>`, `IMultiTenant` with nullable `TenantId` (assessment §3.6, §4), `Description` (2,500), name and code normalisation helpers |
| `ReferenceData.ReferenceDataConsts` | Domain.Shared | name 128, code 16, description 2,500, search 128, page 500, lookup 500, lookup page 25, ledger account 32 |
| `ReferenceData.ReferenceDataErrorCodes` | Domain.Shared | `Platform:ReferenceData.DuplicateKey`, `.InUse`, `.ReferenceNotFound`, `.TooManyRows`, `.UnknownSortColumn`, `.NameRequired`, `.DefaultRequired`, `.TooManyChildren`, `.InvalidNumberSequence`, `.NextNumberTooLow` — localized in the Platform resource (nl, en) |
| `ReferenceData.*Dto` | Application.Contracts | `ReferenceDataListRequestDto`, `ArchivableListRequestDto`, `ReferenceDataLookupDto`, `ReferenceDataDetailDtoBase`, `ReferenceDataBulkDeleteRequestDto`, `ArchivableBulkDeleteRequestDto`, `ReferenceDataBulkDeleteResultDto`, `ReferenceDataSkippedRowDto`, `LocalizedNameDto`, `IReferenceDataUpdateDto`, `NumberSequenceFormatDto` — the shapes of design 41 under Platform names |
| `ReferenceData.ReferenceDataCrudAppServiceBase<…>` | Application | design 41's template methods (paged list with one wildcard search, sort whitelist, joined sort, bounded lookup, create/update with concurrency stamp, delete and bulk delete with in-use reasons, one-default-per-tenant helper) |

Master Data keeps its own copy for now (its error codes and DTO names are in the committed API); converging it onto
this base is a separate, mechanical change (Assumption 4).

### Number sequences (assessment §3.9: KEEP number sequences on the types, one service)

**`NumberSequenceFormat`** (Domain.Shared, value object, stored as owned columns `Numbering*` on each numbered type):

| Field | Rule |
|---|---|
| `Prefix` | string(16)?, letters, digits, `-` and `_` only |
| `IncludeYear` | bool; the counter restarts every year |
| `IncludeMonth` | bool; requires `IncludeYear`; the counter restarts every month |
| `Separator` | `""`, `"-"`, `"/"` or `"."`; joins prefix, year, month and counter |
| `Digits` | 3–10; the counter is zero-padded to it (a counter that outgrows it simply gets longer) |

`Format(counter, localDate)` → e.g. prefix `TO`, year, month, separator `-`, 5 digits: `TO-2026-10-00042`.
`Period(localDate)` → `""`, `"2026"` or `"2026-10"`. Both pure and unit-tested; the editor preview uses the same rule
(the frontend mirrors it in `src/lib/reference-data/number-sequence.ts`, tested against the same cases).

**`NumberSequenceCounter`** (Domain, `platform."NumberSequenceCounters"`): `TenantId`, `SequenceKey` string(128)
(`<Module>.<Type>:<typeId>`, e.g. `Orders.TransportOrderType:6f…`), `Period` string(7), `LastValue` bigint. Unique
`(TenantId, SequenceKey, Period)` `NULLS NOT DISTINCT`. Never soft-deleted.

**`INumberSequenceGenerator`** (Domain; EF implementation):

- `NextAsync(sequenceKey, format)` → the formatted number. One parameterised `INSERT … ON CONFLICT (…) DO UPDATE SET
  "LastValue" = "LastValue" + 1 RETURNING "LastValue"` **inside the caller's transaction** (refused outside a
  transactional unit of work, like the outbox). The row lock holds until the fact commits: two saves on one type get
  consecutive numbers, a rollback returns the number. Gapless per sequence and period (Assumption 1).
- `PeekNextAsync(sequenceKey, format)` → the number the next save would get (editor preview; no lock).
- `SetNextValueAsync(sequenceKey, format, nextValue)` → for a tenant that continues OpenTMS numbering: sets
  `LastValue = nextValue − 1` for the current period, **only upwards** (`NextNumberTooLow` otherwise).
- The local date is the date in `NumberSequences:TimeZone` (default `Europe/Amsterdam`, Assumption 2).

Uniqueness of the issued number is the owning entity's unique index (the order, trip or invoice design); the counter
makes collisions impossible except after a format change that removes the year (the editor warns; the index refuses).

## API contract

No endpoint of its own. The numbered types (designs 51–53) carry `numbering: NumberSequenceFormatDto` on their detail,
create and update DTOs and `nextNumberPreview` plus `nextNumber` on the detail DTO; their update accepts
`nextNumber` (only upwards). OpenAPI and client regenerated with those designs.

## Migration strategy

Expand only. Platform migration `AddNumberSequenceCounters`: the table and its unique index, in the host and every
tenant database through the DbMigrator. `Down` drops the table.

## UI design

No screen. `src/lib/reference-data/crud-hooks.ts` is design 41's hook factory with the path and query-key namespace as
parameters (`createReferenceDataHooks("orders", "transportOrderTypes", "/api/orders/transport-order-type")`); the
Master data app's existing `MasterDataListView` and `MasterDataEditor` render the new lists. A shared
`NumberingFields` component (`src/apps/master-data/components/numbering-fields.tsx`) renders the numbering section of
every numbered type: Prefix, Separator (`Select`), Include year, Include month (disabled without year), Digits, Next
number, and a live preview line ("Next number: TO-2026-10-00042").

## Test risk analysis

| Part | Class | Mandatory coverage |
|---|---|---|
| Concurrency: 50 parallel transactions on one sequence → 50 distinct consecutive numbers | **Critical** | PostgreSQL test |
| Gapless: a rolled-back transaction returns its number | **Critical** | PostgreSQL test |
| Tenant isolation of counters | **Critical** | PostgreSQL negative test |
| Refused outside a transaction | **High** | SQLite + PostgreSQL |
| Format and period: every option, overflow of digits, month without year refused, Amsterdam midnight | **High** | unit tests (table-driven), frontend mirror with the same table |
| `SetNextValueAsync` only upwards; `PeekNextAsync` never increments | **High** | SQLite tests |
| Base template: list paging, search, sort whitelist, lookup bound, concurrency stamp 409, bulk delete limit, one default | **High** | covered through the first concrete service (design 53) and its tests |
| Migration | **Critical** | PostgreSQL migration test |

## Flag & rollout plan

No flag (no user-visible behaviour). Migrate, then deploy. Rollback: nothing calls it.

## Cost & SLO impact

One tiny table; one upsert per numbered save (sub-millisecond), serialising saves of one type for the rest of their
transaction (Assumption 1: acceptable at ≤ 20 a minute per type; the mutation budget p95 < 500 ms holds). No jobs,
no egress.

## Assumptions

1. **Gapless beats throughput:** the counter is taken in the save's own transaction, so saves on one type wait for each
   other's commit. Dutch invoices must be numbered consecutively; orders and trips follow the same rule for
   simplicity. A type that needs throughput over gaplessness can later take its number in a separate transaction.
2. **Periods follow Dutch local time** (`Europe/Amsterdam`, configurable per installation), not the tenant's own time
   zone; there is no tenant time-zone setting yet.
3. Each type has its own counter (`<Module>.<Type>:<id>`); two types never share one.
4. Master Data's own base (design 41) stays as it is in this run; moving it onto the Platform base renames DTO schemas
   in the API and is its own change.

## Security quickscan

- **Permissions:** none of its own; the numbered types' permissions guard `nextNumber`.
- **Input validation:** prefix character set, separator whitelist, digits 3–10, next number > last issued.
- **SQL:** parameterised upsert on Platform's own mapped table.
- **Tenant isolation:** `IMultiTenant`; the data filter applies to reads; the upsert writes `CurrentTenant.Id`.
- **Logging:** sequence key and tenant id; numbers are not personal data.
