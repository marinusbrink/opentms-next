import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

/* The map of an address (design 43, PO 2026-10-12: the old General tab's map section). No geocoder
 * yet (it arrives with Planning), so the map shows the coordinates entered on the address through an
 * OpenStreetMap embed; without coordinates it explains what to enter. A maps provider with geocoding
 * is a later PO choice; the embed keeps the dependency to one URL and no package. */

export interface AddressMapProps {
  latitude: number | null;
  longitude: number | null;
  className?: string;
}

const SPAN = 0.01;

export function addressMapUrl(latitude: number, longitude: number): string {
  const bbox = [
    longitude - SPAN,
    latitude - SPAN,
    longitude + SPAN,
    latitude + SPAN,
  ]
    .map((n) => n.toFixed(5))
    .join(",");
  return `https://www.openstreetmap.org/export/embed.html?bbox=${bbox}&layer=mapnik&marker=${latitude.toFixed(5)},${longitude.toFixed(5)}`;
}

export function AddressMap({
  latitude,
  longitude,
  className,
}: AddressMapProps) {
  const { t } = useL();
  const valid =
    latitude != null &&
    longitude != null &&
    Number.isFinite(latitude) &&
    Number.isFinite(longitude) &&
    Math.abs(latitude) <= 90 &&
    Math.abs(longitude) <= 180;
  return (
    <div
      data-slot="address-map"
      className={cn(
        "flex min-h-64 flex-1 overflow-hidden rounded-md border border-border bg-muted",
        className,
      )}
    >
      {valid ? (
        <iframe
          title={t("MasterData:Map")}
          src={addressMapUrl(latitude, longitude)}
          className="h-full w-full border-0"
          loading="lazy"
          referrerPolicy="no-referrer"
        />
      ) : (
        <p className="m-auto px-4 text-center text-sm text-muted-foreground">
          {t("MasterData:MapNoCoordinates")}
        </p>
      )}
    </div>
  );
}
