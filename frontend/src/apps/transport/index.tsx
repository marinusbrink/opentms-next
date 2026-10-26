import { useEffect } from "react";
import { useLocation, useNavigate } from "@tanstack/react-router";

import { useAppFeatures } from "@/app/app-features";
import { FILTER_SEARCH_KEY, filterOf, filtersOf, transportApp } from "@/apps/transport/filters";
import { OrderPage } from "@/apps/transport/pages/OrderPage";
import { TemplatePage } from "@/apps/transport/pages/TemplatePage";
import { TripPage } from "@/apps/transport/pages/TripPage";
import { DocumentsView } from "@/apps/transport/views/DocumentsView";
import { OrdersView } from "@/apps/transport/views/OrdersView";
import { TemplatesView } from "@/apps/transport/views/TemplatesView";
import { TripsView } from "@/apps/transport/views/TripsView";
import { parseRecordPath } from "@/lib/use-open-record";

/* The Transport app (design 61): routes by pathname like the Master data app. The bare app path goes to the first
 * list that is on for this tenant ("App features", tenants-and-editions.md); a list path without a filter goes to the
 * list's first saved filter (`?filter=today`, UI-PAGE-004), so every nav entry and every URL names its filter. A
 * record path (/transport/orders/{id}, design 61 Assumption 1) renders the record's page. */

const LISTS_WITH_FILTERS = ["/transport/orders", "/transport/trips"] as const;

export function TransportApp() {
  const location = useLocation();
  const navigate = useNavigate();
  const features = useAppFeatures();
  const pathname = location.pathname.replace(/\/+$/, "") || "/transport";
  const search = location.search as Record<string, unknown> | undefined;
  const record = parseRecordPath(pathname);

  const appRedirect = pathname === "/transport" ? features.firstEnabledPath(transportApp) : undefined;
  const filterList = LISTS_WITH_FILTERS.find((list) => list === pathname);
  const filterRedirect = filterList != null && filterOf(search, filterList) == null ? filtersOf(filterList)[0] : undefined;

  useEffect(() => {
    if (appRedirect) {
      void navigate({ to: appRedirect, replace: true });
    } else if (filterList && filterRedirect) {
      void navigate({ to: filterList, search: { [FILTER_SEARCH_KEY]: filterRedirect }, replace: true });
    }
  }, [appRedirect, filterList, filterRedirect, navigate]);

  if (record?.type === "TransportOrder") return <OrderPage id={record.id} typeId={typeof search?.type === "string" ? search.type : undefined} />;
  if (record?.type === "TransportOrderTemplate") return <TemplatePage id={record.id} />;
  if (record?.type === "TransportTrip") return <TripPage id={record.id} />;
  if (pathname.startsWith("/transport/templates")) return <TemplatesView />;
  if (pathname.startsWith("/transport/trips")) return <TripsView filter={filterOf(search, "/transport/trips")} />;
  if (pathname.startsWith("/transport/documents")) return <DocumentsView />;
  return <OrdersView filter={filterOf(search, "/transport/orders")} />;
}
