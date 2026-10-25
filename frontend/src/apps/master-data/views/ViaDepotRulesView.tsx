import { ViewPlaceholder } from "@/apps/AppPlaceholder";

/* Design 61: the Planning & Execution domain group of the Master data app, Trips → Via depot rules. The list and its
 * editor arrive with the Planning backend (design 59); until then the page skeleton with its gates. */
export function ViaDepotRulesView() {
  return <ViewPlaceholder permission="PlanningExecution.ViaDepotRules" />;
}
