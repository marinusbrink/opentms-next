using System;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Timing;

namespace OpenTms.Integrations.OrderIntake.Channels;

/// <summary>What the OAuth <c>state</c> carries through the provider's consent (design 72 "Connect").</summary>
public record MailboxOAuthState(Guid? TenantId, Guid ConnectionId, Guid? UserId, string Nonce, DateTime ExpiresAt);

/// <summary>
/// Protects and reads the OAuth <c>state</c> of a mailbox connection (design 72 "Connect", "Tenant dimension"): data
/// protection with purpose <see cref="OrderIntakeConsts.OAuthStatePurpose"/> over the tenant, connection, user, a nonce and
/// an expiry <see cref="OrderIntakeConsts.OAuthStateLifetime"/> ahead. The anonymous callback takes the tenant only from
/// here; a state that does not unprotect or has expired reads as null. The key ring is the host's shared one, so any
/// instance can read a state another issued.
/// </summary>
public class MailboxOAuthStateProtector : ISingletonDependency
{
    private readonly IDataProtector _protector;
    private readonly IClock _clock;

    public MailboxOAuthStateProtector(IDataProtectionProvider dataProtectionProvider, IClock clock)
    {
        _protector = dataProtectionProvider.CreateProtector(OrderIntakeConsts.OAuthStatePurpose);
        _clock = clock;
    }

    public string Protect(Guid? tenantId, Guid connectionId, Guid? userId)
    {
        var state = new MailboxOAuthState(
            tenantId, connectionId, userId, Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
            ToUtc(_clock.Now).Add(OrderIntakeConsts.OAuthStateLifetime));
        return _protector.Protect(JsonSerializer.Serialize(state));
    }

    /// <summary>The state, or null when it is missing, tampered with, from another purpose or key ring, or expired.</summary>
    public MailboxOAuthState? Unprotect(string? protectedState)
    {
        if (string.IsNullOrWhiteSpace(protectedState) || protectedState.Length > 4096)
        {
            return null;
        }

        MailboxOAuthState? state;
        try
        {
            state = JsonSerializer.Deserialize<MailboxOAuthState>(_protector.Unprotect(protectedState));
        }
        catch (Exception e) when (e is CryptographicException or FormatException or JsonException)
        {
            return null;
        }

        if (state == null || state.ConnectionId == Guid.Empty || string.IsNullOrEmpty(state.Nonce))
        {
            return null;
        }

        return ToUtc(state.ExpiresAt) > ToUtc(_clock.Now) ? state : null;
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
