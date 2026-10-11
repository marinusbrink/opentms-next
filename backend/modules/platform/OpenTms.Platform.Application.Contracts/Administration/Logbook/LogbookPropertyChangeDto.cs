using System;

namespace OpenTms.Platform.Administration.Logbook;

/// <summary>One changed property of an entity (ABP <c>EntityPropertyChange</c>).</summary>
public record LogbookPropertyChangeDto
{
    public Guid Id { get; init; }
    public string PropertyName { get; init; } = default!;
    public string? PropertyTypeFullName { get; init; }
    public string? OriginalValue { get; init; }
    public string? NewValue { get; init; }
}
