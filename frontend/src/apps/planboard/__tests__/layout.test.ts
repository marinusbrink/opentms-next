import { describe, expect, it } from "vitest";

import { DEFAULT_SCHEDULER_ID, defaultLayoutPlan, type PlannedPanel } from "@/apps/planboard/layout";
import { newPaneInstanceId, PLANBOARD_PANE_TYPES, PLANBOARD_PANES, type PlanboardPaneType } from "@/apps/planboard/panes/registry";

/* The default workspace layout (design 70, "Workspace"): Trips left top with Stops and Legs as background tabs, the
 * tractor Scheduler below, Legs to schedule right top, the Map below it; Transport orders and Resources are not in it;
 * a pane the tenant lacks is left out and the others still find a valid anchor. And the registry derived from
 * apps.config.ts. Risk class: Medium. */

const all = () => true;
const without = (...off: PlanboardPaneType[]) => (type: PlanboardPaneType) => !off.includes(type);

/** Every reference points at an earlier panel of the plan; the first has no position. */
function assertWellFormed(plan: PlannedPanel[]) {
  const seen = new Set<string>();
  plan.forEach((panel, index) => {
    if (index === 0) expect(panel.position).toBeUndefined();
    else expect(seen.has(panel.position!.referencePanel), `${panel.id} → ${panel.position!.referencePanel}`).toBe(true);
    expect(panel.params.instanceId).toBe(panel.id);
    seen.add(panel.id);
  });
}

describe("defaultLayoutPlan", () => {
  it("lays out the six default panes with every feature on", () => {
    const plan = defaultLayoutPlan(all);
    expect(plan.map((p) => [p.id, p.params.type, p.position?.referencePanel ?? null, p.position?.direction ?? null, !!p.inactive])).toEqual([
      ["trips", "trips", null, null, false],
      ["stops", "stops", "trips", "within", true],
      ["legs", "legs", "trips", "within", true],
      [DEFAULT_SCHEDULER_ID, "scheduler", "trips", "below", false],
      ["legs-to-schedule", "legs-to-schedule", "trips", "right", false],
      ["map", "map", "legs-to-schedule", "below", false],
    ]);
    expect(plan.find((p) => p.id === DEFAULT_SCHEDULER_ID)!.params.initial).toEqual({ mode: "tractor" });
    expect(plan.some((p) => p.params.type === "orders" || p.params.type === "resources")).toBe(false);
    assertWellFormed(plan);
  });

  it("leaves out a disabled pane and re-anchors the rest", () => {
    const noTrips = defaultLayoutPlan(without("trips"));
    expect(noTrips.map((p) => p.id)).toEqual(["stops", "legs", DEFAULT_SCHEDULER_ID, "legs-to-schedule", "map"]);
    expect(noTrips[1].position).toEqual({ referencePanel: "stops", direction: "within" });
    expect(noTrips[2].position).toEqual({ referencePanel: "stops", direction: "below" });
    assertWellFormed(noTrips);

    const noLegsToSchedule = defaultLayoutPlan(without("legs-to-schedule"));
    expect(noLegsToSchedule.find((p) => p.id === "map")!.position).toEqual({ referencePanel: "trips", direction: "right" });
    assertWellFormed(noLegsToSchedule);
  });

  it("anchors a lone right-hand pane to the first panel when the whole left column is off", () => {
    const plan = defaultLayoutPlan(without("trips", "stops", "legs", "scheduler"));
    expect(plan.map((p) => p.id)).toEqual(["legs-to-schedule", "map"]);
    expect(plan[0].position).toBeUndefined();
    expect(plan[1].position).toEqual({ referencePanel: "legs-to-schedule", direction: "below" });

    const onlyMap = defaultLayoutPlan((t) => t === "map");
    expect(onlyMap).toHaveLength(1);
    expect(onlyMap[0].position).toBeUndefined();
  });

  it("is empty when every pane is off", () => {
    expect(defaultLayoutPlan(() => false)).toEqual([]);
  });

  it("any subset of enabled panes yields a well-formed plan", () => {
    const optional: PlanboardPaneType[] = ["trips", "stops", "legs", "scheduler", "legs-to-schedule", "map"];
    for (let mask = 0; mask < 1 << optional.length; mask++) {
      const enabled = new Set(optional.filter((_, i) => (mask & (1 << i)) !== 0));
      const plan = defaultLayoutPlan((t) => enabled.has(t));
      expect(plan.map((p) => p.params.type).sort()).toEqual([...enabled].sort());
      assertWellFormed(plan);
    }
  });
});

describe("pane registry", () => {
  it("takes feature and title of each pane from the Planboard nav nodes; only the Scheduler may open more than once", () => {
    for (const type of PLANBOARD_PANE_TYPES) {
      expect(PLANBOARD_PANES[type].feature).toMatch(/^Apps\.Planboard\.[A-Za-z]+$/);
      expect(PLANBOARD_PANES[type].titleKey).toMatch(/^App:Planboard\./);
      expect(PLANBOARD_PANES[type].multiple).toBe(type === "scheduler");
    }
    expect(PLANBOARD_PANES["legs-to-schedule"].feature).toBe("Apps.Planboard.LegsToSchedule");
  });

  it("gives a single pane its type as id and a Scheduler a fresh, valid settings id", () => {
    expect(newPaneInstanceId("map")).toBe("map");
    const a = newPaneInstanceId("scheduler", [DEFAULT_SCHEDULER_ID]);
    expect(a).toMatch(/^scheduler-[a-z0-9]+$/);
    expect(a).not.toBe(DEFAULT_SCHEDULER_ID);
    expect(`planboard.pane.${a}`).toMatch(/^[a-zA-Z0-9_.-]{1,100}$/);
  });
});
