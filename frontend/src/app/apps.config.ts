import {
  BarChart3,
  Boxes,
  BookOpen,
  Building2,
  CalendarRange,
  Coins,
  Database,
  Flag,
  History,
  IdCard,
  Landmark,
  Layers,
  LayoutDashboard,
  ListTree,
  MapPin,
  MapPinned,
  Network,
  Package,
  Plug,
  Receipt,
  ScrollText,
  Shield,
  ShieldCheck,
  Tags,
  Thermometer,
  Truck,
  Users,
  Wrench,
  type LucideIcon,
} from "lucide-react";

/* THE shell configuration — the single place where the app list lives.
 *
 * Apps are VIEWS on domains, not modules (see /CLAUDE.md — app→domain mapping):
 * an app never gets its own backend module, it reads from the domains listed here
 * through the generated API client. Changing this list or a mapping is a PO
 * decision; keep the table in /CLAUDE.md in sync with this file.
 *
 * `nameKey` refers to the ABP localization resource "OpenTms" — app names are
 * localized like every other user-facing string (no hardcoded texts).
 */

export interface AppView {
  /** Localization key for the view label. */
  nameKey: string;
  /** Route path (exact or prefix) for active-state matching and navigation. */
  path: string;
  icon: LucideIcon;
  /** Sub-items for expandable categories (Administration's "Audit logs" holds the two logbooks). */
  children?: readonly AppView[];
  /** The ABP feature of this node (see app-features.ts); its ancestors are the dot-prefixes of the name. */
  feature?: string;
  /** Permission the node needs; absent → shown. The pane hides it otherwise (UI-PERM-002). */
  requiredPermission?: string;
}

export type DomainName =
  | "Orders"
  | "PlanningExecution"
  | "Financial"
  | "MasterData"
  | "Integrations"
  | "Reporting"
  | "Platform";

export interface AppDefinition {
  /** Stable identifier; also the app's folder name under src/apps/. */
  id: string;
  /** Localization key for the app name (resource: OpenTms). */
  nameKey: string;
  /** Route path of the app. */
  path: string;
  /** Domains this app reads from — views on domains, never modules of their own. */
  domains: readonly DomainName[];
  icon: LucideIcon;
  /** Office-style tile accent used in the app launcher. */
  tileClass: string;
  /** Left-nav views. Absent → no pane, full-width layout. */
  views?: readonly AppView[];
  /**
   * Permission the tile needs; absent → always shown. A list means any one of them. Host-side permissions keep
   * an app off the tenant side.
   */
  requiredPermission?: string | readonly string[];
  /**
   * The ABP feature of the app (PO 2026-10-14: every app and every navigation node is a feature). Absent for
   * host-only apps: a host has no tenant or edition to carry a feature.
   */
  feature?: string;
  /** The host always has this app and its nodes, whatever the feature values (Administration). */
  hostAlwaysEnabled?: boolean;
}

export const APPS: readonly AppDefinition[] = [
  {
    id: "dashboard",
    nameKey: "App:Dashboard",
    path: "/dashboard",
    domains: ["Reporting"],
    icon: LayoutDashboard,
    tileClass: "bg-sky-600",
    feature: "Apps.Dashboard",
  },
  {
    id: "planboard",
    nameKey: "App:Planboard",
    path: "/planboard",
    domains: ["PlanningExecution"],
    icon: CalendarRange,
    tileClass: "bg-emerald-600",
    feature: "Apps.Planboard",
  },
  {
    id: "transport",
    nameKey: "App:Transport",
    path: "/transport",
    domains: ["Orders", "PlanningExecution"],
    icon: Truck,
    tileClass: "bg-indigo-600",
    feature: "Apps.Transport",
  },
  {
    id: "finance",
    nameKey: "App:Finance",
    path: "/finance",
    domains: ["Financial"],
    icon: Receipt,
    tileClass: "bg-amber-600",
    feature: "Apps.Finance",
  },
  {
    id: "master-data",
    nameKey: "App:MasterData",
    path: "/master-data",
    domains: ["MasterData"],
    icon: Database,
    tileClass: "bg-slate-600",
    feature: "Apps.MasterData",
    /* The ten reference lists of the first run (docs/designs/41-master-data-first-run.md), grouped
     * as the old menu was, plus Relations and Drivers of the second run (design 43; both behind the
     * MasterData.Relations feature, which the views check). Views, not modules: every one reads the
     * MasterData domain. The Relations list owns the group's own path. */
    views: [
      {
        nameKey: "MasterData:Nav.Relations",
        path: "/master-data/relations",
        feature: "Apps.MasterData.Relations",
        icon: Users,
        children: [
          { nameKey: "MasterData:Relations", path: "/master-data/relations", icon: Building2, feature: "Apps.MasterData.Relations.Relations" },
          { nameKey: "MasterData:AddressTypes", path: "/master-data/address-types", icon: MapPin, feature: "Apps.MasterData.Relations.AddressTypes" },
          { nameKey: "MasterData:RelationCategories", path: "/master-data/relation-categories", icon: Tags, feature: "Apps.MasterData.Relations.RelationCategories" },
        ],
      },
      {
        nameKey: "MasterData:Nav.Transport",
        path: "/master-data/transport",
        feature: "Apps.MasterData.Transport",
        icon: Package,
        children: [
          { nameKey: "MasterData:Products", path: "/master-data/products", icon: Package, feature: "Apps.MasterData.Transport.Products" },
          { nameKey: "MasterData:TransportUnits", path: "/master-data/transport-units", icon: Boxes, feature: "Apps.MasterData.Transport.TransportUnits" },
          { nameKey: "MasterData:Temperatures", path: "/master-data/temperatures", icon: Thermometer, feature: "Apps.MasterData.Transport.Temperatures" },
        ],
      },
      {
        nameKey: "MasterData:Nav.Fleet",
        path: "/master-data/fleet",
        feature: "Apps.MasterData.Fleet",
        icon: Truck,
        children: [
          { nameKey: "MasterData:Drivers", path: "/master-data/drivers", icon: IdCard, feature: "Apps.MasterData.Fleet.Drivers" },
          { nameKey: "MasterData:VehicleTypes", path: "/master-data/vehicle-types", icon: Truck, feature: "Apps.MasterData.Fleet.VehicleTypes" },
          { nameKey: "MasterData:TransportCharacteristics", path: "/master-data/transport-characteristics", icon: ListTree, feature: "Apps.MasterData.Fleet.TransportCharacteristics" },
        ],
      },
      {
        nameKey: "MasterData:Nav.Reference",
        path: "/master-data/reference",
        feature: "Apps.MasterData.Reference",
        icon: MapPinned,
        children: [
          { nameKey: "MasterData:Countries", path: "/master-data/countries", icon: Flag, feature: "Apps.MasterData.Reference.Countries" },
          { nameKey: "MasterData:Currencies", path: "/master-data/currencies", icon: Coins, feature: "Apps.MasterData.Reference.Currencies" },
          { nameKey: "MasterData:GeographicalRegions", path: "/master-data/geographical-regions", icon: MapPinned, feature: "Apps.MasterData.Reference.GeographicalRegions" },
        ],
      },
    ],
  },
  {
    id: "integrations",
    nameKey: "App:Integrations",
    path: "/integrations",
    domains: ["Integrations"],
    icon: Plug,
    tileClass: "bg-violet-600",
    feature: "Apps.Integrations",
  },
  {
    id: "reports",
    nameKey: "App:Reports",
    path: "/reports",
    domains: ["Reporting"],
    icon: BarChart3,
    tileClass: "bg-rose-600",
    feature: "Apps.Reports",
  },
  /* Host only (docs/designs/tenants-and-editions.md, PO 2026-10-14): one app with the views Tenants and Editions.
   * Host-side permissions keep it away from every tenant user; it has no feature (a host has no edition). It comes
   * before Administration, so the host's launcher starts with it (PO 2026-10-14). */
  {
    id: "tenants",
    nameKey: "App:Tenants",
    path: "/tenants",
    domains: ["Platform"],
    icon: Landmark,
    tileClass: "bg-teal-700",
    requiredPermission: ["Platform.Tenants", "Platform.Editions"],
    views: [
      { nameKey: "Tenants:Nav.Tenants", path: "/tenants/list", icon: Building2, requiredPermission: "Platform.Tenants" },
      { nameKey: "Tenants:Nav.Editions", path: "/tenants/editions", icon: Layers, requiredPermission: "Platform.Editions" },
    ],
  },
  {
    id: "admin",
    nameKey: "App:Administration",
    path: "/admin",
    domains: ["Platform"],
    icon: Shield,
    tileClass: "bg-gray-700",
    feature: "Apps.Administration",
    hostAlwaysEnabled: true,
    views: [
      { nameKey: "Administration:Users", path: "/admin/users", icon: Users, feature: "Apps.Administration.Users" },
      { nameKey: "Administration:Roles", path: "/admin/roles", icon: ShieldCheck, feature: "Apps.Administration.Roles" },
      { nameKey: "Administration:OrganizationUnits", path: "/admin/organization-units", icon: Network, feature: "Apps.Administration.OrganizationUnits" },
      {
        nameKey: "Administration:AuditLogs",
        path: "/admin/logbook",
        feature: "Apps.Administration.AuditLogs",
        icon: History,
        children: [
          { nameKey: "Administration:FunctionalLogbook", path: "/admin/logbook/functional", icon: BookOpen, feature: "Apps.Administration.AuditLogs.FunctionalLogbook" },
          { nameKey: "Administration:TechnicalLogbook", path: "/admin/logbook/technical", icon: ScrollText, feature: "Apps.Administration.AuditLogs.TechnicalLogbook" },
        ],
      },
      { nameKey: "Administration:Maintenance", path: "/admin/maintenance", icon: Wrench, feature: "Apps.Administration.Maintenance" },
    ],
  },
];

export function findAppByPath(pathname: string): AppDefinition | undefined {
  return APPS.find((app) => pathname === app.path || pathname.startsWith(`${app.path}/`));
}
