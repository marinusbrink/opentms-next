import { act, renderHook } from "@testing-library/react";
import type { ReactNode } from "react";
import { describe, expect, it } from "vitest";

import { PlanboardSelectionProvider, selectionTransitions, usePlanboardSelection } from "@/apps/planboard/selection";

/* The Planboard's selection bus (design 70, "Selection bus"; P:Q16): the focused trip follows a single selection only,
 * a multi-selection keeps the last single trip focused, the selected stop clears when the focused trip changes, and
 * unchanged selections return the same state (no re-render storms between panes). Risk class: Medium. */

const EMPTY = { selectedTripIds: [] as string[], selectedLegIds: [] as string[], focusedTripId: null as string | null, selectedStopId: null as string | null };

describe("selectionTransitions", () => {
  it("a single selected trip becomes the focused trip", () => {
    const next = selectionTransitions.selectTrips(EMPTY, ["t1"]);
    expect(next).toMatchObject({ selectedTripIds: ["t1"], focusedTripId: "t1" });
  });

  it("several selected trips keep the last single trip focused", () => {
    const one = selectionTransitions.selectTrips(EMPTY, ["t1"]);
    const many = selectionTransitions.selectTrips(one, ["t1", "t2", "t3"]);
    expect(many).toMatchObject({ selectedTripIds: ["t1", "t2", "t3"], focusedTripId: "t1" });
    const none = selectionTransitions.selectTrips(many, []);
    expect(none).toMatchObject({ selectedTripIds: [], focusedTripId: "t1" });
  });

  it("a multi-selection from nothing focuses nothing", () => {
    expect(selectionTransitions.selectTrips(EMPTY, ["t1", "t2"]).focusedTripId).toBeNull();
  });

  it("dedupes ids and ignores empty ones (a duplicated single id still focuses it)", () => {
    expect(selectionTransitions.selectTrips(EMPTY, ["t1", "t1", ""])).toMatchObject({ selectedTripIds: ["t1"], focusedTripId: "t1" });
    expect(selectionTransitions.selectLegs(EMPTY, ["l1", "l2", "l1"]).selectedLegIds).toEqual(["l1", "l2"]);
  });

  it("clears the selected stop when the focused trip changes, keeps it otherwise", () => {
    const focused = { ...EMPTY, selectedTripIds: ["t1"], focusedTripId: "t1", selectedStopId: "s1" };
    expect(selectionTransitions.selectTrips(focused, ["t1", "t2"]).selectedStopId).toBe("s1");
    expect(selectionTransitions.selectTrips(focused, ["t2"])).toMatchObject({ focusedTripId: "t2", selectedStopId: null });
    expect(selectionTransitions.focusTrip(focused, "t3")).toMatchObject({ focusedTripId: "t3", selectedStopId: null });
    expect(selectionTransitions.focusTrip(focused, null)).toMatchObject({ focusedTripId: null, selectedStopId: null });
  });

  it("returns the same state object when nothing changes", () => {
    const state = { ...EMPTY, selectedTripIds: ["t1"], selectedLegIds: ["l1"], focusedTripId: "t1", selectedStopId: "s1" };
    expect(selectionTransitions.selectTrips(state, ["t1"])).toBe(state);
    expect(selectionTransitions.selectLegs(state, ["l1"])).toBe(state);
    expect(selectionTransitions.focusTrip(state, "t1")).toBe(state);
    expect(selectionTransitions.selectStop(state, "s1")).toBe(state);
  });

  it("legs and trips are independent selections", () => {
    const state = selectionTransitions.selectLegs(selectionTransitions.selectTrips(EMPTY, ["t1"]), ["l1"]);
    expect(selectionTransitions.selectTrips(state, ["t2"]).selectedLegIds).toEqual(["l1"]);
    expect(selectionTransitions.selectLegs(state, []).focusedTripId).toBe("t1");
  });
});

describe("PlanboardSelectionProvider", () => {
  const wrapper = ({ children }: { children: ReactNode }) => <PlanboardSelectionProvider>{children}</PlanboardSelectionProvider>;

  it("wires the transitions and clears everything on clearSelection", () => {
    const { result } = renderHook(() => usePlanboardSelection(), { wrapper });
    act(() => result.current.setSelectedTripIds(["t1"]));
    act(() => result.current.setSelectedStopId("s1"));
    act(() => result.current.setSelectedLegIds(["l1"]));
    expect(result.current).toMatchObject({ selectedTripIds: ["t1"], focusedTripId: "t1", selectedStopId: "s1", selectedLegIds: ["l1"] });
    act(() => result.current.clearSelection());
    expect(result.current).toMatchObject({ selectedTripIds: [], focusedTripId: null, selectedStopId: null, selectedLegIds: [] });
  });

  it("throws outside the workspace", () => {
    expect(() => renderHook(() => usePlanboardSelection())).toThrow(/PlanboardSelectionProvider/);
  });
});
