using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using OpenTms.RecordUpdates;
using Serilog.Events;
using Serilog.Parsing;
using Shouldly;
using Xunit;

namespace OpenTms.RecordUpdates;

/* Design 49, security quickscan: the SignalR client's query-string token becomes a bearer header on the hub paths
 * only and never overrides a real header; and the token never reaches a log line. Risk class: High. */
public class RecordUpdateHostTests
{
    private static async Task<HttpContext> RunAsync(string path, string query, string? authorization = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);
        if (authorization != null)
        {
            context.Request.Headers.Authorization = authorization;
        }

        var reached = false;
        await HubAccessTokenMiddleware.InvokeAsync(context, () =>
        {
            reached = true;
            return Task.CompletedTask;
        });
        reached.ShouldBeTrue("the middleware always calls the next one");
        return context;
    }

    [Fact]
    public async Task The_query_token_becomes_a_bearer_header_on_a_hub_path()
    {
        var context = await RunAsync("/signalr-hubs/record-updates", "?id=abc&access_token=TOKEN123");
        context.Request.Headers.Authorization.ToString().ShouldBe("Bearer TOKEN123");
    }

    [Fact]
    public async Task The_query_token_is_ignored_outside_the_hub_paths()
    {
        var context = await RunAsync("/api/orders/transport-order-type", "?access_token=TOKEN123");
        context.Request.Headers.Authorization.ToString().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_real_authorization_header_is_never_replaced()
    {
        var context = await RunAsync("/signalr-hubs/record-updates", "?access_token=OTHER", "Bearer REAL");
        context.Request.Headers.Authorization.ToString().ShouldBe("Bearer REAL");
    }

    [Theory]
    [InlineData("?id=1&access_token=eyJhbGciOi.abc.def", "?id=1&access_token=***")]
    [InlineData("?access_token=abc&id=2", "?access_token=***&id=2")]
    [InlineData("?id=3", "?id=3")]
    public void The_redaction_removes_the_token_value(string input, string expected)
    {
        AccessTokenRedactionEnricher.Redact(input).ShouldBe(expected);
    }

    [Fact]
    public void The_enricher_redacts_every_property_that_carries_a_token()
    {
        var logEvent = new LogEvent(
            System.DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            new MessageTemplateParser().Parse("Request starting {Path}{QueryString}"),
            [
                new LogEventProperty("Path", new ScalarValue("/signalr-hubs/record-updates")),
                new LogEventProperty("QueryString", new ScalarValue("?id=x&access_token=SECRET")),
            ]);

        new AccessTokenRedactionEnricher().Enrich(logEvent, null!);

        var rendered = logEvent.RenderMessage();
        rendered.ShouldNotContain("SECRET");
        rendered.ShouldContain("access_token=***");
        logEvent.Properties["Path"].ToString().ShouldContain("/signalr-hubs/record-updates");
    }
}
