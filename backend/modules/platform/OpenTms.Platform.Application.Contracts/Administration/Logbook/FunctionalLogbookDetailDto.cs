using System.Collections.Generic;

namespace OpenTms.Platform.Administration.Logbook;

/// <summary>A functional logbook entry with the property values it changed.</summary>
public record FunctionalLogbookDetailDto : FunctionalLogbookRowDto
{
    public List<LogbookPropertyChangeDto> PropertyChanges { get; init; } = new();
}
