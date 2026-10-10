# Bespoke screens (UI-BSP)

**Load for:** the planboard, maps, Gantt / scheduler, dashboards, portals, integration landing pages.

These screens follow no general page pattern in the old app ([../ui-inventory.md](../ui-inventory.md) §12), and the general rules do not describe their layout. **Their layout comes from their own design doc.** The rules below say which general rules still apply.

---

### UI-BSP-001 · MUST · General rules that still apply to bespoke screens
Even on a bespoke screen, these files apply in full:
- [tokens.md](tokens.md)
- [feedback.md](feedback.md)
- [format.md](format.md)
- [permissions.md](permissions.md)
- [keyboard.md](keyboard.md)
- action icons and labels: UI-ACT-004 to UI-ACT-007
- opening records: UI-DLG-007 (records open as the standard editor dialog over the bespoke screen)

Exempt are page anatomy (UI-PAGE-001/002) and grid layout, unless the screen embeds a normal list.
- **Check:** the design doc of a bespoke screen lists which UI rules it does not follow, as `DEVIATION(UI-…)` lines.
- **Evidence:** inventory §12 (the planboard opens TO / TF through the same global dialogs; it uses the same toasts and formatting).

### UI-BSP-002 · MUST · Grids embedded in bespoke screens are normal grids
A grid inside the planboard, a portal or a map panel follows [grid.md](grid.md) in full. Only the layout around it is bespoke.
- **Check:** embedded grids use `OpenTmsGrid` with their own `gridId`.
- **Evidence:** inventory §2.3, §12 (planboard tabs reuse the standard transport-file / order / leg grids).

### UI-BSP-003 · MUST · Portal screens are for external users and get the portal adaptations
Self-service, carrier and distribution portals:
- use an action column (UI-GRID-009) instead of relying on right-click;
- keep data tables usable at `md` width.
- **Evidence:** inventory §3.6 (action columns only in portals), §1.8.

## Not covered by any rule (needs its own design)

| Screen | Why it is not covered | Library choice |
|---|---|---|
| Planboard workspace (docking tabs, persisted layout, live updates) | unique layout | GAP-07 |
| Vehicle planboard Gantt; resource calendar | timeline interaction | GAP-11 |
| Vehicle outline (drag-and-drop board) | unique interaction | (none yet) |
| Planboard map, Track & Trace, LOS | map UI | GAP-12 |
| Dashboards, sensor charts | charts | GAP-06 |
| PDF import wizard (PDF beside extracted order) | split document view | GAP-10 |
| Proof-of-delivery signature | canvas | GAP-09 |
| Integration landing pages (Chainfill, BigMile, Route42, TrackOnline) | marketing-style pages | (none) |
| Onboarding wizard (pre-shell) | outside the shell | (none) |
