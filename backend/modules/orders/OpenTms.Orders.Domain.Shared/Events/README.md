# Orders — published events

Integration events (ETOs) published by the Orders module live here. They are part of the
module's **published surface**: other modules may reference this project (`Domain.Shared`)
and subscribe via the distributed event bus (through the transactional outbox, at least once)
or the local event bus (same transaction) — never the module's `Domain`, `Application`, or
`EntityFrameworkCore` projects. See the architecture tests and `CLAUDE.md`.
