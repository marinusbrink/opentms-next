using System;

namespace OpenTms.Platform.Administration;

public record SkippedRowDto
{
    public Guid Id { get; init; }
    public string UserName { get; init; } = default!;
    public string Reason { get; init; } = default!;
}
