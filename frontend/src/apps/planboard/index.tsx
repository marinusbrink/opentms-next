import { PlanboardFiltersProvider } from "@/apps/planboard/filters";
import { PlanboardWorkspace } from "@/apps/planboard/PlanboardWorkspace";
import { PlanboardSelectionProvider } from "@/apps/planboard/selection";
import { PlanboardSettingsProvider } from "@/apps/planboard/settings-store";
import { PlanboardTripPeekProvider } from "@/apps/planboard/trip-peek";

/* The Planboard app (design 70): one page, the workspace, with its settings store, board filters, selection bus and the read-only trip peek. */
export function PlanboardApp() {
  return (
    <PlanboardSettingsProvider>
      <PlanboardFiltersProvider>
        <PlanboardSelectionProvider>
          <PlanboardTripPeekProvider>
            <PlanboardWorkspace />
          </PlanboardTripPeekProvider>
        </PlanboardSelectionProvider>
      </PlanboardFiltersProvider>
    </PlanboardSettingsProvider>
  );
}
