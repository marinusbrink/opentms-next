import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";

import { useApplicationConfiguration } from "@/lib/abp/queries";

/* The selection bus of the Planboard (design 70, "Selection bus"): the trips pane, the Scheduler's bars and the map's
 * markers share `selectedTripIds`; Legs to schedule and Legs share `selectedLegIds`. `focusedTripId` is the last single
 * selection: it changes only when exactly one trip is selected (or is set explicitly), so Stops, Legs and the map's
 * detail layer keep showing the last single trip while several are selected (P:Q16). `selectedStopId` is the stop of the
 * focused trip selected in Trip stops or on the map (two-way selection, design 70 "Map"); it clears when the focused
 * trip changes. Both survive the round trip to a record page and back (design 74 §5): they live in `sessionStorage`
 * while the provider is mounted and are restored when it mounts again. */

export interface PlanboardSelection {
  selectedTripIds: readonly string[];
  selectedLegIds: readonly string[];
  /** The last trip that was selected on its own; null until then. */
  focusedTripId: string | null;
  /** The selected stop of the focused trip (Trip stops ↔ map); null when none. */
  selectedStopId: string | null;
  /** Replaces the trip selection; exactly one id also makes it the focused trip. */
  setSelectedTripIds: (ids: readonly string[]) => void;
  setSelectedLegIds: (ids: readonly string[]) => void;
  /** Sets the focused trip directly (null clears it), e.g. when the focused trip was deleted. */
  setFocusedTripId: (id: string | null) => void;
  /** Selects a stop of the focused trip (null clears it). */
  setSelectedStopId: (id: string | null) => void;
  /** Clears both selections and the focused trip. */
  clearSelection: () => void;
}

interface SelectionState {
  selectedTripIds: readonly string[];
  selectedLegIds: readonly string[];
  focusedTripId: string | null;
  selectedStopId: string | null;
}

const EMPTY: SelectionState = { selectedTripIds: [], selectedLegIds: [], focusedTripId: null, selectedStopId: null };

const sameIds = (a: readonly string[], b: readonly string[]) => a.length === b.length && a.every((id, i) => id === b[i]);
const unique = (ids: readonly string[]) => [...new Set(ids.filter(Boolean))];

/** The pure transitions of the bus (exported for tests). */
export const selectionTransitions = {
  selectTrips(state: SelectionState, ids: readonly string[]): SelectionState {
    const next = unique(ids);
    const focusedTripId = next.length === 1 ? next[0] : state.focusedTripId;
    if (sameIds(next, state.selectedTripIds) && focusedTripId === state.focusedTripId) return state;
    const selectedStopId = focusedTripId === state.focusedTripId ? state.selectedStopId : null;
    return { ...state, selectedTripIds: next, focusedTripId, selectedStopId };
  },
  selectLegs(state: SelectionState, ids: readonly string[]): SelectionState {
    const next = unique(ids);
    return sameIds(next, state.selectedLegIds) ? state : { ...state, selectedLegIds: next };
  },
  focusTrip(state: SelectionState, id: string | null): SelectionState {
    return state.focusedTripId === id ? state : { ...state, focusedTripId: id, selectedStopId: null };
  },
  selectStop(state: SelectionState, id: string | null): SelectionState {
    return state.selectedStopId === id ? state : { ...state, selectedStopId: id };
  },
};

/** Per tenant: a browser tab that switches tenant must not carry one tenant's trip id over to the next. */
const selectionStorageKey = (tenantId: string | null | undefined) => `opentms.planboard.selection.${tenantId ?? "host"}`;

/** The stored focus of this browser tab; empty when there is none or storage is not available (exported for tests). */
export function readStoredSelection(tenantId: string | null | undefined): Pick<SelectionState, "focusedTripId" | "selectedStopId"> {
  try {
    const stored = JSON.parse(sessionStorage.getItem(selectionStorageKey(tenantId)) ?? "{}") as Record<string, unknown>;
    const focusedTripId = typeof stored.focusedTripId === "string" ? stored.focusedTripId : null;
    return { focusedTripId, selectedStopId: focusedTripId && typeof stored.selectedStopId === "string" ? stored.selectedStopId : null };
  } catch {
    return { focusedTripId: null, selectedStopId: null };
  }
}

function writeStoredSelection(tenantId: string | null | undefined, focusedTripId: string | null, selectedStopId: string | null) {
  try {
    if (focusedTripId) sessionStorage.setItem(selectionStorageKey(tenantId), JSON.stringify({ focusedTripId, selectedStopId }));
    else sessionStorage.removeItem(selectionStorageKey(tenantId));
  } catch {
    /* Storage is off or full: the selection then simply does not survive leaving the page. */
  }
}

const PlanboardSelectionContext = createContext<PlanboardSelection | null>(null);

export function PlanboardSelectionProvider({ children, initial }: { children: ReactNode; initial?: Partial<SelectionState> }) {
  const configuration = useApplicationConfiguration().data;
  const tenantId = configuration?.currentTenant?.id;
  const known = configuration != null;
  const [state, setState] = useState<SelectionState>(() => ({ ...EMPTY, ...initial }));

  /* The tenant is only known once the configuration has loaded (a page load does not wait for it): until then nothing is
   * read or written, because the storage key depends on it. */
  const [restored, setRestored] = useState(initial != null);
  useEffect(() => {
    if (!known || restored) return;
    setRestored(true);
    const stored = readStoredSelection(tenantId);
    setState((s) => (s.focusedTripId ? s : { ...s, ...stored }));
  }, [known, restored, tenantId]);

  useEffect(() => {
    if (known && restored) writeStoredSelection(tenantId, state.focusedTripId, state.selectedStopId);
  }, [known, restored, tenantId, state.focusedTripId, state.selectedStopId]);

  const setSelectedTripIds = useCallback((ids: readonly string[]) => setState((s) => selectionTransitions.selectTrips(s, ids)), []);
  const setSelectedLegIds = useCallback((ids: readonly string[]) => setState((s) => selectionTransitions.selectLegs(s, ids)), []);
  const setFocusedTripId = useCallback((id: string | null) => setState((s) => selectionTransitions.focusTrip(s, id)), []);
  const setSelectedStopId = useCallback((id: string | null) => setState((s) => selectionTransitions.selectStop(s, id)), []);
  const clearSelection = useCallback(() => setState(EMPTY), []);

  const value = useMemo<PlanboardSelection>(
    () => ({ ...state, setSelectedTripIds, setSelectedLegIds, setFocusedTripId, setSelectedStopId, clearSelection }),
    [state, setSelectedTripIds, setSelectedLegIds, setFocusedTripId, setSelectedStopId, clearSelection],
  );
  return <PlanboardSelectionContext.Provider value={value}>{children}</PlanboardSelectionContext.Provider>;
}

export function usePlanboardSelection(): PlanboardSelection {
  const value = useContext(PlanboardSelectionContext);
  if (!value) throw new Error("usePlanboardSelection needs a PlanboardSelectionProvider (the Planboard workspace).");
  return value;
}

export { PlanboardSelectionContext };
