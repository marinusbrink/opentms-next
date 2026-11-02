using OpenTms.Platform;
using OpenTms.Platform.PredefinedRoles;
using P = OpenTms.MasterData.Permissions.MasterDataPermissions;

namespace OpenTms.MasterData.Permissions;

/// <summary>
/// The Master Data permissions of the predefined roles (docs/designs/71-predefined-roles.md). Order entry and Planner get
/// Create and Update on the lists their forms pick from, so "Add new…" works there (UI-LOOK-004).
/// <c>Drivers.Anonymise</c> stays with the administrator.
/// </summary>
public static class MasterDataPredefinedRoles
{
    private static readonly string[] Views =
    [
        P.Currencies.Default, P.Countries.Default, P.AddressTypes.Default, P.RelationCategories.Default,
        P.TransportCharacteristics.Default, P.Temperatures.Default, P.Products.Default, P.TransportUnits.Default,
        P.VehicleTypes.Default, P.GeographicalRegions.Default, P.Relations.Default, P.Drivers.Default, P.Vehicles.Default,
        P.VehicleCombinations.Default, P.ProductRelationCodes.Default,
    ];

    public static void Configure(PredefinedRoleOptions options)
    {
        options.Grant(PlatformRoleNames.OrderEntry, Views);
        options.Grant(PlatformRoleNames.OrderEntry,
            P.Countries.Create, P.Countries.Update,
            P.AddressTypes.Create, P.AddressTypes.Update,
            P.TransportCharacteristics.Create, P.TransportCharacteristics.Update,
            P.Temperatures.Create, P.Temperatures.Update,
            P.Products.Create, P.Products.Update,
            P.TransportUnits.Create, P.TransportUnits.Update,
            P.Relations.Create, P.Relations.Update);

        options.Grant(PlatformRoleNames.Planner, Views);
        options.Grant(PlatformRoleNames.Planner,
            P.Drivers.Create, P.Drivers.Update,
            P.Vehicles.Create, P.Vehicles.Update,
            P.VehicleCombinations.Create, P.VehicleCombinations.Update);

        options.Grant(PlatformRoleNames.Finance, Views);
        options.Grant(PlatformRoleNames.Finance,
            P.Relations.Create, P.Relations.Update);

        options.Grant(PlatformRoleNames.MasterDataManager,
            P.Currencies.Default, P.Currencies.Create, P.Currencies.Update, P.Currencies.Delete,
            P.Countries.Default, P.Countries.Create, P.Countries.Update, P.Countries.Delete,
            P.AddressTypes.Default, P.AddressTypes.Create, P.AddressTypes.Update, P.AddressTypes.Delete,
            P.RelationCategories.Default, P.RelationCategories.Create, P.RelationCategories.Update, P.RelationCategories.Delete,
            P.TransportCharacteristics.Default, P.TransportCharacteristics.Create, P.TransportCharacteristics.Update, P.TransportCharacteristics.Delete,
            P.Temperatures.Default, P.Temperatures.Create, P.Temperatures.Update, P.Temperatures.Delete,
            P.Products.Default, P.Products.Create, P.Products.Update, P.Products.Delete,
            P.TransportUnits.Default, P.TransportUnits.Create, P.TransportUnits.Update, P.TransportUnits.Delete,
            P.VehicleTypes.Default, P.VehicleTypes.Create, P.VehicleTypes.Update, P.VehicleTypes.Delete,
            P.GeographicalRegions.Default, P.GeographicalRegions.Create, P.GeographicalRegions.Update, P.GeographicalRegions.Delete,
            P.Relations.Default, P.Relations.Create, P.Relations.Update, P.Relations.Delete,
            P.Drivers.Default, P.Drivers.Create, P.Drivers.Update, P.Drivers.Delete,
            P.Vehicles.Default, P.Vehicles.Create, P.Vehicles.Update, P.Vehicles.Delete,
            P.VehicleCombinations.Default, P.VehicleCombinations.Create, P.VehicleCombinations.Update, P.VehicleCombinations.Delete,
            P.ProductRelationCodes.Default, P.ProductRelationCodes.Create, P.ProductRelationCodes.Update, P.ProductRelationCodes.Delete);

        options.Grant(PlatformRoleNames.ReadOnly, Views);
    }
}
