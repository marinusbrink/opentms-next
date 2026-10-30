# Design: "Reset to dataset" — realistic test datasets for a tenant

**PBI:** none; built on the PO's direct instruction in a working session on 2026-10-30 ("In the host Tenants app, add
a menu item *Reset to dataset* with the child items *Average volume test set (5k relations + a regular random amount of
each known entity)* and *Large volume test set (150K relations + 1000+ entities of each known entity)*. Data needs to
be realistic, so real addresses, etc."). The PO's wording plus the answers below are the design; this note records
them so gate 2 can review against it.
**Status:** in review
**Date:** 2026-10-30

> **Platform change — risk class critical** (`/CLAUDE.md` rule 4): a host action that deletes a tenant's data. Design
> and implementation travel in one PR because the PO asked for the build directly; gate 1 and gate 2 are the same
> review.
>
> **Constitution rule 4 (feature flag):** the Tenants app is host-only and has no app features
> (`docs/designs/tenants-and-editions.md`, "App features"), so there is no per-tenant flag to put this behind. The
> action is gated by a new host permission that no role has by default except the host administrator (who has every
> permission). *Open for the PO: confirm this as a `DEVIATION(constitution-4)`.*

## PO decisions (2026-10-30)

| # | Question | Decision |
|---|---|---|
| Q1 | What does the reset wipe? | **Business data only.** All domain data (relations, fleet, drivers, tariffs, orders, trips, …) is deleted and the normal seed runs again. Users, roles, organisation units, settings, number-sequence definitions and the ISO reference data (countries, currencies) stay. |
| Q2 | Where is the action available? | **Every environment, permission-gated.** New host permission `Platform.Tenants.ResetToDataset`, and the tenant's name must be typed to confirm (checked again on the server). |
| Q3 | Where do the real addresses come from? | **Open address registers of NL, BE and DE** (Kadaster BAG, BOSA BeST, German state house-coordinate files), extracted once into an embedded resource. Companies and people are invented and paired with real buildings. |
| Q4 | What is "each known entity"? | **Master data + operational records.** Relations (with addresses, contacts, bank accounts), fleet, drivers, transport units, products, regions, tariffs, order templates, orders with stops and lines, trips and legs. ISO and reference lists (countries, currencies, VAT, order types, waypoint types, …) keep their seeded, realistic size. |

## Domain impact

- **Platform** (orchestration only, no domain knowledge): `ITestDatasetContributor` (Order, WipeAsync, GenerateAsync),
  `TestDatasetContext` (profile, one seeded `Random`, the address book, typed outputs), `TestDatasetProfile`,
  `TestDatasetWriter` (batched inserts, a unit of work per 1,000; hard delete with only the soft-delete filter off),
  `TestAddressRegister`/`TestAddressBook` (the embedded register, drawn without repeats in a 70/15/15 NL/BE/DE mix),
  `TestNames` (invented names; e-mail domains under the reserved `example.com`, so no generated record can reach a real
  mailbox; IBAN/VAT with valid check digits). `TestDatasetResetJob` (Hangfire), the app-service method
  `ITenantAdministrationAppService.ResetToDatasetAsync`, endpoint `POST api/platform/tenants/{id}/reset-to-dataset`.
  Platform's own contributor resets the number-sequence counters and derived-value states.
- **Every domain module** contributes its own part, like an `IDataSeedContributor`; Platform references none of them
  (module boundary rule 4). Order: Master Data 100, Financial 200, Orders 300, Planning & Execution 400. A module reads
  what an earlier one generated through a typed output in the publisher's `Domain.Shared/TestData`
  (`MasterDataTestDataset`, `FinancialTestDataset`, `OrdersTestDataset`), never another module's tables.
- No new tables. The reset's state lives in the tenant's extra properties (`DatasetName`, `DatasetResetState`,
  `DatasetResetAt`, `DatasetResetError`, `DatasetResetRequestId`), like the provisioning state.
- Architecture: no flow of `docs/architecture/02-flows.md` is extended; this is a host tool outside the command → fact
  → event path. No AD item is built or relied on.

## Behaviour

1. Right-click a tenant → **Reset to dataset** → *Average volume test set* or *Large volume test set*. Shown only with
   the permission, for a provisioned tenant (or one from before the Tenants app), disabled while a reset is queued or
   running.
2. The confirm dialog names the dataset, warns that the business data is deleted for good, and stays disabled until
   the tenant's name is typed. The server checks the name, the dataset, the provisioning state and that no reset is
   running, then marks the tenant *Queued* and queues the job after commit.
3. The job, in the tenant's context: wipe (descending Order) → seed → generate (ascending Order). It records *Running*,
   then *Succeeded* or *Failed* with the exception type and message. The list's **Test data** column shows the dataset
   and its state and refreshes itself while a reset is pending.

| Profile | Relations | Other master-data types | Orders | Order dates |
|---|---|---|---|---|
| Average | 5,000 | 25–250 each (random) | 4,000 | today −60 … +14 days |
| Large | 150,000 | 1,000–2,500 each (random) | 40,000 | today −180 … +30 days |

The random generator is seeded per profile, so the same dataset comes out the same every time (apart from "today").

## Guarantees

- **Tenant isolation (rule 1):** the job runs under `CurrentTenant.Change(tenant)`; every delete runs through the
  repository with the tenant filter on, so a shared-tier tenant in the host database loses only its own rows.
- **Idempotent (rule 3):** a run always starts by wiping, so a redelivery or a rerun after a failure converges; a
  redelivery of a finished request id does nothing. A failed run is not rethrown (no Hangfire retry overlapping a new
  reset started from *Failed*).
- **No PII in logs (rule 5):** tenant id, dataset and counts only. The job's arguments are the tenant id, the dataset
  name and a request id.
- **Logbook:** the job and the contributors are `[DisableAuditing]`; generated records do not fill the functional
  logbook.
- **Not atomic:** each batch commits on its own so memory stays flat for 150,000 relations. A failed run leaves part of
  a dataset; the state says *Failed* and the next reset wipes it.

## Address data

`backend/modules/platform/OpenTms.Platform.Domain/TestData/Addresses/addresses.csv.gz`, built by
`scripts/testdata/build-address-extract.py`; sources, licences and attribution in `SOURCES.md` next to it.
OpenStreetMap data is deliberately not used (ODbL share-alike).

## Out of scope

- Generated users, invoices and purchase settlements beyond what the modules model today.
- Choosing the size freely; the two datasets are fixed profiles.
- Derived values (cost/revenue per leg) are not calculated during the run; see the Planning contributor for what is
  requested.
