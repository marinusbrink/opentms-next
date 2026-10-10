using System;

namespace OpenTms.Platform.Administration;

public record AdministrationOrganizationUnitRowDto
{
    public Guid Id { get; init; }
    public string DisplayName { get; init; } = default!;
    public string Code { get; init; } = default!;
    public Guid? ParentId { get; init; }
    public string? ParentDisplayName { get; init; }
    /// <summary>The display names from the root down to this unit, joined with " / ".</summary>
    public string Path { get; init; } = default!;
    public bool HasChildren { get; init; }
    public long MemberCount { get; init; }
    public DateTime CreationTime { get; init; }
    public DateTime? LastModificationTime { get; init; }
}
