/* Persistence of a grid's column layout (design #6 §Column personalization, storage amended by
 * docs/designs/grid-settings-storage.md): the debounced PUT and the DELETE of a reset, with the
 * visible failure path UI-FB-005 requires. A plain module, so the failure path is testable
 * without rendering AG Grid.
 *
 * openapi-fetch does not throw on an HTTP error: it resolves with `error` set and `response.ok`
 * false. A `.catch` alone therefore never sees a 500 — which is how saves failed silently before.
 */

import type { ColumnState } from "ag-grid-community";
import { toast } from "sonner";

import { apiClient } from "@/lib/api/client";
import { getApiErrorMessage } from "@/lib/api/errors";

export interface GridSettingsPayload {
  columnStates: { colId: string; visible: boolean; order: number; width: number | null }[];
  sortModels: { colId: string; sort: "asc" | "desc" }[];
}

/** Maps AG Grid's column state onto the GridSettingsDto of the API contract. */
export function buildGridSettingsPayload(colState: ColumnState[]): GridSettingsPayload {
  const columnStates = colState.map((cs, index) => ({
    colId: cs.colId,
    visible: !(cs.hide ?? false),
    order: index,
    width: cs.width ?? null,
  }));
  const sortModels = colState
    .filter((cs) => cs.sort != null)
    .sort((a, b) => (a.sortIndex ?? 0) - (b.sortIndex ?? 0))
    .map((cs) => ({ colId: cs.colId, sort: cs.sort as "asc" | "desc" }));
  return { columnStates, sortModels };
}

export interface GridSettingsPersister {
  /** PUTs the layout; resolves true when the server stored it. */
  save(colState: ColumnState[]): Promise<boolean>;
  /** DELETEs the saved layout; resolves true when the server removed it. */
  reset(): Promise<boolean>;
}

/** Stable toast id per grid: a repeated failure updates the toast in place instead of stacking. */
export function gridSettingsToastId(gridId: string): string {
  return `grid-settings:${gridId}`;
}

export function createGridSettingsPersister(
  gridId: string,
  t: (key: string) => string,
): GridSettingsPersister {
  const fail = (event: string, titleKey: string, error: unknown, status?: number): false => {
    console.error({ event, gridId, status });
    toast.error(t(titleKey), {
      id: gridSettingsToastId(gridId),
      description: getApiErrorMessage(error, t),
    });
    return false;
  };

  return {
    async save(colState) {
      try {
        const { error, response } = await apiClient.PUT("/api/platform/grid-settings/{gridId}", {
          params: { path: { gridId } },
          body: buildGridSettingsPayload(colState),
        });
        if (error !== undefined || !response.ok) {
          return fail("GridSettingsSaveFailed", "Grid:SettingsSaveFailed", error, response.status);
        }
        return true;
      } catch (error) {
        return fail("GridSettingsSaveFailed", "Grid:SettingsSaveFailed", error);
      }
    },

    async reset() {
      try {
        const { error, response } = await apiClient.DELETE("/api/platform/grid-settings/{gridId}", {
          params: { path: { gridId } },
        });
        if (error !== undefined || !response.ok) {
          return fail("GridSettingsResetFailed", "Grid:SettingsResetFailed", error, response.status);
        }
        return true;
      } catch (error) {
        return fail("GridSettingsResetFailed", "Grid:SettingsResetFailed", error);
      }
    },
  };
}
