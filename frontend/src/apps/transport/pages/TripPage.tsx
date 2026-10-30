import { useAppFeatures } from "@/app/app-features";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { FeatureUnavailable } from "@/components/ui/feature-unavailable";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { RecordNotFound } from "@/components/ui/record-not-found";
import { PageSkeleton } from "@/apps/transport/pages/OrderPage";
import { TripEditor } from "@/apps/transport/trips/TripEditor";
import { useTransportTrip } from "@/domains/planning-execution/trips";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { recordLoadState } from "@/lib/use-open-record";

export interface TripPageProps {
  id: string;
}

/* Design 61, "Trips list and editor": the trip's record page (/transport/trips/{id}) with the page states of
 * UI-PAGE-005 around the TripEditor. */
export function TripPage({ id }: TripPageProps) {
  const { t } = useL();
  const features = useAppFeatures();
  const canView = useIsGranted("PlanningExecution.TransportTrips");
  const canUpdate = useIsGranted("PlanningExecution.TransportTrips.Update");
  const trip = useTransportTrip(canView ? id : undefined);

  if (features.isLoaded && !features.isFeatureEnabled("Apps.Transport.Trips")) return <FeatureUnavailable />;
  if (!canView) return <PermissionDenied />;

  const state = recordLoadState(trip);
  if (state === "loading") return <PageSkeleton />;
  if (state === "not-found") return <RecordNotFound />;
  if (state === "error")
    return (
      <Alert variant="destructive" className="m-6">
        <AlertDescription>{getApiErrorMessage(trip.error, t)}</AlertDescription>
      </Alert>
    );
  return <TripEditor trip={trip.data!} readOnly={!canUpdate} />;
}
