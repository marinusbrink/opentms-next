using Volo.Abp;

namespace OpenTms.Platform.Administration;

public class RoleHasUsersException : BusinessException
{
    public string RoleName { get; }
    public long UserCount { get; }

    public RoleHasUsersException(string roleName, long userCount)
        : base("Administration:RoleHasUsers")
    {
        RoleName = roleName;
        UserCount = userCount;
    }
}
