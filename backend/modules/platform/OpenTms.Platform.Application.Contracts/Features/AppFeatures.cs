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

        public static class Relations
        {
            public const string Default = MasterData.Default + ".Relations";
            public const string RelationsList = Default + ".Relations";
            public const string AddressTypes = Default + ".AddressTypes";
            public const string RelationCategories = Default + ".RelationCategories";
        }

        public static class Transport
        {
            public const string Default = MasterData.Default + ".Transport";
            public const string Products = Default + ".Products";
            public const string TransportUnits = Default + ".TransportUnits";
            public const string ProductRelationCodes = Default + ".ProductRelationCodes";
            public const string Temperatures = Default + ".Temperatures";
        }

        public static class Fleet
        {
            public const string Default = MasterData.Default + ".Fleet";
            public const string Vehicles = Default + ".Vehicles";
            public const string VehicleCombinations = Default + ".VehicleCombinations";
            public const string Drivers = Default + ".Drivers";
            public const string VehicleTypes = Default + ".VehicleTypes";
            public const string TransportCharacteristics = Default + ".TransportCharacteristics";
        }

        public static class Reference
        {
            public const string Default = MasterData.Default + ".Reference";
            public const string Countries = Default + ".Countries";
            public const string Currencies = Default + ".Currencies";
            public const string GeographicalRegions = Default + ".GeographicalRegions";
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
