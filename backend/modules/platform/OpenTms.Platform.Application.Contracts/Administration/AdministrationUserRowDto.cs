using System;
using System.Collections.Generic;

namespace OpenTms.Platform.Administration;

public record AdministrationUserRowDto
{
    public Guid Id { get; init; }
    public string UserName { get; init; } = default!;
    public string Email { get; init; } = default!;
    public string? Name { get; init; }
    public string? Surname { get; init; }
    public bool IsActive { get; init; }
    public List<string> RoleNames { get; init; } = new();
    public DateTime CreationTime { get; init; }
}
