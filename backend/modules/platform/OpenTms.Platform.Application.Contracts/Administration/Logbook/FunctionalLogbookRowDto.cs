using System;

namespace OpenTms.Platform.Administration.Logbook;

/// <summary>One entity change of the functional logbook (ABP <c>EntityChange</c>).</summary>
public record FunctionalLogbookRowDto
{
    public Guid Id { get; init; }

    /// <summary>The technical logbook entry (request) that made the change.</summary>
    public Guid AuditLogId { get; init; }

    public DateTime ChangeTime { get; init; }

    /// <summary>One of <see cref="LogbookChangeTypes"/>.</summary>
    public string ChangeType { get; init; } = default!;

    public string EntityTypeFullName { get; init; } = default!;

    /// <summary>The last segment of <see cref="EntityTypeFullName"/>: the entity's short name.</summary>
    public string EntityTypeName { get; init; } = default!;

    public string? EntityId { get; init; }
    public Guid? UserId { get; init; }
    public string? UserName { get; init; }
}
