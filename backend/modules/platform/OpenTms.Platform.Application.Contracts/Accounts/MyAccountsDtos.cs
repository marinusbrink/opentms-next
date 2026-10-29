using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace OpenTms.Platform.Accounts;

/// <summary>Another account of the signed-in user's e-mail address, in another tenant or the host.</summary>
public record MyAccountDto
{
    /// <summary>Null for the host.</summary>
    public Guid? TenantId { get; init; }

    /// <summary>Null for the host.</summary>
    public string? TenantName { get; init; }

    /// <summary>
    /// True when the switch will ask for that account's password: the password this session knows is not valid there
    /// and no switch is remembered. A remembered switch whose account changed its password asks all the same.
    /// </summary>
    public bool NeedsPassword { get; init; }

    /// <summary>A "Remember password" switch exists (it can be forgotten).</summary>
    public bool IsRemembered { get; init; }
}

public record SwitchAccountDto
{
    /// <summary>The tenant to switch to; null for the host.</summary>
    public Guid? TenantId { get; init; }

    /// <summary>That account's password, when the switch asks for it.</summary>
    [StringLength(128)]
    public string? Password { get; init; }

    /// <summary>With a password: remember the switch so it no longer asks (until either password changes).</summary>
    public bool RememberPassword { get; init; }
}

/// <summary>Where the browser goes next: the auth server's page that switches the session.</summary>
public record AccountSwitchRedirectDto
{
    public string Url { get; init; } = default!;
}

/// <summary>
/// The user menu's list of the tenants the signed-in user's e-mail address has an account in, and the switch between
/// them (docs/designs/63-user-menu.md). Every signed-in user may use it for their own address only; not while
/// impersonating.
/// </summary>
public interface IMyAccountsAppService : IApplicationService
{
    Task<IReadOnlyList<MyAccountDto>> GetListAsync();

    Task<AccountSwitchRedirectDto> SwitchAsync(SwitchAccountDto input);

    /// <summary>Forgets a remembered switch to the account in <paramref name="tenantId"/> (null: the host).</summary>
    Task ForgetAsync(Guid? tenantId);
}
