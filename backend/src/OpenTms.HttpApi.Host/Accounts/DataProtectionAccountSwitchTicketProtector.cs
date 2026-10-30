using System;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using OpenTms.Platform.Accounts;
using Volo.Abp.DependencyInjection;

namespace OpenTms.Accounts;

/// <summary>
/// <see cref="IAccountSwitchTicketProtector"/> on ASP.NET Core data protection: encrypted, signed, valid for five minutes
/// (time to log in again if the auth-server session expired), readable by every instance that shares the
/// data-protection keys. Its own purpose, so an impersonation ticket is never accepted as a switch ticket.
/// </summary>
public class DataProtectionAccountSwitchTicketProtector : IAccountSwitchTicketProtector, ISingletonDependency
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private readonly ITimeLimitedDataProtector _protector;

    public DataProtectionAccountSwitchTicketProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("OpenTms.AccountSwitch.Ticket.v1").ToTimeLimitedDataProtector();
    }

    public string Protect(AccountSwitchTicket ticket) =>
        _protector.Protect(JsonSerializer.Serialize(ticket), Lifetime);

    public AccountSwitchTicket? Unprotect(string protectedTicket)
    {
        try
        {
            return JsonSerializer.Deserialize<AccountSwitchTicket>(_protector.Unprotect(protectedTicket));
        }
        catch (Exception e) when (e is CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }
}
