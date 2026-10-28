using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTms.Platform.SignIn;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;

namespace OpenTms.SignIn;

public enum SignInResolutionOutcome
{
    /// <summary>No user with this e-mail address and password in any tenant or the host.</summary>
    Invalid,
    LockedOut,
    NotAllowed,
    /// <summary>The password was valid nowhere, and a tenant that has this address could not be asked.</summary>
    Unavailable,
    /// <summary>The credentials are valid in exactly one place: sign in there.</summary>
    Single,
    /// <summary>The credentials are valid in several places: the user picks the tenant.</summary>
    Multiple,
}

/// <summary>A place the credentials were valid in: the tenant (null: the host), its name (null: the host) and the user there.</summary>
public record SignInChoice(Guid? TenantId, string? TenantName, Guid UserId);

public record SignInResolution(SignInResolutionOutcome Outcome, IReadOnlyList<SignInChoice> Choices);

/// <summary>
/// Finds where an e-mail address with its password can sign in, before any tenant is known
/// (docs/designs/62-sign-in-tenant-detection.md). The host's sign-in lookup names the candidates; each one is checked in
/// its own tenant's database by <see cref="ISignInCredentialChecker"/>. Only a tenant where the password was valid ever
/// becomes a choice. A failure counts towards lockout only when the password was valid nowhere, so signing in to one
/// tenant never locks the same address out of another. A tenant whose database cannot be reached is left out.
/// </summary>
public class SignInTenantResolver : ITransientDependency
{
    private readonly IUserSignInLookupStore _lookupStore;
    private readonly ISignInCredentialChecker _credentialChecker;
    private readonly ITenantStore _tenantStore;
    private readonly ILookupNormalizer _normalizer;
    private readonly ILogger<SignInTenantResolver> _logger;

    public SignInTenantResolver(
        IUserSignInLookupStore lookupStore,
        ISignInCredentialChecker credentialChecker,
        ITenantStore tenantStore,
        ILookupNormalizer normalizer,
        ILogger<SignInTenantResolver>? logger = null)
    {
        _lookupStore = lookupStore;
        _credentialChecker = credentialChecker;
        _tenantStore = tenantStore;
        _normalizer = normalizer;
        _logger = logger ?? NullLogger<SignInTenantResolver>.Instance;
    }

    /// <summary>Whether a user with this e-mail address exists in an active tenant or the host (step 1 of the page).</summary>
    public async Task<bool> IsKnownEmailAsync(string email) => (await FindPlacesAsync(Normalize(email))).Count > 0;

    public async Task<SignInResolution> ResolveAsync(string email, string password)
    {
        var normalizedEmail = Normalize(email);
        var places = await FindPlacesAsync(normalizedEmail);

        var choices = new List<SignInChoice>();
        var outcomes = new List<SignInCredentialOutcome?>();
        var failed = new List<SignInCandidate>();
        foreach (var (candidate, tenantName) in places)
        {
            SignInCredentialOutcome? outcome;
            try
            {
                outcome = await _credentialChecker.CheckAsync(candidate, normalizedEmail, password);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Sign-in check failed in tenant {TenantId}: left out of this sign-in", candidate.TenantId);
                outcome = null;
            }

            outcomes.Add(outcome);
            if (outcome == SignInCredentialOutcome.Succeeded && choices.All(c => c.TenantId != candidate.TenantId))
            {
                choices.Add(new SignInChoice(candidate.TenantId, tenantName, candidate.UserId));
            }
            else if (outcome == SignInCredentialOutcome.Failed)
            {
                failed.Add(candidate);
            }
        }

        if (choices.Count == 1)
        {
            return new SignInResolution(SignInResolutionOutcome.Single, choices);
        }

        if (choices.Count > 1)
        {
            return new SignInResolution(
                SignInResolutionOutcome.Multiple,
                choices.OrderBy(c => c.TenantId == null ? 0 : 1).ThenBy(c => c.TenantName, StringComparer.OrdinalIgnoreCase).ToList());
        }

        var lockedOut = outcomes.Contains(SignInCredentialOutcome.LockedOut);
        foreach (var candidate in failed)
        {
            try
            {
                lockedOut |= await _credentialChecker.RecordFailureAsync(candidate);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed sign-in not counted in tenant {TenantId}", candidate.TenantId);
            }
        }

        var outcomeWhenNone =
            lockedOut ? SignInResolutionOutcome.LockedOut
            : outcomes.Contains(SignInCredentialOutcome.NotAllowed) ? SignInResolutionOutcome.NotAllowed
            : outcomes.Contains(null) ? SignInResolutionOutcome.Unavailable
            : SignInResolutionOutcome.Invalid;
        return new SignInResolution(outcomeWhenNone, []);
    }

    private string Normalize(string email) => _normalizer.NormalizeEmail(email.Trim());

    /// <summary>The candidates for an address, in active tenants and the host only, with each tenant's name.</summary>
    private async Task<List<(SignInCandidate Candidate, string? TenantName)>> FindPlacesAsync(string normalizedEmail)
    {
        // Cross-tenant by design: candidates from every tenant and the host, each checked in its own tenant only —
        // docs/designs/62-sign-in-tenant-detection.md, DEVIATION(constitution-1).
        var places = new List<(SignInCandidate, string?)>();
        foreach (var candidate in (await _lookupStore.FindAsync(UserSignInLookupHash.Of(normalizedEmail))).Distinct())
        {
            if (candidate.TenantId is not { } tenantId)
            {
                places.Add((candidate, null));
                continue;
            }

            /* A deleted or deactivated tenant is no place to sign in: skipped before its users' lockout counters move. */
            var tenant = await _tenantStore.FindAsync(tenantId);
            if (tenant is { IsActive: true })
            {
                places.Add((candidate, tenant.Name));
            }
        }

        return places;
    }
}
