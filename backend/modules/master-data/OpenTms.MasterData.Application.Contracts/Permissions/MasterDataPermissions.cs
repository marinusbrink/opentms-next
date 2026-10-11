namespace OpenTms.MasterData.Permissions;

/// <summary>
/// One view permission per entity with Create, Update and Delete children (assessment §3.11), both sides.
/// Every public application-service method carries one; the architecture test enforces it.
/// </summary>
public static class MasterDataPermissions
{
    public const string GroupName = "MasterData";

    public static class Currencies
    {
        public const string Default = GroupName + ".Currencies";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    public static class Countries
    {
        public const string Default = GroupName + ".Countries";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    public static class AddressTypes
    {
        public const string Default = GroupName + ".AddressTypes";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    public static class RelationCategories
    {
        public const string Default = GroupName + ".RelationCategories";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    public static class TransportCharacteristics
    {
        public const string Default = GroupName + ".TransportCharacteristics";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    public static class Temperatures
    {
        public const string Default = GroupName + ".Temperatures";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    public static class Products
    {
        public const string Default = GroupName + ".Products";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    public static class TransportUnits
    {
        public const string Default = GroupName + ".TransportUnits";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    public static class VehicleTypes
    {
        public const string Default = GroupName + ".VehicleTypes";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    public static class GeographicalRegions
    {
        public const string Default = GroupName + ".GeographicalRegions";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    /// <summary>Every (view, create, update, delete) quadruple, for the definition provider and the tests.</summary>
    public static readonly (string Entity, string Default, string Create, string Update, string Delete)[] All =
    [
        ("Currencies", Currencies.Default, Currencies.Create, Currencies.Update, Currencies.Delete),
        ("Countries", Countries.Default, Countries.Create, Countries.Update, Countries.Delete),
        ("AddressTypes", AddressTypes.Default, AddressTypes.Create, AddressTypes.Update, AddressTypes.Delete),
        ("RelationCategories", RelationCategories.Default, RelationCategories.Create, RelationCategories.Update, RelationCategories.Delete),
        ("TransportCharacteristics", TransportCharacteristics.Default, TransportCharacteristics.Create, TransportCharacteristics.Update, TransportCharacteristics.Delete),
        ("Temperatures", Temperatures.Default, Temperatures.Create, Temperatures.Update, Temperatures.Delete),
        ("Products", Products.Default, Products.Create, Products.Update, Products.Delete),
        ("TransportUnits", TransportUnits.Default, TransportUnits.Create, TransportUnits.Update, TransportUnits.Delete),
        ("VehicleTypes", VehicleTypes.Default, VehicleTypes.Create, VehicleTypes.Update, VehicleTypes.Delete),
        ("GeographicalRegions", GeographicalRegions.Default, GeographicalRegions.Create, GeographicalRegions.Update, GeographicalRegions.Delete),
    ];
}
