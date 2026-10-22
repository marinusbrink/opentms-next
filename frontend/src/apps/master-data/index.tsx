import { useEffect } from "react";
import { useLocation, useNavigate } from "@tanstack/react-router";

import { APPS } from "@/app/apps.config";
import { useAppFeatures } from "@/app/app-features";

import { AddressTypesView } from "@/apps/master-data/views/AddressTypesView";
import { CountriesView } from "@/apps/master-data/views/CountriesView";
import { CurrenciesView } from "@/apps/master-data/views/CurrenciesView";
import { DriversView } from "@/apps/master-data/views/DriversView";
import { GeographicalRegionsView } from "@/apps/master-data/views/GeographicalRegionsView";
import { ProductRelationCodesView } from "@/apps/master-data/views/ProductRelationCodesView";
import { ProductsView } from "@/apps/master-data/views/ProductsView";
import { RelationCategoriesView } from "@/apps/master-data/views/RelationCategoriesView";
import { RelationsView } from "@/apps/master-data/views/RelationsView";
import { TemperaturesView } from "@/apps/master-data/views/TemperaturesView";
import { TransportCharacteristicsView } from "@/apps/master-data/views/TransportCharacteristicsView";
import { TransportUnitsView } from "@/apps/master-data/views/TransportUnitsView";
import { VehicleCombinationsView } from "@/apps/master-data/views/VehicleCombinationsView";
import { VehicleTypesView } from "@/apps/master-data/views/VehicleTypesView";
import { TransportTripTypesView } from "@/apps/master-data/views/TransportTripTypesView";
import { TransportOrderTypesView } from "@/apps/master-data/views/TransportOrderTypesView";
import { WaypointTypesView } from "@/apps/master-data/views/WaypointTypesView";
import { TimeSlotsView } from "@/apps/master-data/views/TimeSlotsView";
import { VatPercentagesView } from "@/apps/master-data/views/VatPercentagesView";
import { OutboundInvoiceTypesView } from "@/apps/master-data/views/OutboundInvoiceTypesView";
import { InboundInvoiceTypesView } from "@/apps/master-data/views/InboundInvoiceTypesView";
import { AdditionalServicesView } from "@/apps/master-data/views/AdditionalServicesView";
import { DiscountsOrSurchargesView } from "@/apps/master-data/views/DiscountsOrSurchargesView";
import { VehiclesView } from "@/apps/master-data/views/VehiclesView";

/* The Master data app (design 41): routes by pathname like the Administration app. The bare app path, a domain
 * group path and a group path (the nav categories; PO 2026-10-16) redirect to the first list under it that is on for
 * this tenant ("App features", tenants-and-editions.md). The Relations group path is the Relations list itself
 * (design 43). */
const GROUP_PATHS = new Set([
  "/master-data",
  "/master-data/general",
  "/master-data/transport",
  "/master-data/fleet",
  "/master-data/reference",
  "/master-data/orders",
  "/master-data/transport-orders",
  "/master-data/planning",
  "/master-data/trips",
  "/master-data/financial",
  "/master-data/invoicing",
  "/master-data/pricing",
]);
const masterDataApp = APPS.find((app) => app.id === "master-data")!;

export function MasterDataApp() {
  const location = useLocation();
  const navigate = useNavigate();
  const features = useAppFeatures();
  const pathname = location.pathname.replace(/\/+$/, "") || "/master-data";
  const redirect = GROUP_PATHS.has(pathname)
    ? features.firstEnabledPath(masterDataApp, pathname === "/master-data" ? undefined : pathname)
    : undefined;

  useEffect(() => {
    if (redirect) {
      void navigate({ to: redirect, replace: true });
    }
  }, [redirect, navigate]);

  if (pathname.startsWith("/master-data/relation-categories")) return <RelationCategoriesView />;
  if (pathname.startsWith("/master-data/relations")) return <RelationsView />;
  if (pathname.startsWith("/master-data/drivers")) return <DriversView />;
  if (pathname.startsWith("/master-data/vehicle-combinations")) return <VehicleCombinationsView />;
  if (pathname.startsWith("/master-data/vehicles")) return <VehiclesView />;
  if (pathname.startsWith("/master-data/product-relation-codes")) return <ProductRelationCodesView />;
  if (pathname.startsWith("/master-data/products")) return <ProductsView />;
  if (pathname.startsWith("/master-data/transport-units")) return <TransportUnitsView />;
  if (pathname.startsWith("/master-data/temperatures")) return <TemperaturesView />;
  if (pathname.startsWith("/master-data/vehicle-types")) return <VehicleTypesView />;
  if (pathname.startsWith("/master-data/transport-characteristics")) return <TransportCharacteristicsView />;
  if (pathname.startsWith("/master-data/countries")) return <CountriesView />;
  if (pathname.startsWith("/master-data/currencies")) return <CurrenciesView />;
  if (pathname.startsWith("/master-data/geographical-regions")) return <GeographicalRegionsView />;
  /* Designs 51–53: the Orders, Planning & Execution and Financial domain groups. */
  if (pathname.startsWith("/master-data/transport-order-types")) return <TransportOrderTypesView />;
  if (pathname.startsWith("/master-data/waypoint-types")) return <WaypointTypesView />;
  if (pathname.startsWith("/master-data/time-slots")) return <TimeSlotsView />;
  if (pathname.startsWith("/master-data/transport-trip-types")) return <TransportTripTypesView />;
  if (pathname.startsWith("/master-data/vat-percentages")) return <VatPercentagesView />;
  if (pathname.startsWith("/master-data/outbound-invoice-types")) return <OutboundInvoiceTypesView />;
  if (pathname.startsWith("/master-data/inbound-invoice-types")) return <InboundInvoiceTypesView />;
  if (pathname.startsWith("/master-data/additional-services")) return <AdditionalServicesView />;
  if (pathname.startsWith("/master-data/discounts-or-surcharges")) return <DiscountsOrSurchargesView />;
  return <AddressTypesView />;
}
