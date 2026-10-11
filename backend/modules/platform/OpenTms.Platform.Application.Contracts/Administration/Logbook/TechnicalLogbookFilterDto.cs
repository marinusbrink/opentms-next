using System;
using System.ComponentModel.DataAnnotations;

namespace OpenTms.Platform.Administration.Logbook;

/// <summary>The explicit filters of the technical logbook (UI-GRID-005); all optional, combined with AND.</summary>
public record TechnicalLogbookFilterDto
{
    /// <summary>Lower bound (inclusive) of the execution time, UTC.</summary>
    public DateTime? From { get; init; }

    /// <summary>Upper bound (exclusive) of the execution time, UTC.</summary>
    public DateTime? To { get; init; }

    /// <summary>Case-insensitive "contains" on the user name.</summary>
    [MaxLength(256)]
    public string? UserName { get; init; }

    /// <summary>Exact HTTP method (GET, POST, …), case-insensitive.</summary>
    [MaxLength(16)]
    public string? HttpMethod { get; init; }

    /// <summary>Case-insensitive "contains" on the request URL.</summary>
    [MaxLength(500)]
    public string? Url { get; init; }

    public int? HttpStatusCode { get; init; }

    /// <summary>True: only requests that recorded an exception; false: only requests without one.</summary>
    public bool? HasException { get; init; }

    [MaxLength(64)]
    public string? CorrelationId { get; init; }
}
