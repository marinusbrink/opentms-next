using System;

namespace OpenTms.Platform.Administration.Logbook;

/// <summary>One request of the technical logbook (ABP <c>AuditLog</c>).</summary>
public record TechnicalLogbookRowDto
{
    public Guid Id { get; init; }
    public DateTime ExecutionTime { get; init; }

    /// <summary>Milliseconds.</summary>
    public int ExecutionDuration { get; init; }

    public Guid? UserId { get; init; }
    public string? UserName { get; init; }
    public string? HttpMethod { get; init; }
    public string? Url { get; init; }
    public int? HttpStatusCode { get; init; }
    public string? ClientIpAddress { get; init; }
    public string? ApplicationName { get; init; }
    public string? CorrelationId { get; init; }
    public bool HasException { get; init; }

    /// <summary>How many entity changes (functional logbook entries) this request produced.</summary>
    public int EntityChangeCount { get; init; }
}
