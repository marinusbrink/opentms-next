using System;

namespace OpenTms.Platform.Administration;

public record AdministrationRoleRowDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = default!;
    public bool IsDefault { get; init; }
    public bool IsPublic { get; init; }
    public long UserCount { get; init; }
    public bool IsStatic { get; init; }
}
