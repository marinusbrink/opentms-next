using System;

namespace OpenTms.Platform.Administration.Logbook;

/// <summary>One application-service call inside a request (ABP <c>AuditLogAction</c>).</summary>
public record TechnicalLogbookActionDto
{
    public Guid Id { get; init; }
    public string? ServiceName { get; init; }
    public string? MethodName { get; init; }

    /// <summary>The call's arguments as ABP serialised them (JSON).</summary>
    public string? Parameters { get; init; }

    public DateTime ExecutionTime { get; init; }

    /// <summary>Milliseconds.</summary>
    public int ExecutionDuration { get; init; }
}
