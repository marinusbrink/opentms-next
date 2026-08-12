# Orders (frontend domain folder)

Order intake via all channels and the order lifecycle.

This folder mirrors the backend `OpenTms.Orders` module (see the domain map in `/CLAUDE.md`).
Everything the frontend knows about this domain lives here: query hooks, mutations, and
domain-specific components.

Rules:

- Data access only through the generated typed client (`@/lib/api/client`) wrapped in
  TanStack Query hooks defined in this folder — components never fetch directly.
- Apps (`src/apps/`) are views: they COMPOSE from domain folders and never own domain
  logic. Which apps read from this domain is defined in `src/app/apps.config.ts`.
- Cross-domain composition happens in apps, not by importing between domain folders.
