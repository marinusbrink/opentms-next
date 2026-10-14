using System;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using OpenTms.Platform.Impersonation;
using Volo.Abp.DependencyInjection;

namespace OpenTms.Impersonation;

/// <summary>
/// <see cref="IImpersonationTicketProtector"/> on ASP.NET Core data protection: encrypted, signed, valid for one
/// five minutes (time to log in again if the auth-server session expired), readable by every instance that shares the data-protection keys (the same keys the auth cookie needs).
/// </summary>
public class DataProtectionImpersonationTicketProtector : IImpersonationTicketProtector, ISingletonDependency
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private readonly ITimeLimitedDataProtector _protector;

    public DataProtectionImpersonationTicketProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("OpenTms.Impersonation.Ticket.v1").ToTimeLimitedDataProtector();
    }

    public string Protect(ImpersonationTicket ticket) =>
        _protector.Protect(JsonSerializer.Serialize(ticket), Lifetime);

    public ImpersonationTicket? Unprotect(string protectedTicket)
    {
        try
        {
            return JsonSerializer.Deserialize<ImpersonationTicket>(_protector.Unprotect(protectedTicket));
        }
        catch (Exception e) when (e is CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }
}
