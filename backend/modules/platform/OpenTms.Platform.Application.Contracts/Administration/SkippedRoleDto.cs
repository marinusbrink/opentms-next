using System;

namespace OpenTms.Platform.Administration;

public record SkippedRoleDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = default!;
    public string Reason { get; init; } = default!;
}
