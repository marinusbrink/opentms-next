using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace OpenTms.Integrations.Routing;

/// <summary>
/// The OSRM adapter against recorded answers (design 60, test risk analysis: "OSRM provider: request shape, parsing,
/// metres/seconds exact, error → null", High). Request shape: longitude first, overview off unless geometry is asked,
/// the table with both annotations. Parsing: metres and seconds rounded to the unit, never to kilometres or minutes.
/// Failure (constitution rule 3, review B1): OSRM's "no path" codes give null; a 5xx or 429, another non-Ok code, a
/// timeout, an unreachable host or a body that is not OSRM's shape throw <see cref="RoutingUnavailableException"/> so the
/// job is retried; every failure logs one warning that carries the status code only.
/// </summary>
public class OsrmRoutingProviderTests
{
    private readonly RoutingHttpHandler _handler = new();
    private readonly CapturingLogger<OsrmRoutingProvider> _log = new();

    private OsrmRoutingProvider Provider(RoutingIntegrationOptions? options = null)
    {
        var provider = RoutingHarness.Osrm(_handler, options);
        provider.Logger = _log;
        return provider;
    }

    [Fact]
    public async Task A_route_is_asked_longitude_first_without_overview_and_parsed_to_whole_metres_and_seconds()
    {
        _handler.ThenJson(RoutingRecorded.OsrmRouteOk);

        var route = await Provider().GetRouteAsync([RoutingHarness.Amsterdam, RoutingHarness.Rotterdam], VehicleSpec.Default, false, CancellationToken.None);

        var request = _handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Get);
        request.Uri.Host.ShouldBe("osrm.invalid");
        request.PathAndQuery.ShouldBe("/route/v1/driving/4.8952,52.3702;4.47917,51.9225?overview=false");

        route.ShouldNotBeNull();
        var leg = route.Legs.ShouldHaveSingleItem();
        leg.DistanceM.ShouldBe(72485, "72484.6 m rounded to the metre, not to 72 km");
        leg.DurationS.ShouldBe(3712, "3712.4 s rounded to the second, not to 62 minutes");
        route.EncodedPolyline.ShouldBeNull();
        route.TollAmount.ShouldBeNull();
        route.Co2Kg.ShouldBeNull("OSRM knows no toll or CO₂");
    }

    [Fact]
    public async Task Several_points_give_one_leg_per_consecutive_pair_with_half_metres_rounded_away_from_zero()
    {
        _handler.ThenJson(RoutingRecorded.OsrmRouteTwoLegs);

        var route = await Provider().GetRouteAsync([RoutingHarness.Amsterdam, RoutingHarness.Utrecht, RoutingHarness.Rotterdam], null, false, CancellationToken.None);

        route.ShouldNotBeNull();
        route.Legs.Count.ShouldBe(2);
        route.Legs[0].ShouldBe(new ProviderLeg(41235, 1800));
        route.Legs[1].ShouldBe(new ProviderLeg(57800, 2400));
        route.TotalDistanceM.ShouldBe(99035);
        route.TotalDurationS.ShouldBe(4200);
    }

    [Fact]
    public async Task Geometry_is_asked_as_a_full_polyline_overview_and_returned_encoded()
    {
        _handler.ThenJson(RoutingRecorded.OsrmRouteWithGeometry);

        var route = await Provider().GetRouteAsync([RoutingHarness.Amsterdam, RoutingHarness.Rotterdam], null, true, CancellationToken.None);

        _handler.Requests.ShouldHaveSingleItem().PathAndQuery.ShouldEndWith("?overview=full&geometries=polyline");
        route.ShouldNotBeNull().EncodedPolyline.ShouldBe("_p~iF~ps|U_ulLnnqC_mqNvxq`@");
    }

    [Fact]
    public async Task The_table_asks_distance_and_duration_annotations_and_gives_every_cell_in_whole_units()
    {
        _handler.ThenJson(RoutingRecorded.OsrmTableOk);

        var table = await Provider().GetTableAsync([RoutingHarness.Amsterdam, RoutingHarness.Rotterdam, RoutingHarness.Utrecht], null, CancellationToken.None);

        _handler.Requests.ShouldHaveSingleItem().PathAndQuery
            .ShouldBe("/table/v1/driving/4.8952,52.3702;4.47917,51.9225;5.1214,52.0907?annotations=distance,duration");
        table.ShouldNotBeNull();
        table[0, 0].ShouldBe(new ProviderLeg(0, 0));
        table[0, 1].ShouldBe(new ProviderLeg(72485, 3712));
        table[1, 0].ShouldBe(new ProviderLeg(72510, 3720), "the matrix is directed");
        table[2, 1].ShouldBe(new ProviderLeg(58950, 3015));
    }

    [Fact]
    public async Task An_unreachable_pair_in_the_table_is_a_null_cell_not_a_failure()
    {
        _handler.ThenJson(RoutingRecorded.OsrmTableUnreachable);

        var table = await Provider().GetTableAsync([RoutingHarness.Amsterdam, RoutingHarness.Rotterdam], null, CancellationToken.None);

        table.ShouldNotBeNull();
        table[0, 1].ShouldBeNull();
        table[0, 0].ShouldBe(new ProviderLeg(0, 0));
    }

    [Fact]
    public async Task A_code_other_than_Ok_is_null_and_one_warning_with_the_status_and_code_only()
    {
        _handler.ThenStatus(HttpStatusCode.BadRequest, RoutingRecorded.OsrmNoRoute);

        var route = await Provider().GetRouteAsync([RoutingHarness.Amsterdam, RoutingHarness.Rotterdam], null, false, CancellationToken.None);

        route.ShouldBeNull();
        var warning = _log.Entries.ShouldHaveSingleItem();
        warning.Level.ShouldBe(LogLevel.Warning);
        warning.Message.ShouldContain("400");
        warning.Message.ShouldContain("NoRoute");
        warning.Message.ShouldNotContain("52.37", Case.Insensitive, "no coordinates in the log");
        warning.Message.ShouldNotContain("Impossible route");
    }

    [Theory]
    [InlineData("NoSegment")]
    [InlineData("NoTable")]
    public async Task Every_no_path_code_is_null_not_an_outage(string code)
    {
        _handler.ThenStatus(HttpStatusCode.BadRequest, "{\"code\":\"" + code + "\"}");

        (await Provider().GetTableAsync([RoutingHarness.Amsterdam, RoutingHarness.Rotterdam], null, CancellationToken.None)).ShouldBeNull();
        _handler.Requests.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task A_server_error_or_too_many_requests_throws_routing_unavailable(HttpStatusCode status)
    {
        _handler.ThenStatus(status, """{"code":"InvalidQuery"}""");

        var error = await Should.ThrowAsync<RoutingUnavailableException>(() => Provider().GetRouteAsync([RoutingHarness.Amsterdam, RoutingHarness.Rotterdam], null, false, CancellationToken.None));

        error.Failure.ShouldBe(RoutingFailure.HttpStatus);
        error.StatusCode.ShouldBe((int)status);
        error.Code.ShouldBe(IntegrationsErrorCodes.RoutingUnavailable);
        error.Message.ShouldNotContain("52.37", Case.Insensitive, "no coordinates in the exception");
        _log.Entries.ShouldHaveSingleItem().Message.ShouldContain(((int)status).ToString());
    }

    [Fact]
    public async Task A_server_error_whose_body_is_not_json_still_throws_with_its_status()
    {
        _handler.ThenStatus(HttpStatusCode.ServiceUnavailable, "<html>upstream down</html>");

        var error = await Should.ThrowAsync<RoutingUnavailableException>(() => Provider().GetRouteAsync([RoutingHarness.Amsterdam, RoutingHarness.Rotterdam], null, false, CancellationToken.None));

        error.Failure.ShouldBe(RoutingFailure.HttpStatus);
        error.StatusCode.ShouldBe(503);
    }

    [Fact]
    public async Task A_body_that_is_not_json_throws_routing_unavailable()
    {
        _handler.ThenStatus(HttpStatusCode.OK, "<html>gateway</html>");

        var error = await Should.ThrowAsync<RoutingUnavailableException>(() => Provider().GetRouteAsync([RoutingHarness.Amsterdam, RoutingHarness.Rotterdam], null, false, CancellationToken.None));

        error.Failure.ShouldBe(RoutingFailure.InvalidAnswer);
        _log.Entries.ShouldHaveSingleItem().Message.ShouldContain("not JSON");
    }

    [Fact]
    public async Task An_ok_answer_of_the_wrong_shape_throws_routing_unavailable()
    {
        _handler.ThenJson("""{"code":"Ok","routes":[]}""");

        var error = await Should.ThrowAsync<RoutingUnavailableException>(() => Provider().GetRouteAsync([RoutingHarness.Amsterdam, RoutingHarness.Rotterdam], null, false, CancellationToken.None));

        error.Failure.ShouldBe(RoutingFailure.InvalidAnswer);
        _log.Entries.ShouldHaveSingleItem().Message.ShouldContain("unexpected shape");
    }

    [Fact]
    public async Task An_unreachable_host_throws_routing_unavailable_with_the_request_error_only()
    {
        _handler.ThenThrow(new HttpRequestException(HttpRequestError.ConnectionError, "connection refused"));

        var error = await Should.ThrowAsync<RoutingUnavailableException>(() => Provider().GetTableAsync([RoutingHarness.Amsterdam, RoutingHarness.Rotterdam], null, CancellationToken.None));

        error.Failure.ShouldBe(RoutingFailure.Unreachable);
        var warning = _log.Entries.ShouldHaveSingleItem();
        warning.Message.ShouldContain("ConnectionError");
        warning.Message.ShouldNotContain("refused");
    }

    [Fact]
    public async Task A_timeout_throws_routing_unavailable()
    {
        _handler.ThenHang();

        var error = await Should.ThrowAsync<RoutingUnavailableException>(() => Provider().GetRouteAsync([RoutingHarness.Amsterdam, RoutingHarness.Rotterdam], null, false, CancellationToken.None));

        error.Failure.ShouldBe(RoutingFailure.Timeout);
        _log.Entries.ShouldHaveSingleItem().Message.ShouldContain("timed out");
    }

    [Fact]
    public async Task The_callers_own_cancellation_is_not_an_outage()
    {
        _handler.ThenHang();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var thrown = await Record.ExceptionAsync(() => Provider().GetRouteAsync([RoutingHarness.Amsterdam, RoutingHarness.Rotterdam], null, false, cancelled.Token));

        thrown.ShouldBeAssignableTo<OperationCanceledException>("a cancelled job is not retried as an outage");
    }

    [Fact]
    public async Task Without_a_base_url_the_provider_is_not_configured_and_never_calls()
    {
        var provider = Provider(RoutingHarness.Options(osrm: false));

        provider.IsConfigured.ShouldBeFalse();
        (await provider.GetRouteAsync([RoutingHarness.Amsterdam, RoutingHarness.Rotterdam], null, false, CancellationToken.None)).ShouldBeNull();
        (await provider.ProbeAsync(CancellationToken.None)).ShouldBeFalse();
        _handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_probe_is_true_for_any_osrm_answer_with_a_code_and_false_otherwise()
    {
        _handler.ThenStatus(HttpStatusCode.BadRequest, """{"code":"NoSegment","message":"Could not find a matching segment"}""");
        (await Provider().ProbeAsync(CancellationToken.None)).ShouldBeTrue("the engine answered, whatever it thinks of the point");
        _handler.Requests[0].PathAndQuery.ShouldStartWith("/nearest/v1/driving/");

        _handler.ThenStatus(HttpStatusCode.BadGateway, "<html>502</html>");
        (await Provider().ProbeAsync(CancellationToken.None)).ShouldBeFalse();

        _handler.ThenThrow(new HttpRequestException(HttpRequestError.NameResolutionError));
        (await Provider().ProbeAsync(CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task Fewer_than_two_points_is_null_without_a_call()
    {
        (await Provider().GetRouteAsync([RoutingHarness.Amsterdam], null, false, CancellationToken.None)).ShouldBeNull();
        (await Provider().GetTableAsync([], null, CancellationToken.None)).ShouldBeNull();
        _handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public void Coordinates_use_the_invariant_culture()
    {
        using (new CultureScope("nl-NL"))
        {
            OsrmRoutingProvider.Coordinates([new GeoPoint(52.5, 4.25)]).ShouldBe("4.25,52.5");
        }
    }
}

/// <summary>One captured log line of a provider, as a sink would see it.</summary>
public sealed record CapturedLog(LogLevel Level, string Message);

/// <summary>Captures everything at Information and above for one provider under test.</summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    public List<CapturedLog> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (IsEnabled(logLevel))
        {
            Entries.Add(new CapturedLog(logLevel, formatter(state, exception)));
        }
    }
}
