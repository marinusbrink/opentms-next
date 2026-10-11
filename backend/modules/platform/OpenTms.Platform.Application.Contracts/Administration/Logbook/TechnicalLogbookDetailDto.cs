using System.Collections.Generic;

namespace OpenTms.Platform.Administration.Logbook;

/// <summary>A technical logbook entry with everything ABP recorded for the request.</summary>
public record TechnicalLogbookDetailDto : TechnicalLogbookRowDto
{
    public string? ClientName { get; init; }
    public string? ClientId { get; init; }
    public string? BrowserInfo { get; init; }
    public string? ImpersonatorUserName { get; init; }
    public string? Exceptions { get; init; }
    public string? Comments { get; init; }
    public List<TechnicalLogbookActionDto> Actions { get; init; } = new();
    public List<FunctionalLogbookDetailDto> EntityChanges { get; init; } = new();
}
