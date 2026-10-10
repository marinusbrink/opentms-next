# Decisions, proposals and open points

The register behind the other files. Cite an id in a design instead of restating the point.
Status labels are defined in `README.md`.

Dates are the session in which the PO decided. "Detail" names the file that explains it.

## Decided (AD)

| Id | Decision | Date | Built? | Detail |
|---|---|---|---|---|
| AD-01 | The core accepts commands, writes facts and publishes events. It reads no input source and takes no operational decision. Facts belong to the TMS, decisions and their reasons to the operating system. | 2026-10-08 | Structure yes, behavior no | `01` |
| AD-02 | One ABP backend as a modular monolith. No service per domain. Module boundaries strict enough that splitting one off later is a deployment decision. | 2026-10-08 | Yes | `01`, `/CLAUDE.md` |
| AD-03 | A database per tenant plus a host database, firmly, for strict separation. It must scale to 100–1,000 tenants; keeping it healthy is ongoing operations work. | 2026-10-09 | Yes | `04` |
| AD-04 | Two kinds of agents. Boundary agents translate outside input into commands and belong to the TMS. Decision agents act on events under policies and are the operating system. | 2026-10-08 | No | `06` |
| AD-05 | The operating system is an app in the app bar and a client of the same core, with no access of its own to the database. Its workplace is an exception inbox; policies and an autonomy ladder bound the agents. | 2026-10-08 | No | `06` |
| AD-06 | Relational tables for orders, trips and invoices; `jsonb` on the same row for document-shaped parts. A document store was rejected. | 2026-10-08 | n/a | `04` |
| AD-07 | Derived values (allocated cost, CO₂) are stored per leg and never calculated on read. A save returns at once; the calculation runs in the background and the result is pushed to the browser. | 2026-10-08 | No | `02`, `04` |
| AD-08 | One rate-line model: a rate line is anchored to an order line, leg, stop or trip. A fixed price is a rate line that overrides the sum beneath it. | 2026-10-08 | No | `04` |
| AD-09 | Documents live in Cloud Storage; the database keeps a reference. | 2026-10-08 | No: database blob storage is wired | `04` |
| AD-10 | Connections must be managed deliberately at this tenant count; a connection pooler sits between the application and PostgreSQL. | 2026-10-09 | No | `04` |
| AD-11 | One codebase and image, started in roles: Api, Ingest, Jobs, Agents. | 2026-10-09 | No | `05` |
| AD-12 | The routing engine is a separate service. | 2026-10-08 | No | `05` |
| AD-13 | Hangfire does the background computing, in the Jobs role on a fixed number of instances. | 2026-10-09 | Hangfire yes, role no | `05` |
| AD-14 | Google Pub/Sub is the event spine. Agents receive events as push and scale on them. | 2026-10-09 | No | `03`, `05` |
| AD-15 | Facts reported by the outside world enter through ingest, not through commands. Ingest applies no business rule; a rule runs after an event, in a module or agent. | 2026-10-09 | No | `02`, `07` |
| AD-16 | An event is written to an outbox in the same transaction and the same database as its fact: the tenant's own database. | 2026-10-09 | Yes (PR #33) | `03` |
| AD-17 | This architecture is built into opentms-next. No new repository. | 2026-10-09 | — | — |
| AD-18 | ABP's application services are the command layer. No separate command pipeline. (Follows from the department's "ABP conventions over own inventions".) | 2026-10-09 | Yes | `01` |
| AD-19 | The logistics operating system is a **separate app** in the app bar, named **LOS**: it runs the planning department through AI agents and workflows. It replaces the working name "Steering". The old "LOS" in the domain map's Reporting row is retired. | 2026-10-10 | No | `06`, `/CLAUDE.md` |

## Proposed, not confirmed by the PO (AP)

A design may use one of these only by naming it under *Assumptions*.

| Id | Proposal | Detail |
|---|---|---|
| AP-01 | An MCP server on the application services, so an AI platform can use the TMS as a source. | `06` |
| AP-02 | A chat inside the TMS that uses the same tools. | `06` |
| AP-03 | An exception is recorded through a command; the inbox is a view on those records. | `02`, `06` |
| AP-04 | Agents hold no database connection; they send commands through the Api role. | `04`, `05` |
| AP-05 | Every agent run is metered with tenant, agent and token cost; the data lives in the host database. | `06` |
| AP-06 | Event names follow `OpenTms.<Domain>.<FactInPastTense>`. | `03` |
| AP-07 | Rate lines in one table with one nullable reference per anchor and a check that exactly one is set. | `04` |
| AP-08 | A connection budget as configuration per role, and an alert at 70% of the limit per database. | `04` |
| AP-09 | Keep the full connection string per tenant so tenants can be spread over several PostgreSQL servers. | `04` |
| AP-10 | A parallel migration runner with a failure report, for hundreds of tenant databases. | `04` |
| AP-11 | Email is sent by a job from a status row written with the fact; delivery reports update that row. | `05` |
| AP-12 | The role is selected by an environment variable that decides which ABP modules load. | `05` |
| AP-13 | Redis (Memorystore) as SignalR backplane, distributed cache, lock provider and outbox signal. | `05` |
| AP-14 | Ingest writes through "sink" interfaces published by the owning module. | `07` |
| AP-15 | Events raised by ingest go through the outbox. | `07` |
| AP-16 | Vendor adapters for ingest live in the Integrations domain. | `07` |
| AP-17 | The mapping of the starter events onto emitting domains. | `03` |

## Open: a PO decision (AO)

A design that depends on one of these reports it on the issue and stops.

| Id | Open point | Detail |
|---|---|---|
| AO-01 | The contract: which commands the core offers, which events it emits, which read tools an MCP server gets. | `03`, `06` |
| AO-02 | Which domain owns policies, autonomy levels, exceptions and decision records. None in the domain map does. | `06` |
| AO-03 | The change to module boundary rule 2 in `/CLAUDE.md` (distributed events through the outbox next to the local event bus). **Decided by the PO on 2026-10-10 by instructing the build; takes effect when PR #33 merges.** | `03` |
| AO-04 | New apps in the app list: ~~Steering~~ (decided: the LOS app, AD-19) and Chat. | `06` |
| AO-05 | Which language-model vendor and models. | `05` |
| AO-06 | Which connection pooler: PgBouncer on a small VM, or Cloud SQL's managed pooling (Enterprise Plus edition). | `04` |
| AO-07 | Making the outbox dispatcher fit for 500–1,000 tenants: sweep and signals apart, several tenants at a time, batches, a cross-process signal and lock. | `03` |
| AO-08 | Cloud Tasks instead of Hangfire for one-off work. | `05` |
| AO-09 | The fixed list of report types that become an event. | `07` |
| AO-10 | What happens to an event that keeps failing (no dead-letter store exists). | `03` |
| AO-11 | Rate lines and allocated values across module boundaries: anchors cannot be foreign keys, and "the grid reads one table" needs either a copy in Planning & Execution or a read model. | `04` |
| AO-12 | Which domain owns vehicle positions and email status. | `07` |
| AO-13 | Whether "per client" in the autonomy ladder means the tenant or the tenant's own customers. | `06` |

## Superseded: do not use

Earlier statements from the sessions that were replaced. Listed so that nobody revives them.

| What was said | Replaced by |
|---|---|
| A `Tms.*` solution with one project per module, a `Tms.Contracts` assembly and a custom command pipeline | The existing structure: five projects per module, `Domain.Shared` and `Application.Contracts` as the published surface, application services as the command layer (AD-18) |
| Modules named Orders, Planning, Tariffs, Invoicing, Fleet, MasterData, Messaging, Steering | The PO's domain map in `/CLAUDE.md` |
| Reasoning based on classic ABP (ASP.NET Boilerplate), for example about its job manager | ABP 10.6 (abp.io): it has an outbox and a lock-aware job worker built in |
| "Trip recalculated" reaches the browser through the event spine | Through Redis to the SignalR hub (AP-13) |
| Cost allocation runs synchronously on a planning change | Background calculation with push (AD-07) |
| The intake agent starts as a module inside the core | Agents are their own role (AD-11) |
| A payment notification is a command | It is ingest; Financial reacts to the event (AD-15) |
| Outside input split into "asks" and "reports" | "To interpret" and "to record": a report can lead to a business rule too (AD-15) |
| A new repository for the foundation | opentms-next (AD-17) |
