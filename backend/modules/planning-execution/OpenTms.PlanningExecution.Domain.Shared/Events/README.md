# Planning & Execution — published events

Integration events (ETOs) published by the PlanningExecution module live here. They are part of the
module's **published surface**: other modules may reference this project (`Domain.Shared`)
and subscribe via the local event bus — never the module's `Domain`, `Application`, or
`EntityFrameworkCore` projects. See the architecture tests and `CLAUDE.md`.
