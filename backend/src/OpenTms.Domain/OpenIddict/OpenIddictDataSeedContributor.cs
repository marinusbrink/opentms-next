using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using OpenIddict.Abstractions;
using OpenTms.Integrations.AiConnector;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.OpenIddict;
using Volo.Abp.OpenIddict.Applications;
using Volo.Abp.OpenIddict.Scopes;
using Volo.Abp.Uow;

namespace OpenTms.OpenIddict;

/* Creates initial data that is needed to property run the application
 * and make client-to-server communication possible.
 */
public class OpenIddictDataSeedContributor : OpenIddictDataSeedContributorBase, IDataSeedContributor, ITransientDependency
{
    public OpenIddictDataSeedContributor(
        IConfiguration configuration,
        IOpenIddictApplicationRepository openIddictApplicationRepository,
        IAbpApplicationManager applicationManager,
        IOpenIddictScopeRepository openIddictScopeRepository,
        IOpenIddictScopeManager scopeManager)
        : base(configuration, openIddictApplicationRepository, applicationManager, openIddictScopeRepository, scopeManager)
    {
    }

    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        await CreateScopesAsync();
        await CreateApplicationsAsync();
    }

    private async Task CreateScopesAsync()
    {
        await CreateScopesAsync(new OpenIddictScopeDescriptor 
        {
            Name = "OpenTms", 
            DisplayName = "OpenTms API", 
            Resources = { "OpenTms" }
        });

        /* Design 74: the AI connector's scope; its resource is a token's only audience, so the token reaches /mcp alone. */
        await CreateScopesAsync(new OpenIddictScopeDescriptor
        {
            Name = AiConnectorConsts.Scope,
            DisplayName = "OpenTms AI connector",
            Resources = { AiConnectorConsts.Resource }
        });
    }

    private async Task CreateApplicationsAsync()
    {
        var commonScopes = new List<string> {
            OpenIddictConstants.Permissions.Scopes.Address,
            OpenIddictConstants.Permissions.Scopes.Email,
            OpenIddictConstants.Permissions.Scopes.Phone,
            OpenIddictConstants.Permissions.Scopes.Profile,
            OpenIddictConstants.Permissions.Scopes.Roles,
            "OpenTms"
        };

        var configurationSection = Configuration.GetSection("OpenIddict:Applications");


        // OpenTMS web app (React SPA): authorization code + PKCE, public client.
        var appClientId = configurationSection["OpenTms_App:ClientId"];
        if (!appClientId.IsNullOrWhiteSpace())
        {
            var appClientRootUrl = configurationSection["OpenTms_App:RootUrl"]?.TrimEnd('/');
            await CreateOrUpdateApplicationAsync(
                applicationType: OpenIddictConstants.ApplicationTypes.Web,
                name: appClientId!,
                type: OpenIddictConstants.ClientTypes.Public,
                consentType: OpenIddictConstants.ConsentTypes.Implicit,
                displayName: "OpenTMS Web Application",
                secret: null,
                grantTypes: new List<string> {
                    OpenIddictConstants.GrantTypes.AuthorizationCode,
                    OpenIddictConstants.GrantTypes.RefreshToken
                },
                scopes: commonScopes,
                redirectUris: new List<string> { $"{appClientRootUrl}/auth/callback" },
                postLogoutRedirectUris: new List<string> { appClientRootUrl },
                clientUri: appClientRootUrl,
                logoUri: null
            );
        }

        
        




        // Swagger Client
        var swaggerClientId = configurationSection["OpenTms_Swagger:ClientId"];
        if (!swaggerClientId.IsNullOrWhiteSpace())
        {
            var swaggerRootUrl = configurationSection["OpenTms_Swagger:RootUrl"]?.TrimEnd('/');

            await CreateOrUpdateApplicationAsync(
                applicationType: OpenIddictConstants.ApplicationTypes.Web,
                name: swaggerClientId!,
                type: OpenIddictConstants.ClientTypes.Public,
                consentType: OpenIddictConstants.ConsentTypes.Implicit,
                displayName: "Swagger Application",
                secret: null,
                grantTypes: new List<string> { OpenIddictConstants.GrantTypes.AuthorizationCode, },
                scopes: commonScopes,
                redirectUris: new List<string> { $"{swaggerRootUrl}/swagger/oauth2-redirect.html" },
                clientUri: swaggerRootUrl.EnsureEndsWith('/') + "swagger",
                logoUri: "/images/clients/swagger.svg"
            );
        }

        await CreateAiConnectorClientsAsync(configurationSection);
    }

    /// <summary>
    /// Design 74 "Approved tools": every entry of <c>OpenIddict:Applications</c> whose key starts with
    /// <see cref="AiConnectorConsts.ClientIdPrefix"/> is an approved AI tool, so adding one is a configuration change.
    /// A confidential tool (Gemini) is seeded only when its secret is configured; ChatGPT's clients arrive at registration.
    /// </summary>
    private async Task CreateAiConnectorClientsAsync(IConfigurationSection applications)
    {
        foreach (var tool in applications.GetChildren().Where(section => AiConnectorConsts.IsConnectorClient(section.Key)))
        {
            var clientId = tool["ClientId"];
            if (clientId.IsNullOrWhiteSpace())
            {
                continue;
            }

            var secret = tool["ClientSecret"];
            var isConfidential = string.Equals(tool["ClientType"], OpenIddictConstants.ClientTypes.Confidential, StringComparison.OrdinalIgnoreCase);
            if (isConfidential && secret.IsNullOrWhiteSpace())
            {
                continue;
            }

            var descriptor = AiConnectorClients.CreateDescriptor(
                clientId!,
                tool["DisplayName"] ?? clientId!,
                isConfidential ? secret : null,
                tool.GetSection("RedirectUris").GetChildren().Select(uri => uri.Value!).ToList());

            var existing = await OpenIddictApplicationRepository.FindByClientIdAsync(clientId!);
            if (existing == null)
            {
                await ApplicationManager.CreateAsync(descriptor);
            }
            else
            {
                await ApplicationManager.UpdateAsync(existing.ToModel(), descriptor);
            }
        }
    }
}
