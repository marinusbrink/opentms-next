import {
  BarChart3,
  Boxes,
  BookOpen,
  CalendarRange,
  Coins,
  Database,
  Flag,
  History,
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
}

export const APPS: readonly AppDefinition[] = [
  {
    id: "dashboard",
    nameKey: "App:Dashboard",
    path: "/dashboard",
    domains: ["Reporting"],
    icon: LayoutDashboard,
    tileClass: "bg-sky-600",
  },
  {
    id: "planboard",
    nameKey: "App:Planboard",
    path: "/planboard",
    domains: ["PlanningExecution"],
    icon: CalendarRange,
    tileClass: "bg-emerald-600",
  },
  {
    id: "transport",
    nameKey: "App:Transport",
    path: "/transport",
    domains: ["Orders", "PlanningExecution"],
    icon: Truck,
    tileClass: "bg-indigo-600",
  },
  {
    id: "finance",
    nameKey: "App:Finance",
    path: "/finance",
    domains: ["Financial"],
    icon: Receipt,
    tileClass: "bg-amber-600",
  },
  {
    id: "master-data",
    nameKey: "App:MasterData",
    path: "/master-data",
    domains: ["MasterData"],
    icon: Database,
    tileClass: "bg-slate-600",
    /* The ten reference lists of the first run (docs/designs/41-master-data-first-run.md), grouped
     * as the old menu was. Views, not modules: every one reads the MasterData domain. */
    views: [
      {
        nameKey: "MasterData:Nav.Relations",
        path: "/master-data/relations",
        icon: Users,
        children: [
          { nameKey: "MasterData:AddressTypes", path: "/master-data/address-types", icon: MapPin },
          { nameKey: "MasterData:RelationCategories", path: "/master-data/relation-categories", icon: Tags },
        ],
      },
      {
        nameKey: "MasterData:Nav.Transport",
        path: "/master-data/transport",
        icon: Package,
        children: [
          { nameKey: "MasterData:Products", path: "/master-data/products", icon: Package },
          { nameKey: "MasterData:TransportUnits", path: "/master-data/transport-units", icon: Boxes },
          { nameKey: "MasterData:Temperatures", path: "/master-data/temperatures", icon: Thermometer },
        ],
      },
      {
        nameKey: "MasterData:Nav.Fleet",
        path: "/master-data/fleet",
        icon: Truck,
        children: [
          { nameKey: "MasterData:VehicleTypes", path: "/master-data/vehicle-types", icon: Truck },
          { nameKey: "MasterData:TransportCharacteristics", path: "/master-data/transport-characteristics", icon: ListTree },
        ],
      },
      {
        nameKey: "MasterData:Nav.Reference",
        path: "/master-data/reference",
        icon: MapPinned,
        children: [
          { nameKey: "MasterData:Countries", path: "/master-data/countries", icon: Flag },
          { nameKey: "MasterData:Currencies", path: "/master-data/currencies", icon: Coins },
          { nameKey: "MasterData:GeographicalRegions", path: "/master-data/geographical-regions", icon: MapPinned },
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
  },
  {
    id: "reports",
    nameKey: "App:Reports",
    path: "/reports",
    domains: ["Reporting"],
    icon: BarChart3,
    tileClass: "bg-rose-600",
  },
  {
    id: "admin",
    nameKey: "App:Administration",
    path: "/admin",
    domains: ["Platform"],
    icon: Shield,
    tileClass: "bg-gray-700",
    views: [
      { nameKey: "Administration:Users", path: "/admin/users", icon: Users },
      { nameKey: "Administration:Roles", path: "/admin/roles", icon: ShieldCheck },
      { nameKey: "Administration:OrganizationUnits", path: "/admin/organization-units", icon: Network },
      {
        nameKey: "Administration:AuditLogs",
        path: "/admin/logbook",
        icon: History,
        children: [
          { nameKey: "Administration:FunctionalLogbook", path: "/admin/logbook/functional", icon: BookOpen },
          { nameKey: "Administration:TechnicalLogbook", path: "/admin/logbook/technical", icon: ScrollText },
        ],
      },
      { nameKey: "Administration:Maintenance", path: "/admin/maintenance", icon: Wrench },
    ],
  },
];

export function findAppByPath(pathname: string): AppDefinition | undefined {
  return APPS.find((app) => pathname === app.path || pathname.startsWith(`${app.path}/`));
}
