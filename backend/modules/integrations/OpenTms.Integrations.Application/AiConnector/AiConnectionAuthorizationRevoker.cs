using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace OpenTms.Integrations.AiConnector;

/// <summary>
/// The host side of switching a connection off (design 74 "Revoke"): the OpenIddict authorization and its tokens live in
/// the host database, so this runs in the host's context and in a unit of work of its own, as
/// <c>ApiClientApplicationRegistrar</c> does. Revoking them blocks renewal; <c>AiConnection.RevokedAt</c> in the tenant's
/// database has blocked <c>/mcp</c> already. A failure is logged and never thrown: the connection is off either way and
/// revoking it again retries this.
/// </summary>
public class AiConnectionAuthorizationRevoker : ITransientDependency
{
    private readonly IOpenIddictAuthorizationManager _authorizations;
    private readonly IOpenIddictTokenManager _tokens;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ILogger<AiConnectionAuthorizationRevoker> _logger;

    public AiConnectionAuthorizationRevoker(
        IOpenIddictAuthorizationManager authorizations,
        IOpenIddictTokenManager tokens,
        ICurrentTenant currentTenant,
        IUnitOfWorkManager unitOfWorkManager,
        ILogger<AiConnectionAuthorizationRevoker> logger)
    {
        _authorizations = authorizations;
        _tokens = tokens;
        _currentTenant = currentTenant;
        _unitOfWorkManager = unitOfWorkManager;
        _logger = logger;
    }

    public async Task RevokeAsync(string authorizationId)
    {
        try
        {
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
        catch (System.Exception exception) when (exception is not System.OperationCanceledException)
        {
            /* The tool can renew a token until this is retried; /mcp refuses it either way. Someone has to look. */
            _logger.LogError(exception, "OpenIddict authorization {AuthorizationId} of an AI connection was not revoked", authorizationId);
        }
    }
}
