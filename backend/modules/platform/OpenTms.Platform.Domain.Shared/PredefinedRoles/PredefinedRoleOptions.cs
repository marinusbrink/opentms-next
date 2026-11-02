using System;
using System.Collections.Generic;

namespace OpenTms.Platform.PredefinedRoles;

/// <summary>
/// The predefined roles of design 71 and the permissions each one gets. The Platform defines the roles; every module
/// grants its own permissions to them from its <c>Application.Contracts</c> module (the Platform references no domain
/// module), and is loaded by the DbMigrator as well as the host:
/// <code>Configure&lt;PredefinedRoleOptions&gt;(o =&gt; o.Grant(PlatformRoleNames.Planner, PlanningExecutionPermissions.TransportTrips.Default));</code>
/// A grant to a role that is not defined fails at start-up; an unknown permission or one of the wrong side fails the seed.
/// </summary>
public class PredefinedRoleOptions
{
    private readonly Dictionary<string, PredefinedRoleDefinition> _roles = new(StringComparer.Ordinal)
    {
        [PlatformRoleNames.OrderEntry] = new(PlatformRoleNames.OrderEntry, IsHostRole: false),
        [PlatformRoleNames.Planner] = new(PlatformRoleNames.Planner, IsHostRole: false),
        [PlatformRoleNames.Finance] = new(PlatformRoleNames.Finance, IsHostRole: false),
        [PlatformRoleNames.MasterDataManager] = new(PlatformRoleNames.MasterDataManager, IsHostRole: false),
        [PlatformRoleNames.ReadOnly] = new(PlatformRoleNames.ReadOnly, IsHostRole: false),
        [PlatformRoleNames.PlatformOperator] = new(PlatformRoleNames.PlatformOperator, IsHostRole: true),
        [PlatformRoleNames.TenantManager] = new(PlatformRoleNames.TenantManager, IsHostRole: true),
    };

    public IReadOnlyCollection<PredefinedRoleDefinition> Roles => _roles.Values;

    public PredefinedRoleOptions Grant(string roleName, params string[] permissions)
    {
        if (!_roles.TryGetValue(roleName, out var role))
        {
            throw new ArgumentException($"'{roleName}' is not a predefined role.", nameof(roleName));
        }

        foreach (var permission in permissions)
        {
            role.Permissions.Add(permission);
        }

        return this;
    }
}

/// <summary>A predefined role: seeded in every tenant (or only in the host) with <see cref="Permissions"/>.</summary>
public sealed record PredefinedRoleDefinition(string Name, bool IsHostRole)
{
    public SortedSet<string> Permissions { get; } = new(StringComparer.Ordinal);
}
