# 07 — Ingest

Ingest is the route for facts the outside world reports. The pattern is **DECIDED** (AD-15);
nothing is built.

## Why it is a separate route

Some input is already a fact and arrives in volume. An on-board computer sends thousands of
positions per vehicle per day. Sent through the command route, each would cost a
transaction, an outbox record and an event that no one reacts to. Ingest records such input
directly and only raises an event when it matters.

## What goes through ingest

| Source | Reports | Named by |
|---|---|---|
| On-board computer | Vehicle positions | the PO |
| Mail service | Delivered, opened, bounced, marked as spam | the PO |
| EDI partner | Acknowledgements | Claude (**PROPOSED**) |
| Payment provider | Payment notifications | Claude (**PROPOSED**) |

What does **not** go through ingest: anything that must be interpreted first. An order PDF,
a carrier's invoice and a proof-of-delivery photo go through a boundary agent and become a
command (`06-agents-and-ai.md`). A proof of delivery is a borderline case, kept on the
command side because an agent has to read the photo and the order status changes with it.

## What the ingest service does

1. Receives a webhook, or polls the vendor.
2. Knows the vendor's format. It is the only place that does (an anti-corruption layer).
3. Verifies the signature.
4. Deduplicates on the vendor's own id.
5. Writes the fact, in batches where volume asks for it.
6. For report types on a **fixed list**, raises an event.

What it never does: apply a business rule, call a model, or decide anything. "Block this
address for invoices after a spam report" is a rule; it runs in a module or agent that
reacts to the event, and it changes data through a command (flow 3 in `02-flows.md`).

## Rules for a design

- **Ingest writes through an interface the owning module publishes**, never straight into
  another module's tables and never through the public application services. The sessions
  called these "sinks" (for example a position sink, an email-status sink). Their shape is
  **PROPOSED** (AP-14).
- **The fixed list decides what becomes an event** (**OPEN**, AO-09). The list is
  configuration per report type, so ingest holds no judgment. Known so far: bounce and spam
  yes; opened no; a single position no.
- **Events from ingest go through the outbox** like any other, in the tenant's database
  (**PROPOSED**, AP-15; the sessions only said "onto the spine").
- **Every write is idempotent.** Vendors resend. The vendor's id is the key
  (constitution rule 3).
- **The tenant comes from the endpoint or the credentials, never from the payload alone.**
  A webhook is unauthenticated input until its signature is verified.
- **Positions are personal data** when a vehicle has one driver. Answer the privacy
  questions at design time (constitution rule 5) and state retention.
- **Derived events are not ingest's job.** "Vehicle arrived at the stop" or "trip is running
  late" is computed from positions by a module or the monitor agent, not by ingest.

## Where it lives

- **Runtime:** its own role, Ingest, so a flood of positions never slows the API
  (`05-runtime-and-deployment.md`).
- **Code:** the vendor adapters in the Integrations domain, which the domain map already
  describes as the anti-corruption layer (**PROPOSED**, AP-16).
- **Data:** each fact in the schema of the domain that owns it. Which domain owns positions
  (Planning & Execution has "realtime status" and "track & trace"; Master Data has vehicles)
  and email status (Platform has mail) is **OPEN** (AO-12).
