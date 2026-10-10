namespace OpenTms.Platform.Administration;

/// <summary>Returned with HTTP 409 when a unit still has sub-units or members and the delete was not forced.</summary>
public record OrganizationUnitDeleteCheckDto
{
    public string DisplayName { get; init; } = default!;
    public int ChildCount { get; init; }
    public long MemberCount { get; init; }
}
