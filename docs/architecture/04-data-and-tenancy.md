# 04 — Data and tenancy

The tenancy model itself is in `/CLAUDE.md` ("Multi-tenancy model") and is **BUILT**: a host
database plus a database per tenant, a schema per module, one migrator for all of them.
This file adds the data design principles from the sessions and what tenancy means at
500–1,000 tenants.

## Data design principles

These come from three problems in the current OpenTMS that the rebuild must not repeat:
costs calculated on every grid load, thousands of queries per screen through lazy loading,
and documents stored in database tables.

### Relational first, JSONB for document-shaped parts (DECIDED, AD-06)

Orders, trips and invoices are relational: one trip references many orders, one invoice
aggregates many orders, and a change ripples through them. They get proper tables,
constraints and transactions. A document store was considered and rejected.

`jsonb` columns on the same row hold what really is a document: the raw incoming message,
customer-specific attributes, an agent's reasoning trace.

### Never calculate on read (DECIDED, AD-07)

Allocated cost, CO₂ and similar derived values are **stored**, per leg, and written by a
calculation service when something relevant changes. A grid reads stored values.

- They are ordinary columns written by application code, not database computed columns:
  the value of one leg depends on the trip total, the vehicle and the other legs.
- Triggers are few: trip composition changed, a rate changed, actual costs came in.
- The calculation runs in the background and the result is pushed to the browser
  (flow 2 in `02-flows.md`). An earlier idea to calculate synchronously on planning changes
  is superseded by this.
- Store provenance with the number: which rate, which distance, which emission factor, when,
  and for which trip version. For CO₂ that is required for reporting; for the operating
  system it is the rationale behind a decision.
- The calculation is idempotent and can be re-run for a whole trip.

### One rate-line model (DECIDED, AD-08)

Money lives in rate lines; facts live on the order line, leg, stop or trip.

- A rate line is anchored to exactly one of: order line, leg, stop, trip. Waiting hours
  belong to a stop, a kilometre rate to a leg.
- A rate line has a side: cost or revenue.
- A **fixed price is a rate line too**, anchored to the order or the trip and marked as
  overriding the sum beneath it. The total of a level is that line if it exists, otherwise
  the sum of the children. There is no "fixed or calculated" switch on the header.
- Under a fixed price the detail lines stay as the breakdown. They no longer count toward the
  total, but they are the key for spreading the fixed amount over the parts.
- Expected cost has one source: the trip-level cost. Allocation runs from the trip downwards.

The physical shape (one table with one nullable reference per anchor and a check that
exactly one is set) is **PROPOSED** (AP-07).

### Tension with the module boundaries (DECIDED, AD-20; was AO-11)

The sessions spoke of "a rate line with a foreign key to the leg" and "cost stored on the
leg". In this repository rate lines belong to Financial, legs to Planning & Execution and
order lines to Orders, and rule 1 of `/CLAUDE.md` forbids cross-schema keys and joins.

So an anchor is an **id without a database constraint**, and "a grid reads one table" needs
a decision: either Planning & Execution keeps a copy of the allocated values on its legs,
updated from a Financial event, or the grid reads a read model. **Decided on 2026-10-25 (AD-20): Planning &
Execution stores the allocated values on its legs, written by its calculator from Financial's published rate-line
contract (design 56).**

### Query discipline

Already in the coding conventions, repeated here because the old system's performance
problem was exactly this: every collection query is paged, navigations are loaded
explicitly, and a new query ships its index in the same migration.

### Documents live in Cloud Storage (DECIDED, AD-09; not built)

A document (order PDF, CMR, proof of delivery) is stored in Cloud Storage. The database
keeps a reference: bucket, object key, size, content type, hash. The browser fetches the
document straight from storage with a signed URL; it never streams through the API.

Today `Volo.Abp.BlobStoring.Database` is wired, which stores blobs in the host database.
ABP ships a Google Cloud Storage provider. Until the switch is made through a design,
do not add new blob containers that assume the database provider stays.

## Tenancy at 500–1,000 tenants

The PO's position (AD-03): a database per tenant plus a host database stays, firmly, for
strict separation. It has to scale from 100 to 1,000 tenants, and keeping it healthy is
treated as continuous operations work, not a one-off setting.

### Connections are the constraint

A connection belongs to one database, so connections cannot be shared between tenants. The
.NET driver keeps a pool per database per instance. Instances × active tenants × pool size
must stay under the PostgreSQL limit, and without measures it does not.

What a design must do:

- Keep transactions short. Nothing waits on an external call inside a transaction.
- Never loop over tenants inside a request. Work across tenants belongs in a background job
  that takes one tenant at a time.
- Write high-volume data in batches (ingest, `07-ingest.md`).
- State under *Cost & SLO impact* when a feature adds a process that holds its own
  connections, or a job that touches every tenant database.

What the platform will add (not built):

| Measure | Status |
|---|---|
| A connection pooler between all roles and PostgreSQL | **DECIDED** that one is needed (AD-10); which one is **OPEN** (AO-06): PgBouncer on a small VM, or Cloud SQL's managed pooling, which requires the Enterprise Plus edition |
| A pool budget as configuration: a ceiling per role and instance, derived from the database limit | **PROPOSED** (AP-08) |
| An alert at 70% of the connection limit, split per database | **PROPOSED** (AP-08) |
| Agents hold no database connection; they send commands through the Api role | **PROPOSED** (AP-04) |

A pooler in transaction mode forbids session settings and session-level locks. Whether
Hangfire and EF Core migrations work through it is untested; migrations should connect
directly either way.

### Growing past one database server (PROPOSED, AP-09)

Each tenant already has its own connection string. Kept complete per tenant, that makes
spreading tenants over several PostgreSQL instances a backup, a restore and one changed row.
Nothing should be built that assumes all tenant databases sit on one server.

### What a database per tenant costs elsewhere

- **The outbox is per database**, so the dispatcher has to visit tenants (`03`).
- **Migrations run per tenant database.** The DbMigrator handles them one after another and
  continues past a failure. At a thousand tenants a parallel runner with a failure report
  is needed (**PROPOSED**, AP-10).
- **No query spans tenants.** Cross-tenant reporting, metering and support views need their
  own store or their own job. Disabling the tenant filter is not the way (constitution rule 1).
- **Provisioning a tenant** (create database and role, migrate, seed) is a background job, started from
  the host-only Tenants app (`docs/designs/tenants-and-editions.md`). It holds one connection per
  step and never runs in a request.

### The host database

Holds the tenant store and connection strings, ABP's framework tables for the host, and the
Hangfire storage (schema `hangfire`). Metering data is **PROPOSED** to live here too (AP-05): host-owned rows that carry a tenant id
as a plain column, so cross-tenant reads need no tenant-filter bypass (constitution rule 1).
It also holds the module schemas for shared-tier tenants, as `/CLAUDE.md` describes.
