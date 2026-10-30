# MasterData (frontend domain folder)

Relations, addresses, vehicles, drivers.

This folder mirrors the backend `OpenTms.MasterData` module (see the domain map in `/CLAUDE.md`).
Everything the frontend knows about this domain lives here: query hooks, mutations, and
domain-specific components.

Rules:

- Data access only through the generated typed client (`@/lib/api/client`) wrapped in
  TanStack Query hooks defined in this folder — components never fetch directly.
- Apps (`src/apps/`) are views: they COMPOSE from domain folders and never own domain
  logic. Which apps read from this domain is defined in `src/app/apps.config.ts`.
- Cross-domain composition happens in apps, not by importing between domain folders. The one exception: the record
  editors in `components/` that two apps share (CLAUDE.md: "dialogs two apps share") read other domains' query hooks
  for their pickers and open other domains' editors through `domains/master-data/components/create-in-place.tsx`
  ("Add new …", UI-LOOK-004).
