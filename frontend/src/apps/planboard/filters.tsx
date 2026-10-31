import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from "react";

import { clampRange, defaultBoardRange, isValidRange, MAX_BOARD_RANGE_DAYS, type PlanboardRange } from "@/apps/planboard/range";
import { usePaneSettings } from "@/apps/planboard/settings-store";

/* The board-wide filters of the Planboard (design 70, "Workspace"): the date range (at most 92 days, default today …
 * today + 6) and the organisation units. Every pane follows them unless its own "Use board filter" toggle is off; then
 * it has its own range and units, kept in the pane's settings (`planboard.pane.<instanceId>`). */

export interface PlanboardFilters {
  range: PlanboardRange;
  /** Empty means every organisation unit. */
  organizationUnitIds: readonly string[];
}

export interface PlanboardFiltersValue {
  board: PlanboardFilters;
  setBoardRange: (range: PlanboardRange, changed?: "from" | "till") => void;
  setBoardOrganizationUnitIds: (ids: readonly string[]) => void;
}

const PlanboardFiltersContext = createContext<PlanboardFiltersValue | null>(null);

export function PlanboardFiltersProvider({ children, initial }: { children: ReactNode; initial?: Partial<PlanboardFilters> }) {
  const [board, setBoard] = useState<PlanboardFilters>(() => ({
    range: initial?.range && isValidRange(initial.range) ? initial.range : defaultBoardRange(),
    organizationUnitIds: initial?.organizationUnitIds ?? [],
  }));

  const setBoardRange = useCallback(
    (range: PlanboardRange, changed: "from" | "till" = "from") => setBoard((b) => ({ ...b, range: clampRange(range, MAX_BOARD_RANGE_DAYS, changed) })),
    [],
  );
  const setBoardOrganizationUnitIds = useCallback((ids: readonly string[]) => setBoard((b) => ({ ...b, organizationUnitIds: [...new Set(ids)] })), []);

  const value = useMemo(() => ({ board, setBoardRange, setBoardOrganizationUnitIds }), [board, setBoardRange, setBoardOrganizationUnitIds]);
  return <PlanboardFiltersContext.Provider value={value}>{children}</PlanboardFiltersContext.Provider>;
}

export function usePlanboardFilters(): PlanboardFiltersValue {
  const value = useContext(PlanboardFiltersContext);
  if (!value) throw new Error("usePlanboardFilters needs a PlanboardFiltersProvider (the Planboard workspace).");
  return value;
}

/** The part of a pane's settings the filter override keeps. */
export interface PaneFilterSettings {
  useBoardFilter: boolean;
  /** The pane's own range; null until the toggle is first switched off (then it starts from the board range). */
  ownRange: PlanboardRange | null;
  ownOrganizationUnitIds: string[];
}

export const DEFAULT_PANE_FILTER_SETTINGS: PaneFilterSettings = { useBoardFilter: true, ownRange: null, ownOrganizationUnitIds: [] };

/** Repairs a stored filter override of an older or corrupt shape (exported for tests). */
export function sanitizePaneFilterSettings(stored: Record<string, unknown>, maxDays = MAX_BOARD_RANGE_DAYS): Partial<PaneFilterSettings> {
  const result: Partial<PaneFilterSettings> = {};
  if (typeof stored.useBoardFilter === "boolean") result.useBoardFilter = stored.useBoardFilter;
  if (isValidRange(stored.ownRange, maxDays)) result.ownRange = stored.ownRange;
  if (Array.isArray(stored.ownOrganizationUnitIds)) result.ownOrganizationUnitIds = stored.ownOrganizationUnitIds.filter((id): id is string => typeof id === "string");
  return result;
}

/** The filters a pane applies: the board's, or its own when its toggle is off; `maxDays` caps the pane's range. */
export function effectivePaneFilters(board: PlanboardFilters, settings: PaneFilterSettings, maxDays = MAX_BOARD_RANGE_DAYS): PlanboardFilters {
  if (settings.useBoardFilter) {
    return { range: clampRange(board.range, maxDays, "from"), organizationUnitIds: board.organizationUnitIds };
  }
  return { range: clampRange(settings.ownRange ?? board.range, maxDays, "from"), organizationUnitIds: settings.ownOrganizationUnitIds };
}

export interface PaneFilters {
  /** What the pane filters on now. */
  filters: PlanboardFilters;
  useBoardFilter: boolean;
  /** Switching off copies the board's filters as the pane's own starting point. */
  setUseBoardFilter: (value: boolean) => void;
  /** Sets the pane's own range (only meaningful while the toggle is off). */
  setRange: (range: PlanboardRange, changed?: "from" | "till") => void;
  setOrganizationUnitIds: (ids: readonly string[]) => void;
  isLoaded: boolean;
}

/**
 * The per-pane override of the board filters (design 70): reads and writes the "Use board filter" toggle and the pane's
 * own range and units in the pane's settings. `maxDays` caps the range for panes with a shorter window (the Scheduler:
 * 14 days).
 */
export function usePaneFilters(instanceId: string, options?: { maxDays?: number }): PaneFilters {
  const maxDays = options?.maxDays ?? MAX_BOARD_RANGE_DAYS;
  const { board } = usePlanboardFilters();
  const sanitize = useCallback((stored: Record<string, unknown>) => sanitizePaneFilterSettings(stored, maxDays), [maxDays]);
  const { settings, update, isLoaded } = usePaneSettings<PaneFilterSettings>(instanceId, DEFAULT_PANE_FILTER_SETTINGS, sanitize);

  const setUseBoardFilter = useCallback(
    (value: boolean) =>
      update(
        value
          ? { useBoardFilter: true }
          : {
              useBoardFilter: false,
              ownRange: settings.ownRange ?? clampRange(board.range, maxDays, "from"),
              ownOrganizationUnitIds: settings.ownRange ? settings.ownOrganizationUnitIds : [...board.organizationUnitIds],
            },
      ),
    [update, settings.ownRange, settings.ownOrganizationUnitIds, board.range, board.organizationUnitIds, maxDays],
  );
  const setRange = useCallback((range: PlanboardRange, changed: "from" | "till" = "from") => update({ ownRange: clampRange(range, maxDays, changed) }), [update, maxDays]);
  const setOrganizationUnitIds = useCallback((ids: readonly string[]) => update({ ownOrganizationUnitIds: [...new Set(ids)] }), [update]);

  const filters = useMemo(() => effectivePaneFilters(board, settings, maxDays), [board, settings, maxDays]);
  return { filters, useBoardFilter: settings.useBoardFilter, setUseBoardFilter, setRange, setOrganizationUnitIds, isLoaded };
}

export { PlanboardFiltersContext };
