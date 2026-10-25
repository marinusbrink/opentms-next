using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;
using Volo.Abp.DependencyInjection;

namespace OpenTms.RecordUpdates;

/// <summary>
/// Removes the SignalR <c>access_token</c> query value from every log property (design 49, security quickscan): ASP.NET
/// Core's request logging writes the query string, and a bearer token in a log is a credential leak. Registered in DI,
/// so <c>ReadFrom.Services</c> in Program.cs applies it to every event.
/// </summary>
public partial class AccessTokenRedactionEnricher : ILogEventEnricher, ISingletonDependency
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var (name, value) in logEvent.Properties)
        {
            if (value is ScalarValue { Value: string text } && text.Contains("access_token=", System.StringComparison.OrdinalIgnoreCase))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, new ScalarValue(Redact(text))));
            }
        }
    }

    public static string Redact(string text) => TokenValue().Replace(text, "$1***");

    [GeneratedRegex("(access_token=)[^&\\s\"]*", RegexOptions.IgnoreCase)]
    private static partial Regex TokenValue();
}
