using OpenTms.Platform.Grid;

namespace OpenTms.Platform.Administration;

/// <summary>The users to add to, or remove from, a unit: a grid selection over the members or the unadded users.</summary>
public record OrganizationUnitMembersRequestDto
{
    public GridSelectionDto Selection { get; init; } = new();
}
