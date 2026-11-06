using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using OpenTms.Integrations.AiConnector;
using OpenTms.OpenIddict;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.MultiTenancy;
using Volo.Abp.OpenIddict.Applications;
using Volo.Abp.OpenIddict.EntityFrameworkCore;
using Volo.Abp.Uow;

namespace OpenTms.AiConnector;

/// <summary>The approved client a registration gets.</summary>
public sealed record AiConnectorRegisteredClient(string ClientId, string DisplayName);

/// <summary>
/// The rule behind <c>/connect/register</c> (design 74 "Registration"). A tool with fixed URIs has one seeded client, found by
/// its URIs, and nothing is written. An approved URI of ChatGPT's gets an application of its own, because OpenIddict compares
/// redirect URIs exactly; its id is a hash of the URI, so registering again returns the same client. A confidential client
/// (Gemini) is never handed out here: its administrator enters it by hand. OpenIddict's rows live in the host database, so
/// this runs in the host's context whatever the request names.
/// </summary>
public class AiConnectorClientRegistrar : ITransientDependency
{
    private const int MaxRedirectUris = 10;
    private const int MaxRedirectUriLength = 2048;

    private readonly IOpenIddictApplicationManager _applications;
    private readonly IOpenIddictApplicationRepository _applicationRepository;
    private readonly IDbContextProvider<IOpenIddictDbContext> _openIddictDbContext;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ILogger<AiConnectorClientRegistrar> _logger;

    public AiConnectorClientRegistrar(
        IOpenIddictApplicationManager applications,
        IOpenIddictApplicationRepository applicationRepository,
        IDbContextProvider<IOpenIddictDbContext> openIddictDbContext,
        ICurrentTenant currentTenant,
        IUnitOfWorkManager unitOfWorkManager,
        ILogger<AiConnectorClientRegistrar> logger)
    {
        _applications = applications;
        _applicationRepository = applicationRepository;
        _openIddictDbContext = openIddictDbContext;
        _currentTenant = currentTenant;
        _unitOfWorkManager = unitOfWorkManager;
        _logger = logger;
    }

    /// <summary>Null when the URIs are not all of one approved tool.</summary>
    public async Task<AiConnectorRegisteredClient?> RegisterAsync(IReadOnlyList<string> redirectUris, CancellationToken cancellationToken = default)
    {
        if (redirectUris.Count == 0 || redirectUris.Count > MaxRedirectUris
            || redirectUris.Any(uri => string.IsNullOrWhiteSpace(uri) || uri.Length > MaxRedirectUriLength))
        {
            return null;
        }

        using (_currentTenant.Change(null))
        {
            var chatGptUris = redirectUris.Count(AiConnectorClients.IsChatGptRedirectUri);
            if (chatGptUris == 0)
            {
                return await FindFixedClientAsync(redirectUris, cancellationToken);
            }

            /* ChatGPT's application holds exactly one URI; a second one, or a mix with another tool's, is no approved set. */
            return chatGptUris == 1 && redirectUris.Count == 1
                ? await FindOrCreateChatGptClientAsync(redirectUris[0], cancellationToken)
                : null;
        }
    }

    private async Task<AiConnectorRegisteredClient?> FindFixedClientAsync(IReadOnlyList<string> redirectUris, CancellationToken cancellationToken)
    {
        using var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: false), requiresNew: true);
        AiConnectorRegisteredClient? client = null;
        foreach (var uri in redirectUris)
        {
            AiConnectorRegisteredClient? match = null;
            await foreach (var application in _applications.FindByRedirectUriAsync(uri, cancellationToken))
            {
                var clientId = await _applications.GetClientIdAsync(application, cancellationToken);
                if (AiConnectorConsts.IsConnectorClient(clientId)
                    && await _applications.HasClientTypeAsync(application, OpenIddictConstants.ClientTypes.Public, cancellationToken))
                {
                    match = new AiConnectorRegisteredClient(clientId!, await _applications.GetDisplayNameAsync(application, cancellationToken) ?? clientId!);
                    break;
                }
            }

            if (match == null || (client != null && client.ClientId != match.ClientId))
            {
                return null;
            }

            client = match;
        }

        await unitOfWork.CompleteAsync(cancellationToken);
        return client;
    }

    private async Task<AiConnectorRegisteredClient> FindOrCreateChatGptClientAsync(string redirectUri, CancellationToken cancellationToken)
    {
        var clientId = AiConnectorClients.ChatGptClientId(redirectUri);

        /* Read committed, so the find after the lock sees what the previous holder committed; a snapshot isolation level would
         * fix its view at the lock statement and create a duplicate. */
        using var unitOfWork = _unitOfWorkManager.Begin(
            new AbpUnitOfWorkOptions(isTransactional: true, isolationLevel: IsolationLevel.ReadCommitted), requiresNew: true);

        /* Find-then-create is one step per client id on every instance, canary revisions included: all of them use this host
         * database, and OpenIddictApplications.ClientId has no unique index to stop a second row. Released by the commit, so
         * the next holder finds the row. The only raw SQL here; it reads no data (design 74 "Build notes"). */
        var dbContext = await _openIddictDbContext.GetDbContextAsync();
        await dbContext.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({clientId}, 0))", cancellationToken);

        /* The repository, not the manager: the manager may answer from its cache per instance, which says nothing about what
         * the previous holder of the lock committed on another instance. */
        if (await _applicationRepository.FindByClientIdAsync(clientId, cancellationToken) == null)
        {
            await _applications.CreateAsync(
                AiConnectorClients.CreateDescriptor(clientId, AiConnectorConsts.ChatGptDisplayName, null, [redirectUri]), cancellationToken);
            _logger.LogInformation("AI connector client {ClientId} registered", clientId);
        }

        await unitOfWork.CompleteAsync(cancellationToken);
        return new AiConnectorRegisteredClient(clientId, AiConnectorConsts.ChatGptDisplayName);
    }
}
