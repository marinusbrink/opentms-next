using System;
using System.Linq;
using Serilog.Events;
using Serilog.Parsing;
using Shouldly;
using Xunit;

namespace OpenTms.Hosting;

/// <summary>
/// The one-time token of a password link never reaches a log line (constitution rule 5): ASP.NET Core's request lines log
/// the query string of the mail link's first request. Risk class: Critical — a logged token is a password for a day.
/// </summary>
public class PasswordTokenRedactingEnricherTests
{
    private static LogEvent RequestStarting(string queryString)
    {
        var template = new MessageTemplateParser().Parse("Request starting {Path}{QueryString}");
        return new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Information, null, template,
        [
            new LogEventProperty("Path", new ScalarValue("/Account/ResetPassword")),
            new LogEventProperty("QueryString", new ScalarValue(queryString)),
        ]);
    }

    [Theory]
    [InlineData("?userId=42&__tenant=7&resetToken=CfDJ8Secret%2BToken%3D%3D", "?userId=42&__tenant=7&resetToken=REDACTED")]
    [InlineData("?resetToken=CfDJ8Secret&userId=42", "?resetToken=REDACTED&userId=42")]
    [InlineData("?ResetToken=CfDJ8Secret", "?ResetToken=REDACTED")]
    public void The_token_in_a_query_string_is_redacted_in_the_property_and_the_rendered_line(string queryString, string expected)
    {
        var logEvent = RequestStarting(queryString);

        new PasswordTokenRedactingEnricher().Enrich(logEvent, null!);

        ((ScalarValue)logEvent.Properties["QueryString"]).Value.ShouldBe(expected);
        logEvent.RenderMessage().ShouldNotContain("CfDJ8Secret");
        logEvent.RenderMessage().ShouldBe("Request starting \"/Account/ResetPassword\"\"" + expected + "\"");
    }

    [Fact]
    public void Other_properties_and_query_strings_stay_as_they_are()
    {
        var logEvent = RequestStarting("?ReturnUrl=%2Fconnect%2Fauthorize");

        new PasswordTokenRedactingEnricher().Enrich(logEvent, null!);

        ((ScalarValue)logEvent.Properties["QueryString"]).Value.ShouldBe("?ReturnUrl=%2Fconnect%2Fauthorize");
        ((ScalarValue)logEvent.Properties["Path"]).Value.ShouldBe("/Account/ResetPassword");
        logEvent.Properties.Count.ShouldBe(2);
        logEvent.Properties.Keys.Order().ShouldBe(["Path", "QueryString"]);
    }
}
