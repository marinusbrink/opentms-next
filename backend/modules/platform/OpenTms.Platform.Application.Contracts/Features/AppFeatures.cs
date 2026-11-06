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

    /// <summary>The Transport app's navigation nodes (design 56; every node a feature, PO 2026-10-14; all default on, PO 2026-10-25).</summary>
    public static class TransportApp
    {
        public const string Orders = Transport + ".Orders";
        public const string OrderTemplates = Orders + ".Templates";
        public const string Trips = Transport + ".Trips";
        public const string Documents = Transport + ".Documents";

        /// <summary>The order intake review queue (design 72; default on like its siblings, the screens also check Integrations.OrderIntake).</summary>
        public const string Intake = Transport + ".Intake";
    }
    /// <summary>
    /// The Planboard app's panes (design 66/68, PO decision D7: every pane a nav node and a feature; all default on, an
    /// explicit deviation from constitution rule 4 stated in the PR).
    /// </summary>
    public static class PlanboardApp
    {
        public const string Trips = Planboard + ".Trips";
        public const string Scheduler = Planboard + ".Scheduler";
        public const string Orders = Planboard + ".Orders";
        public const string Stops = Planboard + ".Stops";
        public const string Legs = Planboard + ".Legs";
        public const string LegsToSchedule = Planboard + ".LegsToSchedule";
        public const string Map = Planboard + ".Map";
        public const string Resources = Planboard + ".Resources";
    }

    public const string Finance = GroupName + ".Finance";
    public const string Integrations = GroupName + ".Integrations";

    /// <summary>
    /// The Integrations app's navigation nodes (design 72). The group Order intake holds Received and the order-intake
    /// setup: mailboxes, API clients and import profiles (PO decision 2026-11-03: not master data, so not in the Master
    /// data app).
    /// </summary>
    public static class IntegrationsApp
    {
        /// <summary>
        /// The AI connector (design 74): the nav node and the connector itself, a sibling of the group Order intake. Default
        /// off per tenant (constitution rule 4): without it <c>/mcp</c> and the connect screen refuse and the node is hidden.
        /// </summary>
        public const string AiConnector = Integrations + ".AiConnector";

        /// <summary>The group Order intake.</summary>
        public static class Intake
        {
            public const string Default = Integrations + ".Intake";

            /// <summary>Received messages and their trace.</summary>
            public const string Received = Default + ".Received";
            public const string Mailboxes = Default + ".Mailboxes";
            public const string ApiClients = Default + ".ApiClients";
            public const string ImportProfiles = Default + ".ImportProfiles";

            /// <summary>The order intake's tenant settings (design 72 build note 25).</summary>
            public const string Settings = Default + ".Settings";
        }
    }

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

        /// <summary>The Platform domain's group (design 54): its three lists sit directly under it.</summary>
        public static class Platform
        {
            public const string Default = MasterData.Default + ".Platform";
            public const string EmailTemplates = Default + ".EmailTemplates";
            public const string ReportDefinitions = Default + ".ReportDefinitions";
            public const string DocumentTypes = Default + ".DocumentTypes";
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
                public const string ConsignmentNoteProfiles = Default + ".ConsignmentNoteProfiles";
                public const string ConsignmentNoteFixedDescriptions = Default + ".ConsignmentNoteFixedDescriptions";
                public const string OrderAttributeDefinitions = Default + ".OrderAttributeDefinitions";
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
                public const string ViaDepotRules = Default + ".ViaDepotRules";
                public const string TripAttributeDefinitions = Default + ".TripAttributeDefinitions";
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
                public const string PriceLists = Default + ".PriceLists";
                public const string RelationFinancialSettings = Default + ".RelationFinancialSettings";
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

        /// <summary>
        /// Administration → Services: the host's settings of the external services, a tab per service (e-mail of design
        /// 64, documents, routing; PO 2026-11-02, replacing the E-mail node). Host only, so a tenant never sees it.
        /// </summary>
        public const string Services = Default + ".Services";

        public static class AuditLogs
        {
            public const string Default = Administration.Default + ".AuditLogs";
            public const string FunctionalLogbook = Default + ".FunctionalLogbook";
            public const string TechnicalLogbook = Default + ".TechnicalLogbook";
        }
    }
}
