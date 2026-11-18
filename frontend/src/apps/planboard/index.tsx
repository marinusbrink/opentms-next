import { PlanboardFiltersProvider } from "@/apps/planboard/filters";
import { PlanboardOptimisticStateProvider } from "@/apps/planboard/optimistic-state";
import { PlanboardWorkspace } from "@/apps/planboard/PlanboardWorkspace";
import { PlanboardSelectionProvider } from "@/apps/planboard/selection";
import { PlanboardSettingsProvider } from "@/apps/planboard/settings-store";
import { PlanboardTripPeekProvider } from "@/apps/planboard/trip-peek";

/* The Planboard app (design 70): one page, the workspace, with its settings store, board filters, selection bus and the read-only trip peek.
 * Design 75: the optimistic state provider wraps the workspace so all panes share the per-trip command queue. */
export function PlanboardApp() {
  return (
    <PlanboardSettingsProvider>
      <PlanboardFiltersProvider>
        <PlanboardSelectionProvider>
          <PlanboardOptimisticStateProvider>
            <PlanboardTripPeekProvider>
              <PlanboardWorkspace />
            </PlanboardTripPeekProvider>
          </PlanboardOptimisticStateProvider>
        </PlanboardSelectionProvider>
      </PlanboardFiltersProvider>
    </PlanboardSettingsProvider>
  );
}
