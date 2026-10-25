import { ViewPlaceholder } from "@/apps/AppPlaceholder";
import { useL } from "@/lib/i18n/LocalizationProvider";

export interface TripPageProps {
  id: string;
}

/* Design 61, "Trips list and editor": the trip's record page (ribbon Save, Start, Complete, Cancel, Archive, Print CMR;
 * the tabs General, Legs, Stops, Cost and revenue, Documents, Record identification), reached through useOpenRecord.
 * The screen arrives with the Planning backend (design 59); until then the page skeleton with its gates. */
export function TripPage({ id }: TripPageProps) {
  const { t } = useL();
  return (
    <ViewPlaceholder permission="PlanningExecution.TransportTrips" feature="Apps.Transport.Trips" heading={t("App:Transport.Trips")}>
      <div data-slot="trip-page" data-record-id={id} />
    </ViewPlaceholder>
  );
}
