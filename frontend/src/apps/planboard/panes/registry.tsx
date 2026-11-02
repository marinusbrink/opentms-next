import { lazy, type ComponentType, type LazyExoticComponent } from "react";
import type { LucideIcon } from "lucide-react";

import { APPS } from "@/app/apps.config";

/* The pane registry of the Planboard (design 70): pane type → lazy component, feature, title and icon. The feature,
 * the title key and the icon come from the app's nav nodes in apps.config.ts (each node links with `?pane=<type>`), so
 * the nav pane, the Add pane menu and the dockview tabs cannot drift apart. Each pane is its own lazy chunk. */

export const PLANBOARD_PANE_TYPES = ["trips", "scheduler", "orders", "stops", "legs", "legs-to-schedule", "map", "resources"] as const;
export type PlanboardPaneType = (typeof PLANBOARD_PANE_TYPES)[number];

/** The props every pane component receives from the workspace. */
export interface PlanboardPaneProps {
  /** The dockview panel id; also the key of the pane's settings (`planboard.pane.<instanceId>`). */
  instanceId: string;
  type: PlanboardPaneType;
  /** Settings the layout gave a new instance (the default layout's Scheduler: `{ mode: "tractor" }`). */
  initial?: Readonly<Record<string, unknown>>;
}

export interface PlanboardPaneDefinition {
  type: PlanboardPaneType;
  /** `Apps.Planboard.<Pane>`: the pane is offered and rendered only while it (and its ancestors) are on. */
  feature: string;
  /** `App:Planboard.<Pane>`, the nav node's name; also the tab title. */
  titleKey: string;
  icon: LucideIcon;
  /** The Scheduler can be added many times; every other pane once. */
  multiple: boolean;
  component: LazyExoticComponent<ComponentType<PlanboardPaneProps>>;
}

const components: Record<PlanboardPaneType, LazyExoticComponent<ComponentType<PlanboardPaneProps>>> = {
  trips: lazy(() => import("@/apps/planboard/panes/TripsPane")),
  scheduler: lazy(() => import("@/apps/planboard/panes/SchedulerPane")),
  orders: lazy(() => import("@/apps/planboard/panes/OrdersPane")),
  stops: lazy(() => import("@/apps/planboard/panes/StopsPane")),
  legs: lazy(() => import("@/apps/planboard/panes/LegsPane")),
  "legs-to-schedule": lazy(() => import("@/apps/planboard/panes/LegsToSchedulePane")),
  map: lazy(() => import("@/apps/planboard/panes/MapPane")),
  resources: lazy(() => import("@/apps/planboard/panes/ResourcesPane")),
};

export const PANE_SEARCH_KEY = "pane";

function definitionsFromConfig(): Record<PlanboardPaneType, PlanboardPaneDefinition> {
  const app = APPS.find((a) => a.id === "planboard");
  const result = {} as Record<PlanboardPaneType, PlanboardPaneDefinition>;
  for (const type of PLANBOARD_PANE_TYPES) {
    const view = app?.views?.find((v) => v.search?.[PANE_SEARCH_KEY] === type);
    if (!view?.feature) throw new Error(`apps.config.ts: the Planboard has no nav node with ?pane=${type} and a feature`);
    result[type] = { type, feature: view.feature, titleKey: view.nameKey, icon: view.icon, multiple: type === "scheduler", component: components[type] };
  }
  return result;
}

export const PLANBOARD_PANES: Readonly<Record<PlanboardPaneType, PlanboardPaneDefinition>> = definitionsFromConfig();

export function isPlanboardPaneType(value: unknown): value is PlanboardPaneType {
  return typeof value === "string" && (PLANBOARD_PANE_TYPES as readonly string[]).includes(value);
}

/** A new panel id: the type itself for a single-instance pane, `scheduler-<random>` for the Scheduler. */
export function newPaneInstanceId(type: PlanboardPaneType, existingIds: readonly string[] = []): string {
  if (!PLANBOARD_PANES[type].multiple) return type;
  const taken = new Set(existingIds);
  for (;;) {
    const id = `${type}-${Math.random().toString(36).slice(2, 10)}`;
    if (!taken.has(id)) return id;
  }
}
