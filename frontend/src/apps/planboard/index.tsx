import { PlanboardFiltersProvider } from "@/apps/planboard/filters";
import { PlanboardWorkspace } from "@/apps/planboard/PlanboardWorkspace";
import { PlanboardSelectionProvider } from "@/apps/planboard/selection";
import { PlanboardSettingsProvider } from "@/apps/planboard/settings-store";

/* The Planboard app (design 70): one page, the workspace, with its settings store, board filters and selection bus. */
export function PlanboardApp() {
  return (
    <PlanboardSettingsProvider>
      <PlanboardFiltersProvider>
        <PlanboardSelectionProvider>
          <PlanboardWorkspace />
        </PlanboardSelectionProvider>
      </PlanboardFiltersProvider>
    </PlanboardSettingsProvider>
  );
}
