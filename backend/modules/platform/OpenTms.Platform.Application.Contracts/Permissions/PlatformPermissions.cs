namespace OpenTms.Platform.Permissions;

public static class PlatformPermissions
{
    public const string GroupName = "Platform";

    public static class TenantProvisioning
    {
        public const string Manage = GroupName + ".TenantProvisioning.Manage";
    }

    public static class Administration
    {
        public static class Users
        {
            public const string Default = GroupName + ".Administration.Users";
            public const string Create = Default + ".Create";
            public const string Update = Default + ".Update";
            public const string Delete = Default + ".Delete";
            public const string BulkDelete = Default + ".BulkDelete";
            public const string ResetPassword = Default + ".ResetPassword";
        }

        public static class Roles
        {
            public const string Default = GroupName + ".Administration.Roles";
            public const string Create = Default + ".Create";
            public const string Update = Default + ".Update";
            public const string Delete = Default + ".Delete";
            public const string BulkDelete = Default + ".BulkDelete";
        }
    }
}
