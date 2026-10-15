using System.Collections.Generic;
using System.Linq;
using OpenTms.MasterData.Permissions;
using Shouldly;
using Xunit;

namespace OpenTms.MasterData.Permissions;

/// <summary>
/// Unit tests for the Master Data permission constants (design 41; the definition provider is covered
/// in OpenTms.EntityFrameworkCore.Tests MasterDataDefinitionProviderTests,
/// "Feature flag and permissions"). Risk class: Critical — a wrong permission string silently grants
/// or denies access across the whole tenant fleet; a wrong multi-tenancy side hides the permission
/// from tenant administrators.
/// </summary>
public class MasterDataPermissionDefinitionTests
{
    private static readonly string[] Entities =
    [
        "Currencies", "Countries", "AddressTypes", "RelationCategories", "TransportCharacteristics",
        "Temperatures", "Products", "TransportUnits", "VehicleTypes", "GeographicalRegions",
        /* Run 2 (design 43). */
        "Relations", "Drivers",
        /* Run 3 (design 48). */
        "Vehicles", "VehicleCombinations", "ProductRelationCodes"
    ];

    [Fact]
    public void The_group_is_MasterData()
    {
        MasterDataPermissions.GroupName.ShouldBe("MasterData");
    }

    [Fact]
    public void All_lists_the_fifteen_entities_with_view_create_update_and_delete_names()
    {
        MasterDataPermissions.All.Length.ShouldBe(15);
        MasterDataPermissions.All.Select(p => p.Entity).ShouldBe(Entities);

        foreach (var (entity, view, create, update, delete) in MasterDataPermissions.All)
        {
            view.ShouldBe($"MasterData.{entity}");
            create.ShouldBe($"MasterData.{entity}.Create");
            update.ShouldBe($"MasterData.{entity}.Update");
            delete.ShouldBe($"MasterData.{entity}.Delete");
        }
    }

    [Fact]
    public void The_constants_match_the_All_table()
    {
        MasterDataPermissions.Currencies.Default.ShouldBe("MasterData.Currencies");
        MasterDataPermissions.Currencies.Create.ShouldBe("MasterData.Currencies.Create");
        MasterDataPermissions.Currencies.Update.ShouldBe("MasterData.Currencies.Update");
        MasterDataPermissions.Currencies.Delete.ShouldBe("MasterData.Currencies.Delete");
        MasterDataPermissions.Countries.Default.ShouldBe("MasterData.Countries");
        MasterDataPermissions.AddressTypes.Update.ShouldBe("MasterData.AddressTypes.Update");
        MasterDataPermissions.RelationCategories.Delete.ShouldBe("MasterData.RelationCategories.Delete");
        MasterDataPermissions.TransportCharacteristics.Create.ShouldBe("MasterData.TransportCharacteristics.Create");
        MasterDataPermissions.Temperatures.Default.ShouldBe("MasterData.Temperatures");
        MasterDataPermissions.Products.Update.ShouldBe("MasterData.Products.Update");
        MasterDataPermissions.TransportUnits.Delete.ShouldBe("MasterData.TransportUnits.Delete");
        MasterDataPermissions.VehicleTypes.Create.ShouldBe("MasterData.VehicleTypes.Create");
        MasterDataPermissions.GeographicalRegions.Default.ShouldBe("MasterData.GeographicalRegions");
        MasterDataPermissions.Relations.Default.ShouldBe("MasterData.Relations");
        MasterDataPermissions.Drivers.Delete.ShouldBe("MasterData.Drivers.Delete");
        MasterDataPermissions.Drivers.Anonymise.ShouldBe("MasterData.Drivers.Anonymise");
        /* Run 3 (design 48): the twelve new names, listed in full. */
        MasterDataPermissions.Vehicles.Default.ShouldBe("MasterData.Vehicles");
        MasterDataPermissions.Vehicles.Create.ShouldBe("MasterData.Vehicles.Create");
        MasterDataPermissions.Vehicles.Update.ShouldBe("MasterData.Vehicles.Update");
        MasterDataPermissions.Vehicles.Delete.ShouldBe("MasterData.Vehicles.Delete");
        MasterDataPermissions.VehicleCombinations.Default.ShouldBe("MasterData.VehicleCombinations");
        MasterDataPermissions.VehicleCombinations.Create.ShouldBe("MasterData.VehicleCombinations.Create");
        MasterDataPermissions.VehicleCombinations.Update.ShouldBe("MasterData.VehicleCombinations.Update");
        MasterDataPermissions.VehicleCombinations.Delete.ShouldBe("MasterData.VehicleCombinations.Delete");
        MasterDataPermissions.ProductRelationCodes.Default.ShouldBe("MasterData.ProductRelationCodes");
        MasterDataPermissions.ProductRelationCodes.Create.ShouldBe("MasterData.ProductRelationCodes.Create");
        MasterDataPermissions.ProductRelationCodes.Update.ShouldBe("MasterData.ProductRelationCodes.Update");
        MasterDataPermissions.ProductRelationCodes.Delete.ShouldBe("MasterData.ProductRelationCodes.Delete");
    }

    [Fact]
    public void The_sixty_one_permission_strings_are_unique()
    {
        /* 15 entities x 4 plus the Anonymise child of Drivers (designs 43 and 48). */
        var all = MasterDataPermissions.All.SelectMany(p => new[] { p.Default, p.Create, p.Update, p.Delete })
            .Append(MasterDataPermissions.Drivers.Anonymise)
            .ToList();

        all.Count.ShouldBe(61);
        new HashSet<string>(all).Count.ShouldBe(61);
    }
}
