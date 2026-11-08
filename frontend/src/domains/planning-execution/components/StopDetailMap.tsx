import { MapView, Marker } from "@/components/ui/map-view";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The map of the stop detail window (design 74 §3): the one stop. Its own module because it renders a marker, so it needs
 * map-view.tsx itself; StopDetailDialog loads it lazily and MapLibre stays out of the main chunks (map-view-lazy.tsx). */

const STOP_ZOOM = 13;

export default function StopDetailMap({ latitude, longitude }: { latitude: number; longitude: number }) {
  const { t } = useL();
  return (
    <MapView initialView={{ latitude, longitude, zoom: STOP_ZOOM }} aria-label={t("PlanningExecution:StopOnMap")} className="rounded-lg">
      <Marker latitude={latitude} longitude={longitude}>
        <span className="block size-4 rounded-full border-2 border-background bg-brand shadow-sm" />
      </Marker>
    </MapView>
  );
}
