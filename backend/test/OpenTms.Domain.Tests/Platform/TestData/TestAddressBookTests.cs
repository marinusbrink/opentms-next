using System;
using System.Collections.Generic;
using System.Linq;
using Shouldly;
using Xunit;

namespace OpenTms.Platform.TestData;

/// <summary>
/// <see cref="TestAddressBook"/> (docs/designs/65-tenant-test-datasets.md): real addresses drawn without repeats until a
/// country is used up, in a 70/15/15 NL/BE/DE mix. Risk class: Critical by the run it feeds (Platform).
/// </summary>
public class TestAddressBookTests
{
    private static List<TestAddress> Addresses(string country, int count) =>
        Enumerable.Range(1, count)
            .Select(i => new TestAddress(country, $"Straat {country}", i.ToString(), null, $"{1000 + i}", $"Stad {country}", 52, 5))
            .ToList();

    private static List<TestAddress> Mixed(int nl, int be, int de) =>
        Addresses("NL", nl).Concat(Addresses("BE", be)).Concat(Addresses("DE", de)).ToList();

    [Fact]
    public void Next_country_never_repeats_until_the_country_is_exhausted()
    {
        var book = new TestAddressBook(Mixed(50, 20, 10), new Random(1));

        var drawn = Enumerable.Range(0, 50).Select(_ => book.Next("NL")).ToList();

        drawn.Distinct().Count().ShouldBe(50);
        drawn.ShouldAllBe(a => a.CountryCode == "NL");

        /* Exhausted: the draw starts over in a new order, still only NL. */
        var second = Enumerable.Range(0, 50).Select(_ => book.Next("NL")).ToList();
        second.Distinct().Count().ShouldBe(50);
        second.ShouldBe(drawn, ignoreOrder: true);
    }

    [Fact]
    public void Next_without_country_never_repeats_while_every_country_has_addresses_left()
    {
        var book = new TestAddressBook(Mixed(2_000, 2_000, 2_000), new Random(2));

        var drawn = Enumerable.Range(0, 1_500).Select(_ => book.Next()).ToList();

        drawn.Distinct().Count().ShouldBe(1_500);
    }

    [Fact]
    public void Next_and_Next_country_share_the_position_so_no_address_is_handed_out_twice()
    {
        var book = new TestAddressBook(Mixed(30, 30, 30), new Random(3));
        var drawn = new List<TestAddress>();
        for (var i = 0; i < 10; i++)
        {
            drawn.Add(book.Next("NL"));
            drawn.Add(book.Next());
        }

        drawn.Distinct().Count().ShouldBe(drawn.Count);
    }

    [Fact]
    public void The_country_mix_is_roughly_70_15_15()
    {
        var book = new TestAddressBook(Mixed(20_000, 20_000, 20_000), new Random(4));
        const int draws = 20_000;

        var counts = Enumerable.Range(0, draws).Select(_ => book.Next().CountryCode)
            .GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count() / (double)draws);

        counts["NL"].ShouldBe(0.70, 0.02);
        counts["BE"].ShouldBe(0.15, 0.02);
        counts["DE"].ShouldBe(0.15, 0.02);
        TestAddressBook.CountryMix.Values.Sum().ShouldBe(1.0, 1e-9);
    }

    [Fact]
    public void A_register_with_one_country_always_draws_that_country()
    {
        var book = new TestAddressBook(Addresses("BE", 100), new Random(5));

        Enumerable.Range(0, 300).Select(_ => book.Next().CountryCode).Distinct().ShouldBe(new[] { "BE" });
        book.Countries.ShouldBe(new[] { "BE" });
    }

    [Fact]
    public void Count_reports_per_country_and_zero_for_a_missing_one()
    {
        var book = new TestAddressBook(Mixed(5, 3, 2), new Random(6));

        book.Count("NL").ShouldBe(5);
        book.Count("BE").ShouldBe(3);
        book.Count("DE").ShouldBe(2);
        book.Count("FR").ShouldBe(0);
        book.Countries.ShouldBe(new[] { "NL", "BE", "DE" }, ignoreOrder: true);
    }

    [Fact]
    public void Next_for_a_country_without_addresses_throws()
    {
        var book = new TestAddressBook(Addresses("NL", 3), new Random(7));

        Should.Throw<ArgumentException>(() => book.Next("FR"));
    }

    [Fact]
    public void An_empty_register_is_refused()
    {
        Should.Throw<InvalidOperationException>(() => new TestAddressBook(Array.Empty<TestAddress>(), new Random(8)));
    }

    [Fact]
    public void The_same_seed_draws_the_same_addresses()
    {
        var source = Mixed(200, 50, 50);
        List<TestAddress> Draw(int seed)
        {
            var book = new TestAddressBook(source, new Random(seed));
            return Enumerable.Range(0, 120).Select(_ => book.Next()).ToList();
        }

        Draw(9).ShouldBe(Draw(9));
        Draw(9).ShouldNotBe(Draw(10));
    }

    [Fact]
    public void The_caller_list_is_not_reordered()
    {
        var source = Mixed(50, 10, 10);
        var copy = source.ToList();

        var book = new TestAddressBook(source, new Random(11));
        for (var i = 0; i < 100; i++)
        {
            book.Next();
        }

        source.ShouldBe(copy);
    }
}
