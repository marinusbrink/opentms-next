import type { DockviewApi, SerializedDockview } from "dockview-react";

import { isPlanboardPaneType, PLANBOARD_PANES, type PlanboardPaneType } from "@/apps/planboard/panes/registry";

/* The workspace layout (design 70, "Workspace"): the default arrangement, and the stored form of dockview's toJSON()
 * under `planboard.layout`. A stored layout that is corrupt or of another version falls back to the default; a pane
 * whose feature is off stays in a stored layout and shows FeatureUnavailable there. */

export const LAYOUT_VERSION = 1;
/** The single dockview component every panel uses; the pane type travels in the panel's params. */
export const PANE_COMPONENT = "planboard-pane";
export const DEFAULT_SCHEDULER_ID = "scheduler-default";

export interface PlanboardPanelParams {
  type: PlanboardPaneType;
  instanceId: string;
  initial?: Record<string, unknown>;
}

export interface StoredLayout {
  version: typeof LAYOUT_VERSION;
  dockview: SerializedDockview;
}

export function toStoredLayout(dockview: SerializedDockview): StoredLayout {
  return { version: LAYOUT_VERSION, dockview };
}

/** The dockview state of a stored layout, or null when it is absent, corrupt or outdated (→ the default layout). */
export function parseStoredLayout(value: unknown): SerializedDockview | null {
  if (value == null || typeof value !== "object") return null;
  const stored = value as Partial<StoredLayout>;
  if (stored.version !== LAYOUT_VERSION) return null;
  const dockview = stored.dockview as Partial<SerializedDockview> | undefined;
  if (!dockview || typeof dockview !== "object") return null;
  const grid = dockview.grid as Partial<SerializedDockview["grid"]> | undefined;
  if (!grid || typeof grid !== "object" || grid.root == null || typeof grid.root !== "object") return null;
  const panels = dockview.panels;
  if (!panels || typeof panels !== "object" || Array.isArray(panels)) return null;
  const entries = Object.entries(panels);
  if (entries.length === 0) return null;
  const seenSingles = new Set<string>();
  for (const [id, panel] of entries) {
    if (!panel || typeof panel !== "object" || panel.id !== id) return null;
    if (panel.contentComponent !== PANE_COMPONENT) return null;
    const params = panel.params as Partial<PlanboardPanelParams> | undefined;
    if (!params || !isPlanboardPaneType(params.type) || params.instanceId !== id) return null;
    if (!PLANBOARD_PANES[params.type].multiple) {
      if (seenSingles.has(params.type)) return null;
      seenSingles.add(params.type);
    }
  }
  return dockview as SerializedDockview;
}

type Direction = "left" | "right" | "above" | "below" | "within";

export interface PlannedPanel {
  id: string;
  params: PlanboardPanelParams;
  /** Relative to an earlier panel of the plan; absent for the first panel. */
  position?: { referencePanel: string; direction: Direction };
  /** Added as a background tab (the tab next to Trips stays on Trips). */
  inactive?: boolean;
}

/**
 * The default layout (design 70): Trips left top with Stops and Legs as tabs next to it, the tractor Scheduler left
 * bottom, Legs to schedule right top, the Map right bottom. Panes the tenant lacks are left out and the rest moves up;
 * Transport orders and Resources are not in the default layout (Add pane). Pure, so it is testable without dockview.
 */
export function defaultLayoutPlan(isEnabled: (type: PlanboardPaneType) => boolean): PlannedPanel[] {
  const plan: PlannedPanel[] = [];
  const ids = new Set<string>();
  const add = (type: PlanboardPaneType, id: string, anchors: { ref: string; direction: Direction }[], extra?: { inactive?: boolean; initial?: Record<string, unknown> }) => {
    if (!isEnabled(type)) return;
    const anchor = anchors.find((a) => ids.has(a.ref));
    plan.push({
      id,
      params: { type, instanceId: id, ...(extra?.initial ? { initial: extra.initial } : {}) },
      position: plan.length === 0 ? undefined : anchor ? { referencePanel: anchor.ref, direction: anchor.direction } : { referencePanel: plan[0].id, direction: "right" },
      inactive: extra?.inactive,
    });
    ids.add(id);
  };

  add("trips", "trips", []);
  add("stops", "stops", [{ ref: "trips", direction: "within" }], { inactive: true });
  add("legs", "legs", [{ ref: "trips", direction: "within" }, { ref: "stops", direction: "within" }], { inactive: true });
  const leftTop = [{ ref: "trips", direction: "below" as const }, { ref: "stops", direction: "below" as const }, { ref: "legs", direction: "below" as const }];
  add("scheduler", DEFAULT_SCHEDULER_ID, leftTop, { initial: { mode: "tractor" } });
  const rightOfLeft = ["trips", "stops", "legs", DEFAULT_SCHEDULER_ID].map((ref) => ({ ref, direction: "right" as const }));
  add("legs-to-schedule", "legs-to-schedule", rightOfLeft);
  add("map", "map", [{ ref: "legs-to-schedule", direction: "below" }, ...rightOfLeft]);
  return plan;
}

/** Adds the planned panels to an empty dockview. */
export function applyLayoutPlan(api: DockviewApi, plan: readonly PlannedPanel[], titleOf: (type: PlanboardPaneType) => string): void {
  for (const panel of plan) {
    api.addPanel({
      id: panel.id,
      component: PANE_COMPONENT,
      title: titleOf(panel.params.type),
      params: panel.params,
      inactive: panel.inactive,
      ...(panel.position ? { position: panel.position } : {}),
    });
  }
}

/** Retitles every panel in the user's language (a stored layout carries the titles of the language it was saved in). */
export function retitlePanels(api: DockviewApi, titleOf: (type: PlanboardPaneType) => string): void {
  for (const panel of api.panels) {
    const type = (panel.params as Partial<PlanboardPanelParams> | undefined)?.type;
    if (isPlanboardPaneType(type)) panel.api.setTitle(titleOf(type));
  }
}
