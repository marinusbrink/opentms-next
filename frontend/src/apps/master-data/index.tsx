import { useEffect } from "react";
import { useLocation, useNavigate } from "@tanstack/react-router";

import { AddressTypesView } from "@/apps/master-data/views/AddressTypesView";
import { CountriesView } from "@/apps/master-data/views/CountriesView";
import { CurrenciesView } from "@/apps/master-data/views/CurrenciesView";
import { DriversView } from "@/apps/master-data/views/DriversView";
import { GeographicalRegionsView } from "@/apps/master-data/views/GeographicalRegionsView";
import { ProductsView } from "@/apps/master-data/views/ProductsView";
import { RelationCategoriesView } from "@/apps/master-data/views/RelationCategoriesView";
import { RelationsView } from "@/apps/master-data/views/RelationsView";
import { TemperaturesView } from "@/apps/master-data/views/TemperaturesView";
import { TransportCharacteristicsView } from "@/apps/master-data/views/TransportCharacteristicsView";
import { TransportUnitsView } from "@/apps/master-data/views/TransportUnitsView";
import { VehicleTypesView } from "@/apps/master-data/views/VehicleTypesView";

/* The Master data app (design 41): routes by pathname like the Administration app. A group path
 * (the nav category) redirects to its first list; the bare app path to the first group's first list.
 * The Relations group path is the Relations list itself (design 43). */
const GROUP_REDIRECTS: Record<string, string> = {
  "/master-data": "/master-data/address-types",
  "/master-data/transport": "/master-data/products",
  "/master-data/fleet": "/master-data/vehicle-types",
  "/master-data/reference": "/master-data/countries",
};

export function MasterDataApp() {
  const location = useLocation();
  const navigate = useNavigate();
  const pathname = location.pathname.replace(/\/+$/, "") || "/master-data";
  const redirect = GROUP_REDIRECTS[pathname];

  useEffect(() => {
    if (redirect) {
      void navigate({ to: redirect, replace: true });
    }
  }, [redirect, navigate]);

  if (pathname.startsWith("/master-data/relation-categories")) return <RelationCategoriesView />;
  if (pathname.startsWith("/master-data/relations")) return <RelationsView />;
  if (pathname.startsWith("/master-data/drivers")) return <DriversView />;
  if (pathname.startsWith("/master-data/products")) return <ProductsView />;
  if (pathname.startsWith("/master-data/transport-units")) return <TransportUnitsView />;
  if (pathname.startsWith("/master-data/temperatures")) return <TemperaturesView />;
  if (pathname.startsWith("/master-data/vehicle-types")) return <VehicleTypesView />;
  if (pathname.startsWith("/master-data/transport-characteristics")) return <TransportCharacteristicsView />;
  if (pathname.startsWith("/master-data/countries")) return <CountriesView />;
  if (pathname.startsWith("/master-data/currencies")) return <CurrenciesView />;
  if (pathname.startsWith("/master-data/geographical-regions")) return <GeographicalRegionsView />;
  return <AddressTypesView />;
}
