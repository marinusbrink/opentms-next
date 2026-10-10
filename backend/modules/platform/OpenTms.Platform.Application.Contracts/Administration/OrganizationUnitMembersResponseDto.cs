namespace OpenTms.Platform.Administration;

public record OrganizationUnitMembersResponseDto
{
    /// <summary>Users whose membership changed. Users already in (or already out of) the unit are not counted.</summary>
    public int AffectedCount { get; init; }
}
