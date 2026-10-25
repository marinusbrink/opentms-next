import { ViewPlaceholder } from "@/apps/AppPlaceholder";

/* Design 61: the Planning & Execution domain group of the Master data app, Trips → Trip attributes. The list and its
 * editor arrive with the Planning backend (design 59); until then the page skeleton with its gates. */
export function TripAttributeDefinitionsView() {
  return <ViewPlaceholder permission="PlanningExecution.TripAttributeDefinitions" />;
}
