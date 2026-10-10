# 02 — Flows

Three flows carry the whole architecture. Every design that adds behavior should be able to
say which of these it extends. Statuses are in `01-overview.md`; most steps are not built.

## Flow 1 — The agent loop

How work gets done without a person: a signal comes in, the core records a fact, an agent
decides the next step.

1. **A signal arrives.** For example an email with an order PDF. It is not a fact yet.
2. **A boundary agent interprets it and sends a command.** "Create this transport order."
   When the agent is unsure, the result is a *draft order*: a fact with a confidence state,
   which a person confirms. The boundary agent decides what the input *means*; it decides
   nothing about the operation.
3. **The core validates, writes the fact and writes the event** to the outbox, in one
   transaction (`03-events-and-outbox.md`).
4. **The dispatcher publishes the event** on the event spine.
5. **Decision agents receive the event.** "Order created."
6. **A decision agent sends a new command**, within the customer's policies and autonomy
   level: price it, plan it, approve it, or hand it to a person as an exception. The loop
   continues at step 3.

What follows for a design:

- A decision agent never writes to the database and never calls another agent. It sends a
  command, and the next thing happens because of the next event.
- "Hand it to a person" is itself a command that records an exception; the exception inbox
  is a view on those records. Nothing reaches the inbox around the core. (**PROPOSED**, AP-03.)
- The loop only works for events that exist. Adding an agent reaction usually means adding
  or reusing an event first.

## Flow 2 — Save without waiting

How a save stays fast while expensive work still happens. The motivating case: a trip with
four legs and eight stops; the planner changes the carrier, which changes the cost of every leg.

- **A. The planner saves.** The core writes that one change plus an event to the outbox and
  replies immediately. Only validation that could *reject* the save runs synchronously
  (for example: this carrier is not allowed for this customer).
- **B. The worker receives "trip changed"** and recalculates cost allocation and CO₂ for the
  whole trip.
- **C. The worker writes the result** as plain stored columns on each leg, with provenance.
  The trip carries a version number; if it moved on while the worker was calculating, the
  worker discards its result. A later job for the newer version will write.
- **D. The worker reports "trip recalculated".** It reaches the SignalR hub through Redis,
  and the hub pushes it to every screen that has the trip open. The grid refreshes those
  rows a second or two after the save.

What follows for a design:

- Anything that takes noticeable time is flow 2, never part of the request. The performance
  budget says the same: heavy operations are always asynchronous.
- The screen needs a visible "calculating" state on values that are being recalculated, so a
  planner does not act on stale numbers.
- Jobs are idempotent and can be re-run for a whole trip. That is also how a retroactive
  rate change is applied in bulk.
- Reads never calculate. A grid reads stored columns from one table.

## Flow 3 — A report from outside

How facts that the outside world reports get in. Example: a mail service reports that an
invoice email was marked as spam.

- **i. The report arrives** at the ingest service. It is already a fact; nothing needs
  interpreting.
- **ii. Ingest records it.** It verifies the signature, deduplicates on the vendor's id and
  writes the status. It applies no business rule.
- **iii. If this type of report is on the fixed list, an event goes to the spine.** "Spam
  reported" is on it; "email opened" or a single vehicle position is not.
- **iv. A module or agent applies the rule and sends a command.** For example: block this
  address for invoices. From there it is flow 1, steps 6 and 3.

What follows for a design:

- Volume is the reason this route exists. An on-board computer sends thousands of positions
  per vehicle per day; none of them should cost a command, an outbox record and an event.
- "Does a business rule follow?" does not decide the route. A report can lead to a rule. The
  rule just never runs in ingest: it runs after the event, in a module or agent.
- A payment notification is ingest too: it is recorded, "payment received" is published, and
  Financial marks the invoice paid with a command.

Details and rules: `07-ingest.md`.
