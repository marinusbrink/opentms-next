# 05 — Runtime and deployment

## Where the code stands

One process does everything: `OpenTms.HttpApi.Host` serves the API, hosts the OpenIddict
auth server and runs the Hangfire server. `OpenTms.DbMigrator` is the only other executable.
There is no Redis. Deployment to GCP is Terraform in `infra/` (PO decision 2026-10-30,
`docs/designs/gcp-infrastructure.md`): staging and prod, each its own project, the Api role only.

## One image, four roles (DECIDED, AD-11; not built)

The backend stays one codebase and one container image. It is started in one of four roles,
each its own Cloud Run service. The core is not split into services per domain: the outbox
needs fact and event in one transaction, and cost allocation, rate lines and invoice
matching form one transactional chain.

| Role | Does | Scales | Database use |
|---|---|---|---|
| **Api** | Application services for the browser and the agents, auth server, SignalR hub, MCP server | With requests | A small pool per tenant |
| **Ingest** | Receives reports from outside and records them (`07-ingest.md`) | With incoming webhooks | Writes in batches |
| **Jobs** | Hangfire server, outbox dispatcher, recurring work | Fixed number of instances | Visits tenants one at a time |
| **Agents** | Boundary and decision agents; receives events as Pub/Sub push | With the event stream, down to zero | None (**PROPOSED**, AP-04): sends commands through Api |

How a role is selected (an environment variable that decides which ABP modules load) is
**PROPOSED** (AP-12). Today only `Outbox:DispatchEnabled` exists as a per-instance switch,
in PR #33.

The **routing engine** (distances, drive times) is a separate service, not a role of this
image (AD-12).

Why agents are their own role: they are slow and bursty because of the model calls. In the
Api role they would set the planner's response time.

## Background work

- **Hangfire is the job engine** behind ABP's `IBackgroundJobManager`. **BUILT**; its storage
  is the `hangfire` schema of the host database.
- **The Jobs role runs on a fixed number of instances** (AD-13) with CPU always allocated and
  at least one instance. Hangfire pulls its work from the database, so Cloud Run sees no
  requests and has nothing to scale on. One instance runs many jobs in parallel.
- **Whatever must scale with the event stream goes through Pub/Sub push**, not Hangfire
  (AD-14). Push turns every event into an HTTP request, which Cloud Run scales on.
- **Cloud Tasks instead of Hangfire for one-off work** (for example sending one email) is
  **OPEN** (AO-08).
- Every job is idempotent and restartable (constitution rule 3). Cloud Run can stop an
  instance in the middle of a job; the job then runs again.
- A job carries its tenant and a correlation id (coding conventions).

Pattern for sending email (**PROPOSED**, AP-11): the command writes an email row with status
"to send" in the same transaction as the fact; a job sends it and records the vendor's
message id; the job checks the status before sending and passes a fixed key to the mail
service where the service supports one. Delivery reports come back through ingest and update
the same row, which makes it the send log.

## More than one instance

The moment any role runs on more than one instance, four things need a shared answer.
None is built.

| Concern | Problem | Answer |
|---|---|---|
| SignalR | A browser is connected to one Api instance; "trip recalculated" comes from the Jobs role | A backplane (Redis) |
| Cache | ABP's cache is per instance by default; users would see stale master data | A distributed cache (Redis) |
| Locks | Outbox dispatching must not run twice (Hangfire already locks through its PostgreSQL storage) | A distributed lock provider (Redis) |
| Outbox signal | The Jobs role does not hear what the Api role committed | A cross-process signal (Redis) |

Redis (Memorystore on GCP) as the single answer to all four is **PROPOSED** (AP-13). It adds
a fixed monthly cost, which a design introducing it must state.

Two more follow from several instances and are already rules: migrations never run at
application startup, and connections are budgeted (`04-data-and-tenancy.md`).

## Real-time push (DECIDED, AD-07; not built)

The browser is told, not polled. A result computed in the background reaches open screens
through a SignalR hub in the Api role (flow 2). Validation that can reject a save stays
synchronous; everything else may arrive a moment later.

## External services

| Service | Use | Status |
|---|---|---|
| Google Pub/Sub | Event spine | **DECIDED**, not built |
| Cloud Storage | Documents | **DECIDED**, not built |
| Cloud SQL for PostgreSQL | Host and tenant databases | In use locally as plain PostgreSQL 17 |
| Redis / Memorystore | See above | **PROPOSED** |
| A language-model API | Boundary agents, decision agents, chat | **DECIDED** in principle; vendor and model not chosen |
| A mail service with delivery webhooks | Sending and status reports | **PROPOSED**; none chosen |

New service-to-service access uses Workload Identity with a specific role on a specific
resource (constitution rule 7).
