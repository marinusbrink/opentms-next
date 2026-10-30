using System;

namespace OpenTms.Platform.TestData;

/// <summary>
/// The size of a test dataset. "Each known entity" means the master-data lists and the operational records;
/// ISO and reference lists (countries, currencies, VAT, order types, waypoint types...) keep their seeded,
/// realistic size (docs/designs/65-tenant-test-datasets.md).
/// </summary>
public sealed record TestDatasetProfile(
    string Name,
    int Seed,
    int Relations,
    int MinPerEntity,
    int MaxPerEntity,
    int Orders,
    int OrderDaysBack,
    int OrderDaysAhead)
{
    public static readonly TestDatasetProfile Average = new(
        TestDatasets.Average, Seed: 5_000, Relations: 5_000, MinPerEntity: 25, MaxPerEntity: 250,
        Orders: 4_000, OrderDaysBack: 60, OrderDaysAhead: 14);

    public static readonly TestDatasetProfile Large = new(
        TestDatasets.Large, Seed: 150_000, Relations: 150_000, MinPerEntity: 1_000, MaxPerEntity: 2_500,
        Orders: 40_000, OrderDaysBack: 180, OrderDaysAhead: 30);

    public static TestDatasetProfile Get(string name) => name switch
    {
        TestDatasets.Average => Average,
        TestDatasets.Large => Large,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown test dataset.")
    };

    /// <summary>The number of records of one master-data type: random between the profile's bounds.</summary>
    public int NextCount(Random random) => random.Next(MinPerEntity, MaxPerEntity + 1);
}
