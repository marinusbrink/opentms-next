using System;
using System.Linq;
using Shouldly;
using Xunit;

namespace OpenTms.Platform.TestData;

/// <summary>
/// The two fixed profiles of "Reset to dataset" against the design's table (docs/designs/65-tenant-test-datasets.md,
/// "Behaviour") and the run context they seed. Risk class: Critical by the run they size (Platform).
/// </summary>
public class TestDatasetProfileTests
{
    [Fact]
    public void Average_matches_the_design()
    {
        var p = TestDatasetProfile.Average;
        p.Name.ShouldBe(TestDatasets.Average);
        p.Relations.ShouldBe(5_000);
        p.MinPerEntity.ShouldBe(25);
        p.MaxPerEntity.ShouldBe(250);
        p.Orders.ShouldBe(4_000);
        p.OrderDaysBack.ShouldBe(60);
        p.OrderDaysAhead.ShouldBe(14);
    }

    [Fact]
    public void Large_matches_the_design()
    {
        var p = TestDatasetProfile.Large;
        p.Name.ShouldBe(TestDatasets.Large);
        p.Relations.ShouldBe(150_000);
        p.MinPerEntity.ShouldBe(1_000);
        p.MaxPerEntity.ShouldBe(2_500);
        p.Orders.ShouldBe(40_000);
        p.OrderDaysBack.ShouldBe(180);
        p.OrderDaysAhead.ShouldBe(30);
    }

    [Fact]
    public void Every_dataset_name_resolves_to_its_profile_and_the_seeds_differ()
    {
        TestDatasets.All.ShouldBe(new[] { TestDatasets.Average, TestDatasets.Large });
        foreach (var name in TestDatasets.All)
        {
            TestDatasetProfile.Get(name).Name.ShouldBe(name);
        }

        TestDatasetProfile.Average.Seed.ShouldNotBe(TestDatasetProfile.Large.Seed);
    }

    [Theory]
    [InlineData("average")]
    [InlineData("LARGE")]
    [InlineData("")]
    [InlineData("Huge")]
    public void An_unknown_name_is_refused(string name)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => TestDatasetProfile.Get(name));
    }

    [Theory]
    [InlineData(nameof(TestDatasetProfile.Average))]
    [InlineData(nameof(TestDatasetProfile.Large))]
    public void NextCount_stays_within_the_inclusive_bounds_and_reaches_both(string name)
    {
        var profile = TestDatasetProfile.Get(name);
        var random = new Random(1);

        var counts = Enumerable.Range(0, 200_000).Select(_ => profile.NextCount(random)).ToList();

        counts.ShouldAllBe(c => c >= profile.MinPerEntity && c <= profile.MaxPerEntity);
        counts.Min().ShouldBe(profile.MinPerEntity);
        counts.Max().ShouldBe(profile.MaxPerEntity);
    }

    [Fact]
    public void Large_has_over_a_thousand_of_every_other_entity()
    {
        TestDatasetProfile.Large.MinPerEntity.ShouldBeGreaterThanOrEqualTo(1_000);
    }

    /* ── TestDatasetContext ─────────────────────────────────────────────────────────────────── */

    private static TestDatasetContext Context(TestDatasetProfile profile, DateTime today) =>
        new(Guid.NewGuid(), profile, new TestAddressBook(new[] { new TestAddress("NL", "s", "1", null, "1234 AB", "c", 52, 5) }, new Random(0)), today);

    [Fact]
    public void The_context_random_is_seeded_by_the_profile()
    {
        var a = Context(TestDatasetProfile.Average, DateTime.Now);
        var b = Context(TestDatasetProfile.Average, DateTime.Now);
        var c = Context(TestDatasetProfile.Large, DateTime.Now);

        var drawA = Enumerable.Range(0, 20).Select(_ => a.NextCount()).ToList();
        drawA.ShouldBe(Enumerable.Range(0, 20).Select(_ => b.NextCount()).ToList());
        Enumerable.Range(0, 20).Select(_ => c.Random.Next()).ShouldNotBe(Enumerable.Range(0, 20).Select(_ => a.Random.Next()));
    }

    [Fact]
    public void Today_is_the_date_only()
    {
        Context(TestDatasetProfile.Average, new DateTime(2026, 10, 30, 17, 45, 12)).Today.ShouldBe(new DateTime(2026, 10, 30));
    }

    [Fact]
    public void Get_of_an_unpublished_output_names_the_type_and_Find_returns_null()
    {
        var context = Context(TestDatasetProfile.Average, DateTime.Now);

        context.Find<SampleOutput>().ShouldBeNull();
        Should.Throw<InvalidOperationException>(() => context.Get<SampleOutput>()).Message.ShouldContain(nameof(SampleOutput));

        var output = new SampleOutput();
        context.Publish(output);
        context.Get<SampleOutput>().ShouldBeSameAs(output);
    }

    private sealed class SampleOutput;
}
