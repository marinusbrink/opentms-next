using Volo.Abp.Authorization;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Platform;

/// <summary>
/// Host-only services act on host-owned stores. Their permissions are host-side already; this guard holds even
/// where authorization is relaxed (tests) or a permission were ever granted wrongly.
/// </summary>
internal static class HostOnlyGuard
{
    public static void Ensure(ICurrentTenant currentTenant)
    {
        if (currentTenant.IsAvailable)
        {
            throw new AbpAuthorizationException("This action is available on the host side only.");
        }
    }
}
