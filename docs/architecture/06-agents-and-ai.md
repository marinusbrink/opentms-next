# 06 — Agents and AI

Nothing in this file is built. The concept is the PO's and is **DECIDED**; most of the
mechanics are **PROPOSED** or **OPEN**. Check `decisions.md` before relying on a detail.

## The concept (DECIDED, AD-01, AD-04, AD-05)

A classic TMS is a system of record that people fill through screens. The logistics operating
system makes it a system of work: agents do the operational work (intake, pricing, planning,
dispatch, invoicing) within **policies** the customer defines, and people handle what the
agents hand back, in an **exception inbox**.

- The TMS core stays the record of **facts**.
- The operating system owns **decisions and their reasons**: why this trip went to this vehicle.
- It runs on this TMS. Connecting it to another vendor's TMS later is a theoretical option,
  not a design goal.

## Two kinds of agents

| | Boundary agents | Decision agents |
|---|---|---|
| Belong to | The TMS (an input channel, like the EDI adapter) | The operating system |
| Triggered by | Something arriving from outside | A domain event |
| Job | Translate: unstructured input in, a command out | Decide the next operational step |
| Examples | Email + PDF → order; carrier invoice PDF → match proposal; photo → proof of delivery | Order created → price; priced → plan under policy; invoice mismatch → approve or escalate |
| Judged on | Extraction accuracy, and knowing when to say "not sure" | The customer's policies |
| Autonomy ladder | Does not apply | Applies |
| When wrong | A wrong **fact**; caught at intake | A wrong **decision** on correct facts; caught in the exception inbox |

Keeping them apart is what makes a failure traceable to one of the two.

A monitor agent that only raises an exception ("this trip is running late") is a decision
agent at the lowest autonomy level.

**Unsure means a draft.** When a boundary agent is not confident, it creates a draft: a fact
with a confidence state that a person confirms. Orders therefore need a notion of an
unconfirmed order. The operating system treats it like any other exception.

## Policies, autonomy ladder, exception inbox

- A **policy** is a rule the customer sets that bounds what an agent may do by itself.
- The **autonomy ladder** sets, per process and per client, how far an agent goes: from
  "propose everything" to "act alone". Trust is built step by step. Whether "client" means
  the tenant or the tenant's own customers is **OPEN** (AO-13).
- The **exception inbox** is the person's workplace: everything an agent did not decide.
- A decision is stored **with its reason**, next to the fact it produced (the reasoning trace
  is one of the `jsonb` uses in `04-data-and-tenancy.md`).

At autonomy level zero a customer has the classic TMS and an empty inbox. Same product,
different trust level.

### What is not settled

- **No domain owns this** (**OPEN**, AO-02). Policies, autonomy levels, exceptions and
  decision records have no home in the domain map. Adding a domain is a PO decision. A design
  must not park them in another module to get on.
- **The app** (**DECIDED**, AD-19, 2026-10-10): the operating system is a separate app named
  **LOS**, which runs the planning department through AI agents and workflows: the inbox, the
  policy settings and the ladder in one app. The sessions' working name "Steering" is retired.
  Its tile and route arrive with its first design; the Chat app stays open (AO-04).
- **How an exception reaches the inbox** (**PROPOSED**, AP-03): as a command that records it,
  so the inbox is a view on records in the core and has no channel of its own.

## Rules for any agent design

- An agent reaches the core only through application services, with a tenant and
  permissions like any client. It never touches a database.
- An agent never calls another agent. The next step follows from the next event.
- Agent behavior is user-visible behavior: it sits behind a feature flag, default off, per
  tenant (constitution rule 4). The autonomy ladder refines that; it does not replace it.
- **Token cost is a design criterion** (constitution rule 6). State the expected model calls
  per order and their cost under *Cost & SLO impact*. Intake of a messy PDF was estimated in
  the sessions at a few cents to roughly fifteen cents per document; treat that as an
  estimate to verify, not a figure.
- **Sending tenant data to a model API is processing of that data** (constitution rule 5).
  An order PDF holds names and addresses. A design states which data leaves, to whom, and
  answers the privacy questions.
- A model's output is input, not truth: everything it produces passes the same validation as
  a person's entry.

## Metering (PROPOSED, AP-05)

Every agent run is recorded with its tenant, its agent and its token cost, from the first
agent on. The PO needs margin per order and wants the autonomous layer priced on use, apart
from the base subscription. The PO's cost rule: document intake by boundary agents counts as
a cost of the TMS; decisions count as a cost of the operating system.

## MCP server and in-app chat (PROPOSED, AP-01, AP-02)

The PO wants to explore both directions; neither is decided.

- **TMS as a source in an AI platform** (Claude, ChatGPT, Gemini): one MCP server on top of
  the application services. Read tools are queries, write tools are the same commands as
  everywhere else. Sign-in is per user through OAuth, so the tenant and permissions apply.
- **Chat inside the TMS**: the same tools, with the model call made by us. The chat is in
  effect a decision agent steered by a person, under the same policies.

If either is designed:

- No free-form querying by a model. A small set of fixed read tools on stored values.
- Text from ingested documents reaches the model through read tools, so prompt injection is
  expected. Write tools always pass validation and a confirmation step.
- Start read-only.
- In the first direction the customer's own AI subscription pays for tokens; in the second
  we do, and it must be metered.
