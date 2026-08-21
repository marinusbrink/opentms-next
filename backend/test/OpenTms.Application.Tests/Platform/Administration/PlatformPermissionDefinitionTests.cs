using OpenTms.Platform.Permissions;
using Shouldly;
using Xunit;

namespace OpenTms.Platform.Administration;

/// <summary>
/// Unit tests for PlatformPermissions constants.
/// Risk class: Critical — incorrect permission strings silently grant or deny
/// access across the whole tenant fleet (design §Test risk analysis).
/// </summary>
public class PlatformPermissionDefinitionTests
{
    // ── Constant value assertions ─────────────────────────────────────────────

    [Fact]
    public void Users_Default_permission_has_correct_value()
    {
        PlatformPermissions.Administration.Users.Default
            .ShouldBe("Platform.Administration.Users");
    }

    [Fact]
    public void Users_Create_permission_has_correct_value()
    {
        PlatformPermissions.Administration.Users.Create
            .ShouldBe("Platform.Administration.Users.Create");
    }

    [Fact]
    public void Users_Update_permission_has_correct_value()
    {
        PlatformPermissions.Administration.Users.Update
            .ShouldBe("Platform.Administration.Users.Update");
    }

    [Fact]
    public void Users_Delete_permission_has_correct_value()
    {
        PlatformPermissions.Administration.Users.Delete
            .ShouldBe("Platform.Administration.Users.Delete");
    }

    [Fact]
    public void Users_BulkDelete_permission_has_correct_value()
    {
        PlatformPermissions.Administration.Users.BulkDelete
            .ShouldBe("Platform.Administration.Users.BulkDelete");
    }

    [Fact]
    public void Users_ResetPassword_permission_has_correct_value()
    {
        PlatformPermissions.Administration.Users.ResetPassword
            .ShouldBe("Platform.Administration.Users.ResetPassword");
    }

    [Fact]
    public void Roles_Default_permission_has_correct_value()
    {
        PlatformPermissions.Administration.Roles.Default
            .ShouldBe("Platform.Administration.Roles");
    }

    [Fact]
    public void Roles_Create_permission_has_correct_value()
    {
        PlatformPermissions.Administration.Roles.Create
            .ShouldBe("Platform.Administration.Roles.Create");
    }

    [Fact]
    public void Roles_Update_permission_has_correct_value()
    {
        PlatformPermissions.Administration.Roles.Update
            .ShouldBe("Platform.Administration.Roles.Update");
    }

    [Fact]
    public void Roles_Delete_permission_has_correct_value()
    {
        PlatformPermissions.Administration.Roles.Delete
            .ShouldBe("Platform.Administration.Roles.Delete");
    }

    [Fact]
    public void Roles_BulkDelete_permission_has_correct_value()
    {
        PlatformPermissions.Administration.Roles.BulkDelete
            .ShouldBe("Platform.Administration.Roles.BulkDelete");
    }

    // ── Hierarchy: child permissions are prefixed with their parent ──────────

    [Fact]
    public void All_Users_child_permissions_start_with_Users_Default()
    {
        var parent = PlatformPermissions.Administration.Users.Default;

        PlatformPermissions.Administration.Users.Create.ShouldStartWith(parent + ".");
        PlatformPermissions.Administration.Users.Update.ShouldStartWith(parent + ".");
        PlatformPermissions.Administration.Users.Delete.ShouldStartWith(parent + ".");
        PlatformPermissions.Administration.Users.BulkDelete.ShouldStartWith(parent + ".");
        PlatformPermissions.Administration.Users.ResetPassword.ShouldStartWith(parent + ".");
    }

    [Fact]
    public void All_Roles_child_permissions_start_with_Roles_Default()
    {
        var parent = PlatformPermissions.Administration.Roles.Default;

        PlatformPermissions.Administration.Roles.Create.ShouldStartWith(parent + ".");
        PlatformPermissions.Administration.Roles.Update.ShouldStartWith(parent + ".");
        PlatformPermissions.Administration.Roles.Delete.ShouldStartWith(parent + ".");
        PlatformPermissions.Administration.Roles.BulkDelete.ShouldStartWith(parent + ".");
    }

    // ── GroupName is the correct prefix for all permissions ──────────────────

    [Fact]
    public void All_Administration_permissions_start_with_Platform_group_name()
    {
        var group = PlatformPermissions.GroupName;
        group.ShouldBe("Platform");

        PlatformPermissions.Administration.Users.Default.ShouldStartWith(group + ".");
        PlatformPermissions.Administration.Users.Create.ShouldStartWith(group + ".");
        PlatformPermissions.Administration.Users.Update.ShouldStartWith(group + ".");
        PlatformPermissions.Administration.Users.Delete.ShouldStartWith(group + ".");
        PlatformPermissions.Administration.Users.BulkDelete.ShouldStartWith(group + ".");
        PlatformPermissions.Administration.Users.ResetPassword.ShouldStartWith(group + ".");
        PlatformPermissions.Administration.Roles.Default.ShouldStartWith(group + ".");
        PlatformPermissions.Administration.Roles.Create.ShouldStartWith(group + ".");
        PlatformPermissions.Administration.Roles.Update.ShouldStartWith(group + ".");
        PlatformPermissions.Administration.Roles.Delete.ShouldStartWith(group + ".");
        PlatformPermissions.Administration.Roles.BulkDelete.ShouldStartWith(group + ".");
    }

    // ── No duplicate permission strings ──────────────────────────────────────

    [Fact]
    public void All_Administration_permission_strings_are_unique()
    {
        var all = new[]
        {
            PlatformPermissions.Administration.Users.Default,
            PlatformPermissions.Administration.Users.Create,
            PlatformPermissions.Administration.Users.Update,
            PlatformPermissions.Administration.Users.Delete,
            PlatformPermissions.Administration.Users.BulkDelete,
            PlatformPermissions.Administration.Users.ResetPassword,
            PlatformPermissions.Administration.Roles.Default,
            PlatformPermissions.Administration.Roles.Create,
            PlatformPermissions.Administration.Roles.Update,
            PlatformPermissions.Administration.Roles.Delete,
            PlatformPermissions.Administration.Roles.BulkDelete,
        };

        var distinct = new System.Collections.Generic.HashSet<string>(all);
        distinct.Count.ShouldBe(all.Length, "every permission must have a unique string value");
    }
}
