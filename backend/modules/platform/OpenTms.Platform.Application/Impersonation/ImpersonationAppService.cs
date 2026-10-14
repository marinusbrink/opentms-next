using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Permissions;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Identity;
using Volo.Abp.TenantManagement;
using Volo.Abp.UI.Navigation.Urls;
using Volo.Abp.Users;

namespace OpenTms.Platform.Impersonation;

/* "Log in as" (docs/designs/tenants-and-editions.md, "Log in as a tenant user"; PO 2026-10-14). This service only
 * decides and issues a five-minute ticket; the auth server's /Account/Impersonate page redeems it in the browser and
 * switches the session, after which the SPA signs in again through the normal code flow. Reading a tenant's users
 * switches to that tenant's context explicitly (its own database); no tenant filter is disabled. */
[RemoteService(IsEnabled = false)]
public class ImpersonationAppService : PlatformAppServiceBase, IImpersonationAppService
{
    /// <summary>The auth server's page that redeems a ticket (HttpApi.Host, Pages/Account/Impersonate).</summary>
    public const string RedeemPath = "Account/Impersonate";

    /// <summary>The application name the host configures in <c>AppUrlOptions</c> for the auth server's pages.</summary>
    private const string AuthServerApplicationName = "MVC";

    private readonly ITenantRepository _tenantRepository;
    private readonly IIdentityUserRepository _userRepository;
    private readonly IImpersonationTicketProtector _ticketProtector;
    private readonly IAppUrlProvider _appUrlProvider;

    public ImpersonationAppService(
        ITenantRepository tenantRepository,
        IIdentityUserRepository userRepository,
        IImpersonationTicketProtector ticketProtector,
        IAppUrlProvider appUrlProvider)
    {
        _tenantRepository = tenantRepository;
        _userRepository = userRepository;
        _ticketProtector = ticketProtector;
        _appUrlProvider = appUrlProvider;
    }

    [Authorize(PlatformPermissions.Tenants.Impersonation)]
    public async Task<GridResponse<TenantUserRowDto>> GetTenantUsersAsync(Guid tenantId, GridRequest request)
    {
        HostOnlyGuard.Ensure(CurrentTenant);
        var tenant = await _tenantRepository.GetAsync(tenantId);

        var filter = string.IsNullOrWhiteSpace(request.WildcardSearch) ? null : request.WildcardSearch.Trim();
        var sort = request.SortModels.FirstOrDefault();
        var direction = string.Equals(sort?.Sort, "desc", StringComparison.OrdinalIgnoreCase) ? " desc" : " asc";
        var sorting = sort?.ColId switch
        {
            "name" => "Name" + direction,
            "surname" => "Surname" + direction,
            _ => "UserName" + direction
        };

        using (CurrentTenant.Change(tenant.Id, tenant.Name))
        {
            var total = await _userRepository.GetCountAsync();
            var filtered = filter == null ? total : await _userRepository.GetCountAsync(filter);
            var users = await _userRepository.GetListAsync(
                sorting: sorting,
                maxResultCount: Math.Max(0, request.EndRow - request.StartRow),
                skipCount: request.StartRow,
                filter: filter);

            return new GridResponse<TenantUserRowDto>
            {
                Rows = users.Select(u => new TenantUserRowDto
                {
                    Id = u.Id,
                    UserName = u.UserName,
                    Name = u.Name,
                    Surname = u.Surname,
                    IsActive = u.IsActive
                }).ToList(),
                TotalCount = total,
                FilteredCount = filtered
            };
        }
    }

    [Authorize(PlatformPermissions.Tenants.Impersonation)]
    public async Task<ImpersonationRedirectDto> StartAsync(Guid tenantId, Guid userId)
    {
        HostOnlyGuard.Ensure(CurrentTenant);
        var actorId = CurrentUser.Id ?? throw new AbpAuthorizationException();
        var tenant = await _tenantRepository.GetAsync(tenantId);

        using (CurrentTenant.Change(tenant.Id, tenant.Name))
        {
            var user = await _userRepository.FindAsync(userId);
            if (user == null || !user.IsActive)
            {
                throw new UserFriendlyException(L["Impersonation:UserNotAvailable"]);
            }
        }

        Logger.LogInformation(
            "Impersonation of user {UserId} in tenant {TenantId} requested by {ActorId}", userId, tenantId, actorId);

        return await RedirectForAsync(new ImpersonationTicket(
            IsReturn: false,
            ExpectedUserId: actorId,
            ExpectedTenantId: null,
            TargetUserId: userId,
            TargetTenantId: tenantId));
    }

    [Authorize]
    public async Task<ImpersonationRedirectDto> ReturnAsync()
    {
        var impersonatorId = CurrentUser.FindImpersonatorUserId()
                             ?? throw new UserFriendlyException(L["Impersonation:NotImpersonating"]);

        Logger.LogInformation(
            "Return from impersonation of user {UserId} in tenant {TenantId} to {ImpersonatorId}",
            CurrentUser.Id, CurrentTenant.Id, impersonatorId);

        return await RedirectForAsync(new ImpersonationTicket(
            IsReturn: true,
            ExpectedUserId: CurrentUser.Id!.Value,
            ExpectedTenantId: CurrentTenant.Id,
            TargetUserId: impersonatorId,
            TargetTenantId: CurrentUser.FindImpersonatorTenantId()));
    }

    private async Task<ImpersonationRedirectDto> RedirectForAsync(ImpersonationTicket ticket)
    {
        var root = (await _appUrlProvider.GetUrlAsync(AuthServerApplicationName)).EnsureEndsWith('/');
        return new ImpersonationRedirectDto
        {
            Url = $"{root}{RedeemPath}?ticket={Uri.EscapeDataString(_ticketProtector.Protect(ticket))}"
        };
    }
}
