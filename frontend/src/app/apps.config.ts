import {
  BadgePercent,
  Barcode,
  BarChart3,
  Boxes,
  BookOpen,
  Building2,
  CalendarArrowDown,
  CalendarClock,
  CalendarDays,
  CalendarRange,
  CircleDot,
  ClipboardList,
  Coins,
  Cog,
  Database,
  FileBadge,
  FileInput,
  FileOutput,
  FilePen,
  FileText,
  Files,
  Flag,
  FolderTree,
  History,
  IdCard,
  Landmark,
  Layers,
  LayoutDashboard,
  LayoutTemplate,
  Link2,
  List,
  Mail,
  ListTree,
  MapPin,
  MapPinned,
  Network,
  Package,
  PackagePlus,
  Percent,
  Plug,
  Receipt,
  Route,
  ScrollText,
  Shield,
  ShieldCheck,
  SlidersHorizontal,
  Tags,
  TextQuote,
  Thermometer,
  Truck,
  Users,
  Wallet,
  Warehouse,
  Waypoints,
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
  /**
   * A saved list preset (UI-PAGE-004): the node routes to its list's path with this search (`?filter=today`). Such a
   * node has no feature of its own; the list's node decides (design 61: the Orders and Trips views).
   */
  search?: Readonly<Record<string, string>>;
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
    domains: ["Orders", "PlanningExecution", "Financial", "MasterData"],
    icon: Truck,
    tileClass: "bg-indigo-600",
    feature: "Apps.Transport",
    /* Design 61 (features of design 56, all default on): Orders, Templates, Trips, Documents. The children of Orders
     * and Trips are saved filters of the one list (UI-PAGE-004): nav entries that route to the list's path with
     * `?filter=`; they carry no feature, the list's node decides. Templates' feature chains under Orders
     * (AppFeatures.TransportApp), so it is off whenever Orders is, although it is a sibling in the tree. Records are
     * pages under the list's path (/transport/orders/{id}; design 61, Assumption 1), reached through useOpenRecord. */
    views: [
      {
        nameKey: "App:Transport.Orders",
        path: "/transport/orders",
        feature: "Apps.Transport.Orders",
        icon: ClipboardList,
        children: [
          { nameKey: "App:Transport.View.Today", path: "/transport/orders", search: { filter: "today" }, icon: CalendarDays },
          { nameKey: "App:Transport.View.Tomorrow", path: "/transport/orders", search: { filter: "tomorrow" }, icon: CalendarArrowDown },
          { nameKey: "App:Transport.View.ThisWeek", path: "/transport/orders", search: { filter: "this-week" }, icon: CalendarRange },
          { nameKey: "App:Transport.View.Open", path: "/transport/orders", search: { filter: "open" }, icon: CircleDot },
          { nameKey: "App:Transport.View.Drafts", path: "/transport/orders", search: { filter: "drafts" }, icon: FilePen },
          { nameKey: "App:Transport.View.All", path: "/transport/orders", search: { filter: "all" }, icon: List },
        ],
      },
      { nameKey: "App:Transport.Templates", path: "/transport/templates", feature: "Apps.Transport.Orders.Templates", icon: LayoutTemplate },
      {
        nameKey: "App:Transport.Trips",
        path: "/transport/trips",
        feature: "Apps.Transport.Trips",
        icon: Route,
        children: [
          { nameKey: "App:Transport.View.Today", path: "/transport/trips", search: { filter: "today" }, icon: CalendarDays },
          { nameKey: "App:Transport.View.ThisWeek", path: "/transport/trips", search: { filter: "this-week" }, icon: CalendarRange },
          { nameKey: "App:Transport.View.Open", path: "/transport/trips", search: { filter: "open" }, icon: CircleDot },
          { nameKey: "App:Transport.View.All", path: "/transport/trips", search: { filter: "all" }, icon: List },
        ],
      },
      { nameKey: "App:Transport.Documents", path: "/transport/documents", feature: "Apps.Transport.Documents", icon: FileText },
    ],
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
    domains: ["MasterData", "Orders", "PlanningExecution", "Financial", "Platform"],
    icon: Database,
    tileClass: "bg-slate-600",
    feature: "Apps.MasterData",
    /* PO 2026-10-16: the Master data app holds the master data of EVERY domain, one group per domain in its nav
     * tree (app → domain group → group → list). A domain's group, with its feature and its entry in `domains`,
     * arrives with that domain's first list; a group without children is hidden. The Master Data
     * domain's own group (General) holds the ten reference lists of the first run
     * (docs/designs/41-master-data-first-run.md), grouped as the old menu was, plus Relations and Drivers of the
     * second run (design 43) and Vehicles, Vehicle combinations and Product relation codes of the third (design 48).
     * Orders, Planning & Execution and Financial follow with their first lists (designs 51–53), then the Platform
     * domain's group (design 54) with its three lists directly under it, no sub-groups. The Transport run (design 61)
     * adds seven lists under the existing Orders, Planning & Execution and Financial groups.
     * Views, not modules. The list routes did not move with the tree; the Relations list owns its group's path. */
    views: [
      {
        nameKey: "MasterData:Nav.General",
        path: "/master-data/general",
        feature: "Apps.MasterData.General",
        icon: FolderTree,
        children: [
          {
            nameKey: "MasterData:Nav.Relations",
            path: "/master-data/relations",
            feature: "Apps.MasterData.General.Relations",
            icon: Users,
            children: [
              { nameKey: "MasterData:Relations", path: "/master-data/relations", icon: Building2, feature: "Apps.MasterData.General.Relations.Relations" },
              { nameKey: "MasterData:AddressTypes", path: "/master-data/address-types", icon: MapPin, feature: "Apps.MasterData.General.Relations.AddressTypes" },
              { nameKey: "MasterData:RelationCategories", path: "/master-data/relation-categories", icon: Tags, feature: "Apps.MasterData.General.Relations.RelationCategories" },
            ],
          },
          {
            nameKey: "MasterData:Nav.Transport",
            path: "/master-data/transport",
            feature: "Apps.MasterData.General.Transport",
            icon: Package,
            children: [
              { nameKey: "MasterData:Products", path: "/master-data/products", icon: Package, feature: "Apps.MasterData.General.Transport.Products" },
              { nameKey: "MasterData:TransportUnits", path: "/master-data/transport-units", icon: Boxes, feature: "Apps.MasterData.General.Transport.TransportUnits" },
              { nameKey: "MasterData:ProductRelationCodes", path: "/master-data/product-relation-codes", icon: Barcode, feature: "Apps.MasterData.General.Transport.ProductRelationCodes" },
              { nameKey: "MasterData:Temperatures", path: "/master-data/temperatures", icon: Thermometer, feature: "Apps.MasterData.General.Transport.Temperatures" },
            ],
          },
          {
            nameKey: "MasterData:Nav.Fleet",
            path: "/master-data/fleet",
            feature: "Apps.MasterData.General.Fleet",
            icon: Truck,
            children: [
              { nameKey: "MasterData:Vehicles", path: "/master-data/vehicles", icon: Truck, feature: "Apps.MasterData.General.Fleet.Vehicles" },
              { nameKey: "MasterData:VehicleCombinations", path: "/master-data/vehicle-combinations", icon: Link2, feature: "Apps.MasterData.General.Fleet.VehicleCombinations" },
              { nameKey: "MasterData:Drivers", path: "/master-data/drivers", icon: IdCard, feature: "Apps.MasterData.General.Fleet.Drivers" },
              { nameKey: "MasterData:VehicleTypes", path: "/master-data/vehicle-types", icon: Truck, feature: "Apps.MasterData.General.Fleet.VehicleTypes" },
              { nameKey: "MasterData:TransportCharacteristics", path: "/master-data/transport-characteristics", icon: ListTree, feature: "Apps.MasterData.General.Fleet.TransportCharacteristics" },
            ],
          },
          {
            nameKey: "MasterData:Nav.Reference",
            path: "/master-data/reference",
            feature: "Apps.MasterData.General.Reference",
            icon: MapPinned,
            children: [
              { nameKey: "MasterData:Countries", path: "/master-data/countries", icon: Flag, feature: "Apps.MasterData.General.Reference.Countries" },
              { nameKey: "MasterData:Currencies", path: "/master-data/currencies", icon: Coins, feature: "Apps.MasterData.General.Reference.Currencies" },
              { nameKey: "MasterData:GeographicalRegions", path: "/master-data/geographical-regions", icon: MapPinned, feature: "Apps.MasterData.General.Reference.GeographicalRegions" },
            ],
          },
        ],
      },
      {
        nameKey: "MasterData:Nav.Orders",
        path: "/master-data/orders",
        feature: "Apps.MasterData.Orders",
        icon: ClipboardList,
        children: [
          {
            nameKey: "MasterData:Nav.TransportOrders",
            path: "/master-data/transport-orders",
            feature: "Apps.MasterData.Orders.TransportOrders",
            icon: ClipboardList,
            children: [
              { nameKey: "Orders:TransportOrderTypes", path: "/master-data/transport-order-types", icon: ClipboardList, feature: "Apps.MasterData.Orders.TransportOrders.TransportOrderTypes" },
              { nameKey: "Orders:WaypointTypes", path: "/master-data/waypoint-types", icon: Waypoints, feature: "Apps.MasterData.Orders.TransportOrders.WaypointTypes" },
              { nameKey: "Orders:TimeSlots", path: "/master-data/time-slots", icon: CalendarClock, feature: "Apps.MasterData.Orders.TransportOrders.TimeSlots" },
              /* Design 61 (the Transport run). */
              { nameKey: "Orders:ConsignmentNoteProfiles", path: "/master-data/consignment-note-profiles", icon: FileBadge, feature: "Apps.MasterData.Orders.TransportOrders.ConsignmentNoteProfiles" },
              { nameKey: "Orders:ConsignmentNoteFixedDescriptions", path: "/master-data/consignment-note-fixed-descriptions", icon: TextQuote, feature: "Apps.MasterData.Orders.TransportOrders.ConsignmentNoteFixedDescriptions" },
              { nameKey: "Orders:OrderAttributeDefinitions", path: "/master-data/order-attribute-definitions", icon: SlidersHorizontal, feature: "Apps.MasterData.Orders.TransportOrders.OrderAttributeDefinitions" },
            ],
          },
        ],
      },
      {
        nameKey: "MasterData:Nav.PlanningExecution",
        path: "/master-data/planning",
        feature: "Apps.MasterData.PlanningExecution",
        icon: Route,
        children: [
          {
            nameKey: "MasterData:Nav.Trips",
            path: "/master-data/trips",
            feature: "Apps.MasterData.PlanningExecution.Trips",
            icon: Route,
            children: [
              { nameKey: "PlanningExecution:TransportTripTypes", path: "/master-data/transport-trip-types", icon: Route, feature: "Apps.MasterData.PlanningExecution.Trips.TransportTripTypes" },
              /* Design 61 (the Transport run). */
              { nameKey: "PlanningExecution:ViaDepotRules", path: "/master-data/via-depot-rules", icon: Warehouse, feature: "Apps.MasterData.PlanningExecution.Trips.ViaDepotRules" },
              { nameKey: "PlanningExecution:TripAttributeDefinitions", path: "/master-data/trip-attribute-definitions", icon: SlidersHorizontal, feature: "Apps.MasterData.PlanningExecution.Trips.TripAttributeDefinitions" },
            ],
          },
        ],
      },
      {
        nameKey: "MasterData:Nav.Financial",
        path: "/master-data/financial",
        feature: "Apps.MasterData.Financial",
        icon: Wallet,
        children: [
          {
            nameKey: "MasterData:Nav.Invoicing",
            path: "/master-data/invoicing",
            feature: "Apps.MasterData.Financial.Invoicing",
            icon: Receipt,
            children: [
              { nameKey: "Financial:VatPercentages", path: "/master-data/vat-percentages", icon: Percent, feature: "Apps.MasterData.Financial.Invoicing.VatPercentages" },
              { nameKey: "Financial:OutboundInvoiceTypes", path: "/master-data/outbound-invoice-types", icon: FileOutput, feature: "Apps.MasterData.Financial.Invoicing.OutboundInvoiceTypes" },
              { nameKey: "Financial:InboundInvoiceTypes", path: "/master-data/inbound-invoice-types", icon: FileInput, feature: "Apps.MasterData.Financial.Invoicing.InboundInvoiceTypes" },
            ],
          },
          {
            nameKey: "MasterData:Nav.Pricing",
            path: "/master-data/pricing",
            feature: "Apps.MasterData.Financial.Pricing",
            icon: BadgePercent,
            children: [
              { nameKey: "Financial:AdditionalServices", path: "/master-data/additional-services", icon: PackagePlus, feature: "Apps.MasterData.Financial.Pricing.AdditionalServices" },
              { nameKey: "Financial:DiscountsOrSurcharges", path: "/master-data/discounts-or-surcharges", icon: BadgePercent, feature: "Apps.MasterData.Financial.Pricing.DiscountsOrSurcharges" },
              /* Design 61 (the Transport run; design 57). */
              { nameKey: "Financial:PriceLists", path: "/master-data/price-lists", icon: ScrollText, feature: "Apps.MasterData.Financial.Pricing.PriceLists" },
              { nameKey: "Financial:RelationFinancialSettings", path: "/master-data/relation-financial-settings", icon: Landmark, feature: "Apps.MasterData.Financial.Pricing.RelationFinancialSettings" },
            ],
          },
        ],
      },
      {
        nameKey: "Platform:Nav.Platform",
        path: "/master-data/platform",
        feature: "Apps.MasterData.Platform",
        icon: Cog,
        children: [
          { nameKey: "Platform:EmailTemplates", path: "/master-data/email-templates", icon: Mail, feature: "Apps.MasterData.Platform.EmailTemplates" },
          { nameKey: "Platform:ReportDefinitions", path: "/master-data/report-definitions", icon: FileText, feature: "Apps.MasterData.Platform.ReportDefinitions" },
          { nameKey: "Platform:DocumentTypes", path: "/master-data/document-types", icon: Files, feature: "Apps.MasterData.Platform.DocumentTypes" },
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
