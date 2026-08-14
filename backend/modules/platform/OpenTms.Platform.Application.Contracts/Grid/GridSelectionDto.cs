using System;
using System.Collections.Generic;

namespace OpenTms.Platform.Grid;

public record GridSelectionDto
{
    public string Mode { get; init; } = "Explicit";

    public List<Guid> ExplicitIds { get; init; } = new();

    public GridRequest? FilterRequest { get; init; }

    public List<Guid> ExcludedIds { get; init; } = new();
}
