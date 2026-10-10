# 03 — Events and the outbox

Events are how the core tells the rest of the system that something changed. They are the
public interface to the agent layer, so they are few, well named and stable.

## Where the code stands

- The transactional outbox and its dispatcher are built and tested in **PR #33**
  (`feat/outbox-per-tenant`), together with the `/CLAUDE.md` section "Events and the outbox"
  and the change to module boundary rule 2 (open point **AO-03**, decided by merging that PR).
- No module publishes an event yet; the first events arrive with their own designs (AO-01).

The full design of what was built is [`docs/designs/outbox-per-tenant.md`](../designs/outbox-per-tenant.md).

## Two buses, two guarantees

| Bus | Guarantee | Use it for |
|---|---|---|
| `IDistributedEventBus` | Written to the outbox in the **same transaction and the same database as the fact**. Published afterwards, **at least once**, in the tenant's context. | Every event another module, another runtime role or an agent reacts to. |
| `ILocalEventBus` | In-process, inside the current transaction. Nothing survives a crash. | A reaction that belongs to the same transaction. |

## How the outbox works (AD-16; BUILT, PR #33)

- The table is `platform."AbpEventOutbox"`, present in the host database and in every
  tenant database. A tenant's event is stored in that tenant's own database. A shared-tier
  tenant (no own database) uses the host database.
- A module publishes with `IDistributedEventBus.PublishAsync(eto)` from an application
  service method that changes data. Nothing else is needed. The row is enqueued at publish
  time, under the publisher's current tenant, inside the publisher's unit of work.
- **Publishing outside a transactional unit of work throws.** The fact would already be
  committed without its event. The `useOutbox: false` parameter is ignored for `OpenTms.*` events.
- Only events whose type sits in an `OpenTms.*` namespace use the outbox. ABP's own events
  (identity, tenant management, entity-change events) keep their direct in-process behavior.
- Each stored event carries the tenant it was raised for. Handlers run in that tenant's
  context, also for shared-tier tenants.
- A dispatcher publishes the stored events and deletes them. It is told which tenant just
  committed (a signal) and sweeps every database every five minutes as a safety net.
- ABP's own outbox sender is switched off: it only reads the host database, so tenant events
  would never be sent. Never switch it back on.
- Unit tests on SQLite bypass the outbox and get their events delivered directly.

## What a design must respect

- **Handlers are idempotent.** Delivery is at least once (constitution rule 3). Give an event
  a natural key the handler can deduplicate on; a handler does not receive the outbox id.
- **Name the emitting and the consuming domain** for every event, as the design template asks.
- **An event states a fact in the past tense** and carries ids, not copies of data. No names,
  addresses or licence plates in a payload that is not strictly needed (constitution rule 5).
- **Do not emit everything.** Add an event when something reacts to it. Events are hard to
  take back once agents depend on them.
- **ETOs live in the publisher's `Domain.Shared/Events`.** A consumer references that project,
  never the publisher's `Domain` or `Application`.
- **Never read or write the outbox table yourself.**

Naming convention (**PROPOSED**, AP-06): event name `OpenTms.<Domain>.<FactInPastTense>`,
for example `OpenTms.Orders.OrderCreated`.

## Starter list of events

Named in the sessions as the first events worth having. None exists in code. The list is a
starting point, not a contract; the contract is open point **AO-01**. The emitting domains
were mapped onto the domain map afterwards and are **PROPOSED** (AP-17).

| Event | Emitted by | First consumers |
|---|---|---|
| Order created | Orders | pricing agent; Planning & Execution |
| Order changed | Orders | Planning & Execution; Financial |
| Trip composed | Planning & Execution | dispatch agent; cost allocation |
| Trip changed | Planning & Execution | cost allocation (flow 2) |
| Trip executed | Planning & Execution | Financial (invoicing) |
| Trip recalculated | Financial or Planning & Execution (**OPEN**, AO-11) | the browser, through SignalR |
| Invoice ready | Financial | invoice agent |
| Payment received | Integrations (from ingest) | Financial |
| Mail bounced / spam reported | Platform (from ingest) | monitor agent; the owning domain |
| Agent run completed | the agent runtime | metering |

## The event spine (DECIDED, AD-14; not built)

Google Pub/Sub is the spine. Today the dispatcher hands an event to handlers in the same
process; the target is that it publishes to Pub/Sub instead.

- ABP ships no Pub/Sub provider (it has Azure Service Bus, RabbitMQ, Kafka and Rebus). A
  provider or publisher has to be written.
- The stored event id becomes the Pub/Sub message id, so a consumer can deduplicate.
- The tenant id travels as a message attribute.
- Agents receive events as **push** subscriptions: each event is an HTTP request to the
  Agents role, which lets Cloud Run scale that role by itself, down to zero.

## Known limits of the dispatcher

The dispatcher is correct but not yet built for 500–1,000 tenants. Not measured; this
follows from the code.

1. The full sweep and the signals share one loop: during a sweep, new events wait.
2. Databases are handled one after another: one slow tenant database delays the rest.
3. Events are published and deleted one at a time.
4. A sweep opens every tenant database, which works against small connection pools (`04`).
5. The signal only works inside one process. After the role split the Jobs role will not
   hear what the Api role committed (needs Redis).
6. The lock that stops two instances from draining the same outbox is in-process (one lock
   per database, keyed on the resolved connection string).
7. An event that keeps failing blocks the events behind it in that one database. There is no
   dead-letter store.

None of this changes the table or how modules publish. They are open points **AO-07** and
**AO-10**.
