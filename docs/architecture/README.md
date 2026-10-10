# Solution architecture of opentms-next

This folder describes where opentms-next is heading: a TMS whose core only records facts,
with agents doing the operational work on top of it (the "logistics operating system").
It is written for the agents of the development department, the architect first.

- **Last updated:** 2026-10-10 (applied to the repository the same day; the outbox statuses
  were corrected to what is built in PR #33)
- **Source:** architecture working sessions between the PO and Claude, 8–9 October 2026
- **Owner:** the PO. Agents propose changes through a design or the retrospective route;
  they never edit this folder as part of a feature, with one exception: **the design PR that
  builds a DECIDED item flips that item's status** (the "Built?" column in `decisions.md` and
  the status in `01-overview.md`), so the folder stays true without a separate PO round.

## What this is, and what it is not

It is the **target** architecture plus an honest account of how far the code is. Most of it
is not built. Do not read a description here as "this exists": every element carries a status.

It is **not** a backlog. A design builds what its PBI asks for and nothing else. Use this
folder to make that design fit the target, not to pull extra work into it.

## Status labels

| Label | Meaning | What a design may do with it |
|---|---|---|
| **BUILT** | In the codebase. The text says where. | Rely on it. |
| **DECIDED** | The PO decided it. Not built yet. | Rely on the decision and build toward it. |
| **PROPOSED** | Suggested in the sessions, never confirmed by the PO. | Use it only as an explicit line under *Assumptions*, so gate 1 sees it. |
| **OPEN** | Undecided. A PO decision. | Do not decide it. If the design depends on it, report on the issue and stop. |

`decisions.md` lists every decision, proposal and open point with an id (`AD-…`, `AP-…`,
`AO-…`). Cite those ids in a design instead of restating them.

## Authority

1. The SaaS constitution and the department conventions.
2. `/CLAUDE.md` of this repository: domain map, module boundary rules, tenancy model.
3. This folder.

Where this folder seems to conflict with 1 or 2, the higher one wins. Stop and report the
conflict on the issue; do not pick a side silently.

## Which file to read when

Read `01-overview.md` for any design that goes beyond a single screen or endpoint. Then read
only what the PBI touches:

| The PBI touches… | Read |
|---|---|
| The overall shape, which component does what | `01-overview.md` |
| How a request, a save or an outside report travels through the system | `02-flows.md` |
| Events between modules, anything another module or an agent reacts to | `03-events-and-outbox.md` |
| Entities, rates, calculated values, documents, tenants, connections | `04-data-and-tenancy.md` |
| Background work, real-time push, hosting, scaling, caching | `05-runtime-and-deployment.md` |
| Agents, policies, autonomy, exceptions, chat, MCP, token cost | `06-agents-and-ai.md` |
| Data arriving from outside: on-board computers, mail status, EDI, payments | `07-ingest.md` |
| Whether something was decided, proposed or is still open | `decisions.md` |
| A Dutch domain term in a PBI | `glossary.md` |
