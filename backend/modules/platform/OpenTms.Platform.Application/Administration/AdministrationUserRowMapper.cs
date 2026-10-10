using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp.Data;
using Volo.Abp.Identity;

namespace OpenTms.Platform.Administration;

/* One mapping from an identity user to the row the administration grids show; the users
 * list and the organization-unit member lists share it. */
internal static class AdministrationUserRowMapper
{
    public static AdministrationUserRowDto Map(IdentityUser user, IReadOnlyDictionary<Guid, string> roleNameById, DateTimeOffset now)
    {
        var roleNames = user.Roles
            .Select(r => roleNameById.TryGetValue(r.RoleId, out var name) ? name : null)
            .Where(n => n != null)
            .Select(n => n!)
            .ToList();
        return Map(user, roleNames, now);
    }

    public static AdministrationUserRowDto Map(IdentityUser user, List<string> roleNames, DateTimeOffset now) =>
        new()
        {
            Id = user.Id,
            UserName = user.UserName,
            Email = user.Email,
            Name = user.Name,
            Surname = user.Surname,
            PhoneNumber = user.PhoneNumber,
            IsActive = user.IsActive,
            ShouldChangePasswordOnNextLogin = user.ShouldChangePasswordOnNextLogin,
            LockoutEnabled = user.LockoutEnabled,
            IsLockedOut = user.LockoutEnabled && user.LockoutEnd != null && user.LockoutEnd > now,
            HasProfilePicture = !string.IsNullOrEmpty(user.GetProperty<string?>(UserAppService.ProfilePictureContentTypeProperty)),
            RoleNames = roleNames,
            OrganizationUnitIds = user.OrganizationUnits.Select(ou => ou.OrganizationUnitId).ToList(),
            CreationTime = user.CreationTime
        };
}
