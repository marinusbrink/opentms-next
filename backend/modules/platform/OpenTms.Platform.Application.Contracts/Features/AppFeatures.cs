namespace OpenTms.Platform.Features;

/// <summary>
/// One feature per app and per node of an app's navigation tree (PO decision 2026-10-14,
/// docs/designs/tenants-and-editions.md, "App features"). The names follow the tree, so a node's ancestors are its
/// dot-prefixes: a node is on only when it and every ancestor are on. The tree mirrors
/// <c>frontend/src/app/apps.config.ts</c>; adding an app or a node adds its feature here in the same change.
/// Host-only apps (Tenants, Editions) have no feature: a host has no tenant or edition to carry one.
/// </summary>
public static class AppFeatures
{
    public const string GroupName = "Apps";

    public const string Dashboard = GroupName + ".Dashboard";
    public const string Planboard = GroupName + ".Planboard";
    public const string Transport = GroupName + ".Transport";
    public const string Finance = GroupName + ".Finance";
    public const string Integrations = GroupName + ".Integrations";
    public const string Reports = GroupName + ".Reports";

    public static class MasterData
    {
        public const string Default = GroupName + ".MasterData";

        /// <summary>
        /// The Master Data domain's own group. The Master data app holds the master data of every domain, one group
        /// per domain (PO decision 2026-10-16); a domain's group arrives with its first list.
        /// </summary>
        public static class General
        {
            public const string Default = MasterData.Default + ".General";

            public static class Relations
            {
                public const string Default = General.Default + ".Relations";
                public const string RelationsList = Default + ".Relations";
                public const string AddressTypes = Default + ".AddressTypes";
                public const string RelationCategories = Default + ".RelationCategories";
            }

            public static class Transport
            {
                public const string Default = General.Default + ".Transport";
                public const string Products = Default + ".Products";
                public const string TransportUnits = Default + ".TransportUnits";
                public const string ProductRelationCodes = Default + ".ProductRelationCodes";
                public const string Temperatures = Default + ".Temperatures";
            }

            public static class Fleet
            {
                public const string Default = General.Default + ".Fleet";
                public const string Vehicles = Default + ".Vehicles";
                public const string VehicleCombinations = Default + ".VehicleCombinations";
                public const string Drivers = Default + ".Drivers";
                public const string VehicleTypes = Default + ".VehicleTypes";
                public const string TransportCharacteristics = Default + ".TransportCharacteristics";
            }

            public static class Reference
            {
                public const string Default = General.Default + ".Reference";
                public const string Countries = Default + ".Countries";
                public const string Currencies = Default + ".Currencies";
                public const string GeographicalRegions = Default + ".GeographicalRegions";
            }
        }

        /// <summary>The Orders domain's group (design 51).</summary>
        public static class Orders
        {
            public const string Default = MasterData.Default + ".Orders";

            public static class TransportOrders
            {
                public const string Default = Orders.Default + ".TransportOrders";
                public const string TransportOrderTypes = Default + ".TransportOrderTypes";
                public const string WaypointTypes = Default + ".WaypointTypes";
                public const string TimeSlots = Default + ".TimeSlots";
            }
        }

        /// <summary>The Planning &amp; Execution domain's group (design 53).</summary>
        public static class PlanningExecution
        {
            public const string Default = MasterData.Default + ".PlanningExecution";

            public static class Trips
            {
                public const string Default = PlanningExecution.Default + ".Trips";
                public const string TransportTripTypes = Default + ".TransportTripTypes";
            }
        }

        /// <summary>The Financial domain's group (design 52).</summary>
        public static class Financial
        {
            public const string Default = MasterData.Default + ".Financial";

            public static class Invoicing
            {
                public const string Default = Financial.Default + ".Invoicing";
                public const string VatPercentages = Default + ".VatPercentages";
                public const string OutboundInvoiceTypes = Default + ".OutboundInvoiceTypes";
                public const string InboundInvoiceTypes = Default + ".InboundInvoiceTypes";
            }

            public static class Pricing
            {
                public const string Default = Financial.Default + ".Pricing";
                public const string AdditionalServices = Default + ".AdditionalServices";
                public const string DiscountsOrSurcharges = Default + ".DiscountsOrSurcharges";
            }
        }
    }

    public static class Administration
    {
        public const string Default = GroupName + ".Administration";
        public const string Users = Default + ".Users";
        public const string Roles = Default + ".Roles";
        public const string OrganizationUnits = Default + ".OrganizationUnits";
        public const string Maintenance = Default + ".Maintenance";

        public static class AuditLogs
        {
            public const string Default = Administration.Default + ".AuditLogs";
            public const string FunctionalLogbook = Default + ".FunctionalLogbook";
            public const string TechnicalLogbook = Default + ".TechnicalLogbook";
        }
    }
}
