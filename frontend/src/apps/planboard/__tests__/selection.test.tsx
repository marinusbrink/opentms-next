import { act, renderHook } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

/* The stored selection is per tenant (design 74 §5): the provider reads the tenant from the application configuration. */
let config: { currentTenant?: { id?: string | null } } | undefined = { currentTenant: { id: "tenant-a" } };
vi.mock("@/lib/abp/queries", () => ({ useApplicationConfiguration: () => ({ data: config }) }));

import { PlanboardSelectionProvider, readStoredSelection, selectionTransitions, usePlanboardSelection } from "@/apps/planboard/selection";

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

const wrapper = ({ children }: { children: ReactNode }) => <PlanboardSelectionProvider>{children}</PlanboardSelectionProvider>;

beforeEach(() => {
  config = { currentTenant: { id: "tenant-a" } };
  sessionStorage.clear();
});

describe("PlanboardSelectionProvider", () => {

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

/* Design 74 §5, as built: the focus and the selected stop survive the round trip to a record page in `sessionStorage`, under a
 * key per tenant, so a browser tab that switches tenant does not carry one tenant's trip id over to the next. Risk class:
 * Critical for the negative tests (tenant isolation, in the browser's storage), Medium for the rest. */
describe("the stored selection", () => {
  const keyOf = (tenant: string) => `opentms.planboard.selection.${tenant}`;
  const store = (tenant: string, focusedTripId: string, selectedStopId: string | null = null) => sessionStorage.setItem(keyOf(tenant), JSON.stringify({ focusedTripId, selectedStopId }));
  const asTenant = (id: string | null) => {
    config = { currentTenant: { id } };
  };

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("survives leaving the Planboard and coming back: the provider unmounts and mounts again", () => {
    const first = renderHook(() => usePlanboardSelection(), { wrapper });
    act(() => first.result.current.setSelectedTripIds(["t1"]));
    act(() => first.result.current.setSelectedStopId("s1"));
    first.unmount();

    const second = renderHook(() => usePlanboardSelection(), { wrapper });
    expect(second.result.current).toMatchObject({ focusedTripId: "t1", selectedStopId: "s1" });
    /* The selections themselves are not stored: only the focus and the stop. */
    expect(second.result.current.selectedTripIds).toEqual([]);
  });

  it("tenant A's stored selection is not restored for tenant B", () => {
    const tenantA = renderHook(() => usePlanboardSelection(), { wrapper });
    act(() => tenantA.result.current.setSelectedTripIds(["trip-of-a"]));
    act(() => tenantA.result.current.setSelectedStopId("stop-of-a"));
    tenantA.unmount();

    asTenant("tenant-b");
    const tenantB = renderHook(() => usePlanboardSelection(), { wrapper });
    expect(tenantB.result.current).toMatchObject({ focusedTripId: null, selectedStopId: null, selectedTripIds: [] });
    expect(sessionStorage.getItem(keyOf("tenant-b"))).toBeNull();
  });

  it("tenant B starting up leaves tenant A's stored selection alone, and A gets it back", () => {
    store("tenant-a", "trip-of-a", "stop-of-a");
    asTenant("tenant-b");
    const tenantB = renderHook(() => usePlanboardSelection(), { wrapper });
    act(() => tenantB.result.current.setSelectedTripIds(["trip-of-b"]));
    tenantB.unmount();
    expect(JSON.parse(sessionStorage.getItem(keyOf("tenant-a"))!)).toEqual({ focusedTripId: "trip-of-a", selectedStopId: "stop-of-a" });
    expect(JSON.parse(sessionStorage.getItem(keyOf("tenant-b"))!)).toMatchObject({ focusedTripId: "trip-of-b" });

    asTenant("tenant-a");
    const backToA = renderHook(() => usePlanboardSelection(), { wrapper });
    expect(backToA.result.current).toMatchObject({ focusedTripId: "trip-of-a", selectedStopId: "stop-of-a" });
  });

  it("the host has its own key: a tenant's selection is not the host's and the other way round", () => {
    store("tenant-a", "trip-of-a");
    expect(readStoredSelection(null)).toEqual({ focusedTripId: null, selectedStopId: null });
    expect(readStoredSelection(undefined)).toEqual({ focusedTripId: null, selectedStopId: null });
    store("host", "host-trip");
    expect(readStoredSelection("tenant-a").focusedTripId).toBe("trip-of-a");
    expect(readStoredSelection("tenant-b").focusedTripId).toBeNull();
    expect(readStoredSelection(null).focusedTripId).toBe("host-trip");
  });

  it("the configuration arriving after the mount does not give a tenant another tenant's selection", () => {
    store("tenant-b", "trip-of-b");
    config = undefined;
    const { result, rerender } = renderHook(() => usePlanboardSelection(), { wrapper });
    asTenant("tenant-a");
    rerender();
    expect(result.current.focusedTripId).toBeNull();
    expect(JSON.parse(sessionStorage.getItem(keyOf("tenant-b"))!)).toMatchObject({ focusedTripId: "trip-of-b" });
  });

  /* A full page load (a reload, and every tenant switch: session-switch.ts assigns window.location) mounts the Planboard before
   * the application configuration is known: AppShell does not wait for it. */
  it("the tenant's stored selection survives a page load, when the configuration arrives after the mount", () => {
    store("tenant-a", "trip-of-a", "stop-of-a");
    config = undefined;
    const { result, rerender } = renderHook(() => usePlanboardSelection(), { wrapper });
    asTenant("tenant-a");
    rerender();
    expect(result.current).toMatchObject({ focusedTripId: "trip-of-a", selectedStopId: "stop-of-a" });
    expect(JSON.parse(sessionStorage.getItem(keyOf("tenant-a"))!)).toEqual({ focusedTripId: "trip-of-a", selectedStopId: "stop-of-a" });
  });

  it("clearing the focus removes the stored selection", () => {
    store("tenant-a", "t1", "s1");
    const { result } = renderHook(() => usePlanboardSelection(), { wrapper });
    act(() => result.current.clearSelection());
    expect(sessionStorage.getItem(keyOf("tenant-a"))).toBeNull();
  });

  it("a stored stop without a focused trip is no selection", () => {
    sessionStorage.setItem(keyOf("tenant-a"), JSON.stringify({ selectedStopId: "s1" }));
    expect(readStoredSelection("tenant-a")).toEqual({ focusedTripId: null, selectedStopId: null });
  });

  it("corrupt content and values of the wrong type are ignored", () => {
    sessionStorage.setItem(keyOf("tenant-a"), "{not json");
    expect(readStoredSelection("tenant-a")).toEqual({ focusedTripId: null, selectedStopId: null });
    sessionStorage.setItem(keyOf("tenant-a"), JSON.stringify({ focusedTripId: 5, selectedStopId: 6 }));
    expect(readStoredSelection("tenant-a")).toEqual({ focusedTripId: null, selectedStopId: null });
    sessionStorage.setItem(keyOf("tenant-a"), JSON.stringify({ focusedTripId: "t1", selectedStopId: 6 }));
    expect(readStoredSelection("tenant-a")).toEqual({ focusedTripId: "t1", selectedStopId: null });
  });

  it("storage that is off breaks nothing: the selection simply does not survive", () => {
    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
      throw new Error("denied");
    });
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new Error("denied");
    });
    const { result } = renderHook(() => usePlanboardSelection(), { wrapper });
    expect(result.current.focusedTripId).toBeNull();
    act(() => result.current.setSelectedTripIds(["t1"]));
    expect(result.current.focusedTripId).toBe("t1");
  });

  it("an `initial` selection wins over what is stored", () => {
    store("tenant-a", "stored");
    const withInitial = ({ children }: { children: ReactNode }) => <PlanboardSelectionProvider initial={{ focusedTripId: "given" }}>{children}</PlanboardSelectionProvider>;
    const { result } = renderHook(() => usePlanboardSelection(), { wrapper: withInitial });
    expect(result.current.focusedTripId).toBe("given");
  });
});
