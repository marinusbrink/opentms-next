using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Platform.Maintenance.Health;

namespace OpenTms.Platform.Administration.Maintenance;

/* One mapping for the live report and the heartbeat rows (HealthCheckEntryDto.Create), and the order of the list. */
internal static class HealthCheckEntryMapper
{
    public static HealthCheckEntryDto ToDto(HealthCheckEntry entry) =>
        HealthCheckEntryDto.Create(entry.Name, entry.Status, entry.Description, entry.Duration, entry.Tags.ToList(), entry.Error, entry.Data);

    /// <summary>Problems first (Unhealthy, then Degraded), the system before external services, then by name.</summary>
    public static IOrderedEnumerable<HealthCheckEntryDto> ProblemsFirst(IEnumerable<HealthCheckEntryDto> entries) =>
        entries
            .OrderBy(e => StatusOrder(e.Status))
            .ThenBy(e => e.Area == HealthCheckEntryDto.CoreArea ? 0 : 1)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase);

    public static int StatusOrder(string status) => status switch
    {
        "Unhealthy" => 0,
        "Degraded" => 1,
        _ => 2
    };
}
