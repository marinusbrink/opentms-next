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

        AppFeatures.MasterData.Relations.Default.ShouldBe("Apps.MasterData.Relations");
        AppFeatures.MasterData.Relations.RelationsList.ShouldBe("Apps.MasterData.Relations.Relations");
        AppFeatures.MasterData.Relations.AddressTypes.ShouldBe("Apps.MasterData.Relations.AddressTypes");
        AppFeatures.MasterData.Relations.RelationCategories.ShouldBe("Apps.MasterData.Relations.RelationCategories");

        AppFeatures.MasterData.Transport.Default.ShouldBe("Apps.MasterData.Transport");
        AppFeatures.MasterData.Transport.Products.ShouldBe("Apps.MasterData.Transport.Products");
        AppFeatures.MasterData.Transport.TransportUnits.ShouldBe("Apps.MasterData.Transport.TransportUnits");
        AppFeatures.MasterData.Transport.ProductRelationCodes.ShouldBe("Apps.MasterData.Transport.ProductRelationCodes");
        AppFeatures.MasterData.Transport.Temperatures.ShouldBe("Apps.MasterData.Transport.Temperatures");

        AppFeatures.MasterData.Fleet.Default.ShouldBe("Apps.MasterData.Fleet");
        AppFeatures.MasterData.Fleet.Vehicles.ShouldBe("Apps.MasterData.Fleet.Vehicles");
        AppFeatures.MasterData.Fleet.VehicleCombinations.ShouldBe("Apps.MasterData.Fleet.VehicleCombinations");
        AppFeatures.MasterData.Fleet.Drivers.ShouldBe("Apps.MasterData.Fleet.Drivers");
        AppFeatures.MasterData.Fleet.VehicleTypes.ShouldBe("Apps.MasterData.Fleet.VehicleTypes");
        AppFeatures.MasterData.Fleet.TransportCharacteristics.ShouldBe("Apps.MasterData.Fleet.TransportCharacteristics");

        AppFeatures.MasterData.Reference.Default.ShouldBe("Apps.MasterData.Reference");
        AppFeatures.MasterData.Reference.Countries.ShouldBe("Apps.MasterData.Reference.Countries");
        AppFeatures.MasterData.Reference.Currencies.ShouldBe("Apps.MasterData.Reference.Currencies");
        AppFeatures.MasterData.Reference.GeographicalRegions.ShouldBe("Apps.MasterData.Reference.GeographicalRegions");
    }

    [Fact]
    public void The_old_master_data_flag_type_no_longer_exists()
    {
        typeof(AppFeatures).Assembly.GetType("OpenTms.MasterData.Features.MasterDataFeatures").ShouldBeNull();
        System.Type.GetType("OpenTms.MasterData.Features.MasterDataFeatures, OpenTms.MasterData.Application.Contracts").ShouldBeNull();
    }
}
