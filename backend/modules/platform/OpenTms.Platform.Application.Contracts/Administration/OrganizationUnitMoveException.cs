using Volo.Abp;

namespace OpenTms.Platform.Administration;

/// <summary>Moving a unit under itself or under one of its own sub-units would cut the subtree loose.</summary>
public class OrganizationUnitMoveException : UserFriendlyException
{
    public OrganizationUnitMoveException(string message) : base(message, "Administration:OrganizationUnitCannotMoveIntoItself")
    {
    }
}
