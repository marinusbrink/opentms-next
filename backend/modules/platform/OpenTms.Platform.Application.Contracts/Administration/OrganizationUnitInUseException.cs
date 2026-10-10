using Volo.Abp;

namespace OpenTms.Platform.Administration;

public class OrganizationUnitInUseException : BusinessException
{
    public string DisplayName { get; }
    public int ChildCount { get; }
    public long MemberCount { get; }

    public OrganizationUnitInUseException(string displayName, int childCount, long memberCount)
        : base("Administration:OrganizationUnitInUse")
    {
        DisplayName = displayName;
        ChildCount = childCount;
        MemberCount = memberCount;
    }
}
