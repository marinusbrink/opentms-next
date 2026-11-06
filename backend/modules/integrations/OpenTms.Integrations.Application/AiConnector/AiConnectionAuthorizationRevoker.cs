using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
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
/// </summary>
public class AiConnectionAuthorizationRevoker : ITransientDependency
{
    private readonly IOpenIddictAuthorizationManager _authorizations;
    private readonly IOpenIddictTokenManager _tokens;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ICancellationTokenProvider _cancellation;
    private readonly ILogger<AiConnectionAuthorizationRevoker> _logger;

    public AiConnectionAuthorizationRevoker(
        IOpenIddictAuthorizationManager authorizations,
        IOpenIddictTokenManager tokens,
        ICurrentTenant currentTenant,
        IUnitOfWorkManager unitOfWorkManager,
        ICancellationTokenProvider cancellation,
        ILogger<AiConnectionAuthorizationRevoker> logger)
    {
        _authorizations = authorizations;
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
            ?? throw new System.InvalidOperationException("Switching off an AI connection needs a unit of work to commit first.");

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

    public async Task RevokeAsync(string authorizationId)
    {
        try
        {
            /* The decision stands once this runs: a caller who hangs up must not stop it halfway. */
            using (_cancellation.Use(CancellationToken.None))
            using (_currentTenant.Change(null))
            {
                using var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);
                var authorization = await _authorizations.FindByIdAsync(authorizationId);
                if (authorization != null)
                {
                    await _authorizations.TryRevokeAsync(authorization);
                }

                await _tokens.RevokeByAuthorizationIdAsync(authorizationId);
                await unitOfWork.CompleteAsync();
            }
        }
        catch (System.Exception exception)
        {
            /* The tool can renew a token until this is retried; /mcp refuses it either way. Someone has to look. */
            _logger.LogError(exception, "OpenIddict authorization {AuthorizationId} of an AI connection was not revoked", authorizationId);
        }
    }
}
