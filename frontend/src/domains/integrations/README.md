# Integrations (frontend domain folder)

EDI, OTM5, accounting links, partner links; anti-corruption layer.

This folder mirrors the backend `OpenTms.Integrations` module (see the domain map in `/CLAUDE.md`).
Everything the frontend knows about this domain lives here: query hooks, mutations, and
domain-specific components.

Rules:

- Data access only through the generated typed client (`@/lib/api/client`) wrapped in
  TanStack Query hooks defined in this folder — components never fetch directly.
- Apps (`src/apps/`) are views: they COMPOSE from domain folders and never own domain
  logic. Which apps read from this domain is defined in `src/app/apps.config.ts`.
- Cross-domain composition happens in apps, not by importing between domain folders.
