using OpenTms.Platform.Features;
using Shouldly;
using Xunit;

namespace OpenTms.MasterData.Features;

/// <summary>
/// Unit tests for the Master data app feature names (PO decision 2026-10-14, docs/designs/tenants-and-editions.md,
/// "App features"; they replace MasterData.FirstRun and MasterData.Relations; the definitions are covered in
/// OpenTms.EntityFrameworkCore.Tests AppFeatureDefinitionTests). Risk class: High — the names are what gates every
/// service, what the frontend checks and what an edition stores; the data migration maps the old values onto them.
/// </summary>
public class MasterDataFeatureDefinitionTests
{
    [Fact]
    public void The_master_data_features_follow_the_navigation_tree()
    {
        AppFeatures.GroupName.ShouldBe("Apps");
        AppFeatures.MasterData.Default.ShouldBe("Apps.MasterData");
        AppFeatures.MasterData.General.Default.ShouldBe("Apps.MasterData.General");

        AppFeatures.MasterData.General.Relations.Default.ShouldBe("Apps.MasterData.General.Relations");
        AppFeatures.MasterData.General.Relations.RelationsList.ShouldBe("Apps.MasterData.General.Relations.Relations");
        AppFeatures.MasterData.General.Relations.AddressTypes.ShouldBe("Apps.MasterData.General.Relations.AddressTypes");
        AppFeatures.MasterData.General.Relations.RelationCategories.ShouldBe("Apps.MasterData.General.Relations.RelationCategories");

        AppFeatures.MasterData.General.Transport.Default.ShouldBe("Apps.MasterData.General.Transport");
        AppFeatures.MasterData.General.Transport.Products.ShouldBe("Apps.MasterData.General.Transport.Products");
        AppFeatures.MasterData.General.Transport.TransportUnits.ShouldBe("Apps.MasterData.General.Transport.TransportUnits");
        AppFeatures.MasterData.General.Transport.ProductRelationCodes.ShouldBe("Apps.MasterData.General.Transport.ProductRelationCodes");
        AppFeatures.MasterData.General.Transport.Temperatures.ShouldBe("Apps.MasterData.General.Transport.Temperatures");

        AppFeatures.MasterData.General.Fleet.Default.ShouldBe("Apps.MasterData.General.Fleet");
        AppFeatures.MasterData.General.Fleet.Vehicles.ShouldBe("Apps.MasterData.General.Fleet.Vehicles");
        AppFeatures.MasterData.General.Fleet.VehicleCombinations.ShouldBe("Apps.MasterData.General.Fleet.VehicleCombinations");
        AppFeatures.MasterData.General.Fleet.Drivers.ShouldBe("Apps.MasterData.General.Fleet.Drivers");
        AppFeatures.MasterData.General.Fleet.VehicleTypes.ShouldBe("Apps.MasterData.General.Fleet.VehicleTypes");
        AppFeatures.MasterData.General.Fleet.TransportCharacteristics.ShouldBe("Apps.MasterData.General.Fleet.TransportCharacteristics");

        AppFeatures.MasterData.General.Reference.Default.ShouldBe("Apps.MasterData.General.Reference");
        AppFeatures.MasterData.General.Reference.Countries.ShouldBe("Apps.MasterData.General.Reference.Countries");
        AppFeatures.MasterData.General.Reference.Currencies.ShouldBe("Apps.MasterData.General.Reference.Currencies");
        AppFeatures.MasterData.General.Reference.GeographicalRegions.ShouldBe("Apps.MasterData.General.Reference.GeographicalRegions");
    }

    [Fact]
    public void The_old_master_data_flag_type_no_longer_exists()
    {
        typeof(AppFeatures).Assembly.GetType("OpenTms.MasterData.Features.MasterDataFeatures").ShouldBeNull();
        System.Type.GetType("OpenTms.MasterData.Features.MasterDataFeatures, OpenTms.MasterData.Application.Contracts").ShouldBeNull();
    }
}
