using System;
using System.Linq;
using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace OpenTms.Hosting;

/// <summary>
/// Keeps the one-time token of a link to choose a password out of the logs (constitution rule 5): the mail link's first
/// request carries it in the query string, which ASP.NET Core's request lines log ("Request starting … ?resetToken=…").
/// Every string property holding <c>resetToken=</c> gets the value replaced; the rendered message follows, since it is
/// rendered from the properties. The reset-password page moves the token into a cookie at once, so no later request
/// carries it (docs/designs/62-sign-in-tenant-detection.md, "Password recovery").
/// </summary>
public sealed partial class PasswordTokenRedactingEnricher : ILogEventEnricher
{
    public const string Redacted = "REDACTED";

    [GeneratedRegex("(resetToken=)[^&\\s\"]*", RegexOptions.IgnoreCase)]
    private static partial Regex Token();

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var (name, value) in logEvent.Properties.ToList())
        {
            if (value is ScalarValue { Value: string text } && text.Contains("resetToken=", StringComparison.OrdinalIgnoreCase))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, new ScalarValue(Token().Replace(text, "$1" + Redacted))));
            }
        }
    }
}
