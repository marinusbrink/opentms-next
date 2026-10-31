import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from "react";

/* The selection bus of the Planboard (design 70, "Selection bus"): the trips pane, the Scheduler's bars and the map's
 * markers share `selectedTripIds`; Legs to schedule and Legs share `selectedLegIds`. `focusedTripId` is the last single
 * selection: it changes only when exactly one trip is selected (or is set explicitly), so Stops, Legs and the map's
 * detail layer keep showing the last single trip while several are selected (P:Q16). `selectedStopId` is the stop of the
 * focused trip selected in Trip stops or on the map (two-way selection, design 70 "Map"); it clears when the focused
 * trip changes. */

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

const PlanboardSelectionContext = createContext<PlanboardSelection | null>(null);

export function PlanboardSelectionProvider({ children, initial = EMPTY }: { children: ReactNode; initial?: Partial<SelectionState> }) {
  const [state, setState] = useState<SelectionState>(() => ({ ...EMPTY, ...initial }));

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
