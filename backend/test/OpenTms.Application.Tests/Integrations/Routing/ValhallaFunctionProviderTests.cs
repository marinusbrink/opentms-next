using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTms.MasterData.Fleet;
using Shouldly;
using Xunit;

namespace OpenTms.Integrations.Routing;

/// <summary>
/// The route-function adapter against the contract of the assessment appendix §1.4 (design 60, test risk analysis:
/// "Function provider: contract, retry without vehicle, toll/CO₂ parsing", High). The POST carries origin, destination,
/// the waypoints between them and the vehicle block; the answer gives one leg (the function knows only the total), the
/// polyline, the toll and the CO₂e in kilograms with three decimals. A 442 or a "no path" error body is retried once
/// without the vehicle and the answer then carries no toll and no CO₂. Any other failure is null with the status only.
/// </summary>
public class ValhallaFunctionProviderTests
{
    private readonly RoutingHttpHandler _handler = new();
    private readonly CapturingLogger<ValhallaFunctionProvider> _log = new();

    private static readonly VehicleSpec Truck = new(18_000, EuroClass.Euro5, FuelType.Lng, 28.5, 400, 255, 1650);

    private ValhallaFunctionProvider Provider(RoutingIntegrationOptions? options = null)
    {
        var provider = RoutingHarness.Function(_handler, options);
        provider.Logger = _log;
        return provider;
    }

    [Fact]
    public async Task The_request_is_one_post_with_origin_destination_waypoints_and_the_vehicle_block()
    {
        _handler.ThenJson(RoutingRecorded.FunctionOk);

        await Provider().CalculateAsync([RoutingHarness.Amsterdam, RoutingHarness.Utrecht, RoutingHarness.Rotterdam], Truck, CancellationToken.None);

        var request = _handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Uri.ShouldBe(new System.Uri(RoutingHarness.FunctionUrl));
        var body = request.Json;
        body.GetProperty("origin").GetProperty("lat").GetDouble().ShouldBe(52.3702);
        body.GetProperty("origin").GetProperty("lon").GetDouble().ShouldBe(4.8952);
        body.GetProperty("destination").GetProperty("lat").GetDouble().ShouldBe(51.9225);
        var waypoints = body.GetProperty("waypoints");
        waypoints.GetArrayLength().ShouldBe(1);
        waypoints[0].GetProperty("lon").GetDouble().ShouldBe(5.1214);
        var vehicle = body.GetProperty("vehicle");
        vehicle.GetProperty("weight").GetDouble().ShouldBe(18.0, "tonnes, as the legacy route calculator sends them");
        vehicle.GetProperty("euroClass").GetString().ShouldBe("EURO_V");
        vehicle.GetProperty("height").GetDouble().ShouldBe(4.0);
        vehicle.GetProperty("width").GetDouble().ShouldBe(2.55);
        vehicle.GetProperty("length").GetDouble().ShouldBe(16.5);
        vehicle.TryGetProperty("fuelType", out _).ShouldBeFalse("LNG has no GLEC name at the route function: left out");
        vehicle.GetProperty("fuelConsumption").GetDouble().ShouldBe(0.285, "litres per kilometre");
        request.Headers.ShouldNotContainKey("X-API-Key", "no key configured, no header");
    }

    [Fact]
    public async Task Without_a_vehicle_the_default_truck_is_sent_and_unknown_parts_are_left_out()
    {
        _handler.ThenJson(RoutingRecorded.FunctionOk);

        await Provider().CalculateAsync([RoutingHarness.Amsterdam, RoutingHarness.Rotterdam], null, CancellationToken.None);

        var vehicle = _handler.Requests.ShouldHaveSingleItem().Json.GetProperty("vehicle");
        vehicle.GetProperty("weight").GetDouble().ShouldBe(40.0, "OpenTMS's toll default: 40 t");
        vehicle.GetProperty("euroClass").GetString().ShouldBe("EURO_VI");
        vehicle.GetProperty("fuelType").GetString().ShouldBe("Diesel");
        vehicle.TryGetProperty("fuelConsumption", out _).ShouldBeFalse("null properties are left out");
        vehicle.TryGetProperty("height", out _).ShouldBeFalse();
        _handler.Requests[0].Json.GetProperty("waypoints").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task The_configured_key_travels_in_the_configured_header()
    {
        _handler.ThenJson(RoutingRecorded.FunctionOk);

        await Provider(RoutingHarness.Options(apiKey: "qq-function-key")).CalculateAsync([RoutingHarness.Amsterdam, RoutingHarness.Rotterdam], null, CancellationToken.None);

        _handler.Requests.ShouldHaveSingleItem().Headers["X-API-Key"].ShouldBe("qq-function-key");
    }

    [Fact]
    public async Task Route_toll_and_carbon_footprint_are_parsed_to_whole_units_money_and_kilograms_with_three_decimals()
    {
        _handler.ThenJson(RoutingRecorded.FunctionOk);

        var route = await Provider().CalculateAsync([RoutingHarness.Amsterdam, RoutingHarness.Rotterdam], Truck, CancellationToken.None);

        route.ShouldNotBeNull();
        route.Legs.ShouldHaveSingleItem().ShouldBe(new ProviderLeg(74210, 3806));
        route.EncodedPolyline.ShouldBe("_p~iF~ps|U_ulLnnqC_mqNvxq`@");
        route.TollDistanceM.ShouldBe(51231);
        route.TollAmount.ShouldBe(12.35m, "money with two decimals");
        route.TollCurrencyCode.ShouldBe("EUR");
        route.Co2Kg.ShouldBe(58.123m, "kilograms with three decimals");
        route.VehicleDropped.ShouldBeFalse();
    }

    [Theory]
    [InlineData(442, "{}")]
    [InlineData(400, RoutingRecorded.FunctionNoPathBody)]
    public async Task No_truck_path_is_retried_once_without_the_vehicle_and_toll_and_co2_are_dropped(int status, string body)
    {
        _handler.ThenStatus(status, body).ThenJson(RoutingRecorded.FunctionNoVehicle);

        var route = await Provider().CalculateAsync([RoutingHarness.Amsterdam, RoutingHarness.Rotterdam], Truck, CancellationToken.None);

        _handler.Requests.Count.ShouldBe(2);
        _handler.Requests[0].Json.TryGetProperty("vehicle", out _).ShouldBeTrue();
        _handler.Requests[1].Json.TryGetProperty("vehicle", out _).ShouldBeFalse("the retry goes without the vehicle");
        route.ShouldNotBeNull();
        route.Legs.ShouldHaveSingleItem().ShouldBe(new ProviderLeg(73000, 3700));
        route.EncodedPolyline.ShouldBe("abcd");
        route.TollAmount.ShouldBeNull();
        route.TollDistanceM.ShouldBeNull();
        route.Co2Kg.ShouldBeNull();
        route.VehicleDropped.ShouldBeTrue();
    }

    [Fact]
    public async Task A_retry_that_also_fails_is_null_after_exactly_two_attempts()
    {
        _handler.ThenStatus(ValhallaFunctionProvider.NoPathStatusCode).ThenStatus(ValhallaFunctionProvider.NoPathStatusCode);

        (await Provider().CalculateAsync([RoutingHarness.Amsterdam, RoutingHarness.Rotterdam], Truck, CancellationToken.None)).ShouldBeNull();
        _handler.Requests.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Any_other_error_is_null_after_one_attempt_with_the_status_only_in_the_log(HttpStatusCode status)
    {
        _handler.ThenStatus(status, """{"error":"secret details 52.3702"}""");

        (await Provider().CalculateAsync([RoutingHarness.Amsterdam, RoutingHarness.Rotterdam], Truck, CancellationToken.None)).ShouldBeNull();
        _handler.Requests.Count.ShouldBe(1);
        var warning = _log.Entries.ShouldHaveSingleItem();
        warning.Level.ShouldBe(LogLevel.Warning);
        warning.Message.ShouldContain(((int)status).ToString());
        warning.Message.ShouldNotContain("secret");
        warning.Message.ShouldNotContain("52.3702");
    }

    [Fact]
    public async Task A_timeout_or_an_unreachable_host_is_null()
    {
        _handler.ThenHang();
        (await Provider().CalculateAsync([RoutingHarness.Amsterdam, RoutingHarness.Rotterdam], null, CancellationToken.None)).ShouldBeNull();

        _handler.ThenThrow(new HttpRequestException(HttpRequestError.ConnectionError));
        (await Provider().CalculateAsync([RoutingHarness.Amsterdam, RoutingHarness.Rotterdam], null, CancellationToken.None)).ShouldBeNull();

        _log.Entries.Count.ShouldBe(2);
        _log.Entries[0].Message.ShouldContain("timed out");
        _log.Entries[1].Message.ShouldContain("ConnectionError");
    }

    [Fact]
    public async Task An_answer_without_a_route_is_null()
    {
        _handler.ThenJson("""{"message":"ok"}""");

        (await Provider().CalculateAsync([RoutingHarness.Amsterdam, RoutingHarness.Rotterdam], null, CancellationToken.None)).ShouldBeNull();
        _log.Entries.ShouldHaveSingleItem().Message.ShouldContain("unexpected shape");
    }

    [Fact]
    public async Task Not_configured_means_no_call_and_null()
    {
        var provider = Provider(RoutingHarness.Options(function: false));

        provider.IsConfigured.ShouldBeFalse();
        provider.Host.ShouldBeNull();
        (await provider.CalculateAsync([RoutingHarness.Amsterdam, RoutingHarness.Rotterdam], null, CancellationToken.None)).ShouldBeNull();
        (await provider.ProbeAsync(CancellationToken.None)).ShouldBeFalse();
        _handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_table_is_one_call_per_ordered_pair_and_the_probe_is_a_get_that_accepts_a_405()
    {
        _handler.Always(RoutingRecorded.FunctionOk);
        var table = await Provider().GetTableAsync([RoutingHarness.Amsterdam, RoutingHarness.Rotterdam, RoutingHarness.Utrecht], null, CancellationToken.None);

        table.ShouldNotBeNull();
        _handler.Requests.Count.ShouldBe(6, "three points, six ordered pairs, the diagonal is free");
        table[0, 0].ShouldBe(new ProviderLeg(0, 0));
        table[0, 1].ShouldBe(new ProviderLeg(74210, 3806));

        _handler.Requests.Clear();
        _handler.ThenStatus(HttpStatusCode.MethodNotAllowed, string.Empty);
        (await Provider().ProbeAsync(CancellationToken.None)).ShouldBeTrue();
        _handler.Requests.ShouldHaveSingleItem().Method.ShouldBe(HttpMethod.Get);

        _handler.ThenStatus(HttpStatusCode.BadGateway, string.Empty);
        (await Provider().ProbeAsync(CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public void The_host_is_the_host_name_only()
    {
        Provider().Host.ShouldBe("route-function.invalid");
    }

    [Theory]
    [InlineData(EuroClass.Euro0, "EURO_0")]
    [InlineData(EuroClass.Euro3, "EURO_III")]
    [InlineData(EuroClass.Euro6, "EURO_VI")]
    [InlineData(EuroClass.Eev, "EURO_V")]
    [InlineData(EuroClass.ZeroEmission, "EURO_VI")]
    public void The_euro_class_travels_as_the_route_functions_name(EuroClass euroClass, string expected)
    {
        ValhallaFunctionProvider.RouteFunctionVehicle.EuroClassName(euroClass).ShouldBe(expected);
    }

    [Theory]
    [InlineData(FuelType.Diesel, "Diesel")]
    [InlineData(FuelType.Petrol, "Petrol")]
    [InlineData(FuelType.Electric, "Electric")]
    [InlineData(FuelType.Cng, "CNG")]
    [InlineData(FuelType.Lng, null)]
    [InlineData(FuelType.Hydrogen, null)]
    public void Only_the_GLEC_fuel_names_travel(FuelType fuelType, string? expected)
    {
        ValhallaFunctionProvider.RouteFunctionVehicle.FuelTypeName(fuelType).ShouldBe(expected);
    }
}
