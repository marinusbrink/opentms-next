using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace OpenTms.Platform.TestData;

/// <summary>
/// <see cref="TestAddressRegister.ParseAsync"/>: the embedded extract of the NL/BE/DE address registers
/// (docs/designs/65-tenant-test-datasets.md, "Address data"). Independent of the embedded file itself, which may be
/// absent in a checkout. Risk class: Critical by the run it feeds; a mis-parsed coordinate or a street cut at a comma
/// puts wrong data in every relation of a dataset.
/// </summary>
public class TestAddressRegisterTests
{
    private const string Header = "country,street,house_number,house_number_suffix,postal_code,city,latitude,longitude";

    private static Task<System.Collections.Generic.IReadOnlyList<TestAddress>> ParseAsync(string text) =>
        TestAddressRegister.ParseAsync(new StringReader(text));

    [Fact]
    public async Task Skips_the_header_and_reads_every_field()
    {
        var addresses = await ParseAsync($"{Header}\nNL,Stationsplein,1,A,3511 ED,Utrecht,52.0894,5.1101\n");

        var a = addresses.ShouldHaveSingleItem();
        a.ShouldBe(new TestAddress("NL", "Stationsplein", "1", "A", "3511 ED", "Utrecht", 52.0894, 5.1101));
    }

    [Fact]
    public async Task Reads_quoted_fields_with_commas_and_escaped_quotes()
    {
        var addresses = await ParseAsync(
            $"{Header}\n" +
            "BE,\"Rue de la Loi, Wetstraat\",16,,1000,\"Bruxelles, Brussel\",50.8466,4.3686\n" +
            "DE,\"Am \"\"Alten\"\" Markt\",3,b,10115,Berlin,52.5321,13.3849\n");

        addresses.Count.ShouldBe(2);
        addresses[0].Street.ShouldBe("Rue de la Loi, Wetstraat");
        addresses[0].City.ShouldBe("Bruxelles, Brussel");
        addresses[0].HouseNumber.ShouldBe("16");
        addresses[0].PostalCode.ShouldBe("1000");
        addresses[0].Latitude.ShouldBe(50.8466);
        addresses[1].Street.ShouldBe("Am \"Alten\" Markt");
        addresses[1].HouseNumberSuffix.ShouldBe("b");
    }

    [Fact]
    public async Task Empty_house_number_and_suffix_become_null()
    {
        var addresses = await ParseAsync($"{Header}\nNL,Dorpsstraat,,,1234 AB,Ergens,52.1,5.2\nNL,Kade,7,,1234 AC,Ergens,52.1,5.2\n");

        addresses[0].HouseNumber.ShouldBeNull();
        addresses[0].HouseNumberSuffix.ShouldBeNull();
        addresses[1].HouseNumber.ShouldBe("7");
        addresses[1].HouseNumberSuffix.ShouldBeNull();
    }

    [Theory]
    [InlineData("nl-NL")]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public async Task Coordinates_are_parsed_invariantly_whatever_the_current_culture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            var a = (await ParseAsync($"{Header}\nDE,Königsallee,92,,40212,Düsseldorf,51.2195,6.7799\n")).ShouldHaveSingleItem();
            a.Latitude.ShouldBe(51.2195);
            a.Longitude.ShouldBe(6.7799);
            a.Street.ShouldBe("Königsallee");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task Skips_blank_and_short_lines()
    {
        var addresses = await ParseAsync($"{Header}\n\nNL,too,short\nNL,Markt,1,,5611 EB,Eindhoven,51.43,5.47\n\n");

        addresses.ShouldHaveSingleItem().City.ShouldBe("Eindhoven");
    }

    [Fact]
    public async Task A_header_only_file_is_empty()
    {
        (await ParseAsync(Header + "\n")).ShouldBeEmpty();
        (await ParseAsync(string.Empty)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Repeated_street_and_city_names_share_one_string_instance()
    {
        var addresses = await ParseAsync(
            $"{Header}\nNL,Markt,1,,5611 EB,Eindhoven,51.43,5.47\nNL,Markt,2,,5611 EB,Eindhoven,51.43,5.47\n");

        ReferenceEquals(addresses[0].Street, addresses[1].Street).ShouldBeTrue();
        ReferenceEquals(addresses[0].City, addresses[1].City).ShouldBeTrue();
    }
}
