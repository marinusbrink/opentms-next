namespace OpenTms.Platform.Permissions;

public static class PlatformPermissions
{
    public const string GroupName = "Platform";

    public static class TenantProvisioning
    {
        public const string Manage = GroupName + ".TenantProvisioning.Manage";
    }

    /// <summary>The Tenants app (host only): docs/designs/tenants-and-editions.md.</summary>
    public static class Tenants
    {
        public const string Default = GroupName + ".Tenants";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";

        /// <summary>"Log in as" a user of a tenant (docs/designs/tenants-and-editions.md).</summary>
        public const string Impersonation = Default + ".Impersonation";

        /// <summary>"Reset to dataset": replaces a tenant's business data by a test dataset (docs/designs/65-tenant-test-datasets.md).</summary>
        public const string ResetToDataset = Default + ".ResetToDataset";
    }

    /// <summary>Derived values recalculated in the background (docs/designs/49-derived-values-and-live-updates.md).</summary>
    public static class DerivedValues
    {
        /// <summary>Re-run the calculation of a set of records (the bulk re-run of flow 2).</summary>
        public const string Recalculate = GroupName + ".DerivedValues.Recalculate";
    }

    /// <summary>The Editions app (host only): docs/designs/tenants-and-editions.md.</summary>
    public static class Editions
    {
        public const string Default = GroupName + ".Editions";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
        public const string ManageFeatures = Default + ".ManageFeatures";
    }

    /// <summary>
    /// The Platform master-data lists in the Master data app (design 54): one view permission per list with Create,
    /// Update and Delete children, both sides.
    /// </summary>
    public static class MasterData
    {
        public static class EmailTemplates
        {
            public const string Default = GroupName + ".EmailTemplates";
            public const string Create = Default + ".Create";
            public const string Update = Default + ".Update";
            public const string Delete = Default + ".Delete";
        }

        public static class ReportDefinitions
        {
            public const string Default = GroupName + ".ReportDefinitions";
            public const string Create = Default + ".Create";
            public const string Update = Default + ".Update";
            public const string Delete = Default + ".Delete";

            /// <summary>Open the provider's template editor and copy a template (design 55).</summary>
            public const string EditTemplate = Default + ".EditTemplate";
        }

        public static class DocumentTypes
        {
            public const string Default = GroupName + ".DocumentTypes";
            public const string Create = Default + ".Create";
            public const string Update = Default + ".Update";
            public const string Delete = Default + ".Delete";
        }

        /// <summary>(entity, view, create, update, delete) of every list: the definition provider and the admin seed iterate it.</summary>
        public static readonly (string Entity, string Default, string Create, string Update, string Delete)[] All =
        [
            ("EmailTemplates", EmailTemplates.Default, EmailTemplates.Create, EmailTemplates.Update, EmailTemplates.Delete),
            ("ReportDefinitions", ReportDefinitions.Default, ReportDefinitions.Create, ReportDefinitions.Update, ReportDefinitions.Delete),
            ("DocumentTypes", DocumentTypes.Default, DocumentTypes.Create, DocumentTypes.Update, DocumentTypes.Delete),
        ];
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
            public const string Unlock = Default + ".Unlock";
        }

        public static class Roles
        {
            public const string Default = GroupName + ".Administration.Roles";
            public const string Create = Default + ".Create";
            public const string Update = Default + ".Update";
            public const string Delete = Default + ".Delete";
            public const string BulkDelete = Default + ".BulkDelete";
        }

        public static class OrganizationUnits
        {
            public const string Default = GroupName + ".Administration.OrganizationUnits";
            public const string Create = Default + ".Create";
            public const string Update = Default + ".Update";
            public const string Delete = Default + ".Delete";
            public const string ManageMembers = Default + ".ManageMembers";
        }

        /// <summary>Read-only: the logbooks have no write actions, so there are view permissions only.</summary>
        public static class Logbook
        {
            public const string Default = GroupName + ".Administration.Logbook";
            public const string Functional = Default + ".Functional";
            public const string Technical = Default + ".Technical";
        }

        /// <summary>
        /// The Maintenance view. Host-only tabs (caches, databases, health, background jobs) act on the
        /// process or the host's stores; the Outbox tab exists on both sides (a tenant sees its own).
        /// </summary>
        public static class Maintenance
        {
            public const string Default = GroupName + ".Administration.Maintenance";

            public const string Caches = Default + ".Caches";
            public const string CachesClear = Caches + ".Clear";

            public const string Databases = Default + ".Databases";

            public const string Outbox = Default + ".Outbox";
            public const string OutboxDispatch = Outbox + ".Dispatch";

            public const string Health = Default + ".Health";

            public const string BackgroundJobs = Default + ".BackgroundJobs";
            public const string BackgroundJobsManage = BackgroundJobs + ".Manage";
        }

        /// <summary>The host's e-mail settings (design 64): host only; one configuration for every tenant.</summary>
        public static class EmailSettings
        {
            public const string Default = GroupName + ".Administration.EmailSettings";
            public const string Update = Default + ".Update";
            public const string SendTest = Default + ".SendTest";
        }
    }
}
