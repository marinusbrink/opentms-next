import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from "react";
import { toast } from "sonner";

import { useAppFeatures } from "@/app/app-features";
import { TripPeekSheet } from "@/apps/planboard/TripPeekSheet";
import { useIsGrantedChecker } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { RECORD_ROUTES, useOpenRecord } from "@/lib/use-open-record";

/* Opening a trip from any Planboard pane (design 70 assumption 1): the trip page of the Transport app when the tenant has
 * it (`Apps.Transport.Trips`, through useOpenRecord), otherwise the read-only TripPeekSheet over the workspace instead of
 * the "feature unavailable" toast. Every pane opens trips through `useOpenPlanboardTrip()`; the workspace holds the
 * provider and renders the sheet with `PlanboardTripPeekHost`. */

interface TripPeekValue {
  peekTripId: string | null;
  peek: (tripId: string | null) => void;
}

const TripPeekContext = createContext<TripPeekValue | null>(null);

export function PlanboardTripPeekProvider({ children }: { children: ReactNode }) {
  const [peekTripId, setPeekTripId] = useState<string | null>(null);
  const value = useMemo(() => ({ peekTripId, peek: setPeekTripId }), [peekTripId]);
  return <TripPeekContext.Provider value={value}>{children}</TripPeekContext.Provider>;
}

/** The sheet of the trip being peeked at; place it inside the workspace's positioned container. */
export function PlanboardTripPeekHost() {
  const value = useContext(TripPeekContext);
  if (!value?.peekTripId) return null;
  return <TripPeekSheet key={value.peekTripId} tripId={value.peekTripId} onClose={() => value.peek(null)} />;
}

/** `const openTrip = useOpenPlanboardTrip(); openTrip(id)`: the trip page, or the read-only peek without the Transport
 * app's trip page. Without the view permission nothing opens (a toast, UI-PERM-004). */
export function useOpenPlanboardTrip(): (tripId: string) => void {
  const context = useContext(TripPeekContext);
  const peek = context?.peek;
  const openRecord = useOpenRecord();
  const features = useAppFeatures();
  const isGranted = useIsGrantedChecker();
  const { t } = useL();

  return useCallback(
    (tripId: string) => {
      const route = RECORD_ROUTES.TransportTrip;
      if (peek && features.isLoaded && !features.isFeatureEnabled(route.feature)) {
        if (!isGranted(route.permission)) {
          toast.error(t("PermissionDenied"));
          return;
        }
        peek(tripId);
        return;
      }
      openRecord("TransportTrip", tripId, { returnTo: "/planboard" });
    },
    [peek, features, isGranted, openRecord, t],
  );
}
