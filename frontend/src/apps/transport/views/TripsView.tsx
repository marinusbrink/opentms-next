import { ViewPlaceholder } from "@/apps/AppPlaceholder";

export interface TripsViewProps {
  /** The saved filter from the URL (today, this-week, open, all; UI-PAGE-004). */
  filter?: string;
}

/* Design 61, "Trips list and editor": the trips grid with its command bar (no New: the Planboard, PO). The screen
 * arrives with the Planning backend (design 59); until then the page skeleton with its gates. */
export function TripsView({ filter }: TripsViewProps) {
  return <ViewPlaceholder permission="PlanningExecution.TransportTrips" feature="Apps.Transport.Trips" filter={filter} />;
}
