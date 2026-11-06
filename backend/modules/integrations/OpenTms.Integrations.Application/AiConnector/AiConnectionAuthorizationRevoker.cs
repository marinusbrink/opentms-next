using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.OpenIddict.Authorizations;
using Volo.Abp.OpenIddict.Tokens;
using Volo.Abp.Threading;
using Volo.Abp.Uow;

namespace OpenTms.Integrations.AiConnector;

/// <summary>
/// The host side of switching a connection off (design 74 "Revoke"): the OpenIddict authorization and its tokens live in
/// the host database, so this runs in the host's context and in a unit of work of its own, as
/// <c>ApiClientApplicationRegistrar</c> does. Revoking them blocks renewal; <c>AiConnection.RevokedAt</c> in the tenant's
/// database blocks <c>/mcp</c>. The two databases share no transaction, so switching off commits the tenant's row first and
/// then comes here (<see cref="RevokeAfterCommit"/>): a failure on the tenant's side has revoked nothing, and a failure here
/// leaves a connection that is off. It is logged and never thrown; calling the revoke endpoint again retries it.
/// <para>
/// This reads and writes the stores, never the managers. <c>OpenIddictAuthorizationManager.UpdateAsync</c> writes the changed
/// model to ABP's distributed cache before the transaction commits, and that entry outlives a rollback: the next read through
/// the manager then says revoked while the database says valid, and <c>TryRevokeAsync</c> answers true without writing.
/// </para>
/// </summary>
public class AiConnectionAuthorizationRevoker : ITransientDependency
{
    private readonly IOpenIddictAuthorizationStore<OpenIddictAuthorizationModel> _authorizations;
    private readonly IOpenIddictAuthorizationCache<OpenIddictAuthorizationModel> _authorizationCache;
    private readonly IOpenIddictTokenStore<OpenIddictTokenModel> _tokens;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ICancellationTokenProvider _cancellation;
    private readonly ILogger<AiConnectionAuthorizationRevoker> _logger;

    public AiConnectionAuthorizationRevoker(
        IOpenIddictAuthorizationStore<OpenIddictAuthorizationModel> authorizations,
        IOpenIddictAuthorizationCache<OpenIddictAuthorizationModel> authorizationCache,
        IOpenIddictTokenStore<OpenIddictTokenModel> tokens,
        ICurrentTenant currentTenant,
        IUnitOfWorkManager unitOfWorkManager,
        ICancellationTokenProvider cancellation,
        ILogger<AiConnectionAuthorizationRevoker> logger)
    {
        _authorizations = authorizations;
        _authorizationCache = authorizationCache;
        _tokens = tokens;
        _currentTenant = currentTenant;
        _unitOfWorkManager = unitOfWorkManager;
        _cancellation = cancellation;
        _logger = logger;
    }

    /// <summary>Revokes these once the current unit of work has committed, and not at all when it does not commit.</summary>
    public void RevokeAfterCommit(IReadOnlyCollection<string> authorizationIds)
    {
        var unitOfWork = _unitOfWorkManager.Current
            ?? throw new InvalidOperationException("Switching off an AI connection needs a unit of work to commit first.");

        /* The unit of work's own services: a caller's scope may be gone once it has committed. */
        var services = unitOfWork.ServiceProvider;
        unitOfWork.OnCompleted(async () =>
        {
            var revoker = services.GetRequiredService<AiConnectionAuthorizationRevoker>();
            foreach (var authorizationId in authorizationIds)
            {
                await revoker.RevokeAsync(authorizationId);
            }
        });
    }

    /// <summary>True when the database now holds the authorization and all its tokens as revoked. False has been logged as an error.</summary>
    public async Task<bool> RevokeAsync(string authorizationId)
    {
        try
        {
            /* The decision stands once this runs: a caller who hangs up must not stop it halfway. */
            using (_cancellation.Use(CancellationToken.None))
            using (_currentTenant.Change(null))
            {
                await WriteAsync(authorizationId);
                if (await IsCompletelyRevokedAsync(authorizationId))
                {
                    return true;
                }
            }

            _logger.LogError("OpenIddict authorization {AuthorizationId} of an AI connection is still not fully revoked after revoking it", authorizationId);
        }
        catch (Exception exception)
        {
            /* The tool can renew a token until this is retried; /mcp refuses it either way. Someone has to look. */
            _logger.LogError(exception, "OpenIddict authorization {AuthorizationId} of an AI connection was not revoked", authorizationId);
        }

        return false;
    }

    private async Task WriteAsync(string authorizationId)
    {
        OpenIddictAuthorizationModel? authorization = null;
        try
        {
            using var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);
            authorization = await _authorizations.FindByIdAsync(authorizationId, CancellationToken.None);
            if (authorization != null
                && !string.Equals(await _authorizations.GetStatusAsync(authorization, CancellationToken.None), OpenIddictConstants.Statuses.Revoked, StringComparison.OrdinalIgnoreCase))
            {
                await _authorizations.SetStatusAsync(authorization, OpenIddictConstants.Statuses.Revoked, CancellationToken.None);
                await _authorizations.UpdateAsync(authorization, CancellationToken.None);
            }

            await _tokens.RevokeByAuthorizationIdAsync(authorizationId, CancellationToken.None);
            await unitOfWork.CompleteAsync();
        }
        finally
        {
            /* Whatever came of it: what /connect/token reads next must be the database, not what the cache held before. */
            if (authorization != null)
            {
                await _authorizationCache.RemoveAsync(authorization, CancellationToken.None);
            }
        }
    }

    /// <summary>Read back in a unit of work of its own, so from what was committed. An authorization that no longer exists is revoked as far as it can be.</summary>
    private async Task<bool> IsCompletelyRevokedAsync(string authorizationId)
    {
        using var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: false), requiresNew: true);
        var authorization = await _authorizations.FindByIdAsync(authorizationId, CancellationToken.None);
        var revoked = authorization == null
            || string.Equals(await _authorizations.GetStatusAsync(authorization, CancellationToken.None), OpenIddictConstants.Statuses.Revoked, StringComparison.OrdinalIgnoreCase);

        await foreach (var token in _tokens.FindByAuthorizationIdAsync(authorizationId, CancellationToken.None))
        {
            revoked &= string.Equals(await _tokens.GetStatusAsync(token, CancellationToken.None), OpenIddictConstants.Statuses.Revoked, StringComparison.OrdinalIgnoreCase);
        }

        await unitOfWork.CompleteAsync();
        return revoked;
    }
}
