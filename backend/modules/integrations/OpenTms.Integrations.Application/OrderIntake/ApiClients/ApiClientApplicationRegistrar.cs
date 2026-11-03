using System;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenIddict.Abstractions;
using OpenTms.Integrations.Permissions;
using Volo.Abp;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Uow;

namespace OpenTms.Integrations.OrderIntake.ApiClients;

/// <summary>
/// The host side of an API client (design 72 "API client", "Tenant dimension"): its OpenIddict application in the host
/// database — confidential, grant type <c>client_credentials</c> only, scope <see cref="OrderIntakeConsts.ApiScope"/>,
/// properties <see cref="OrderIntakeConsts.TenantIdProperty"/> and <see cref="OrderIntakeConsts.ApiClientIdProperty"/> that
/// the token server turns into the token's tenant — and its client permission grant
/// <c>Integrations.IntakeApi.Submit</c> (provider <c>C</c>, stored host side, where ABP's client permission provider reads
/// it). Every operation runs in the host's context and in a unit of work of its own, committed before the tenant row
/// changes; the caller compensates when the tenant side then fails.
/// </summary>
public class ApiClientApplicationRegistrar : ITransientDependency
{
    private const string Base32Alphabet = "abcdefghijklmnopqrstuvwxyz234567";
    private const string SecretAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    private readonly IOpenIddictApplicationManager _applications;
    private readonly IPermissionManager _permissions;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public ApiClientApplicationRegistrar(
        IOpenIddictApplicationManager applications,
        IPermissionManager permissions,
        ICurrentTenant currentTenant,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _applications = applications;
        _permissions = permissions;
        _currentTenant = currentTenant;
        _unitOfWorkManager = unitOfWorkManager;
    }

    /// <summary><c>otms_</c> + 24 random base32 characters.</summary>
    public static string NewClientId()
        => OrderIntakeConsts.ClientIdPrefix + new string(RandomNumberGenerator.GetItems<char>(Base32Alphabet, OrderIntakeConsts.ClientIdRandomLength));

    /// <summary>40 random letters and digits; shown once.</summary>
    public static string NewSecret()
        => new(RandomNumberGenerator.GetItems<char>(SecretAlphabet, OrderIntakeConsts.ClientSecretLength));

    /// <summary>Creates the application with its properties and grants the intake permission.</summary>
    public Task CreateAsync(string clientId, string secret, string displayName, Guid tenantId, Guid apiClientId, CancellationToken cancellationToken = default)
        => InHostAsync(async () =>
        {
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = clientId,
                ClientSecret = secret,
                ClientType = OpenIddictConstants.ClientTypes.Confidential,
                DisplayName = displayName,
                Permissions =
                {
                    OpenIddictConstants.Permissions.Endpoints.Token,
                    OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                    OpenIddictConstants.Permissions.Prefixes.Scope + OrderIntakeConsts.ApiScope
                }
            };
            descriptor.Properties[OrderIntakeConsts.TenantIdProperty] = JsonSerializer.SerializeToElement(tenantId.ToString());
            descriptor.Properties[OrderIntakeConsts.ApiClientIdProperty] = JsonSerializer.SerializeToElement(apiClientId.ToString());
            await _applications.CreateAsync(descriptor, cancellationToken);
            await SetGrantAsync(clientId, true);
        });

    /// <summary>Replaces the secret (OpenIddict stores it hashed).</summary>
    public Task SetSecretAsync(string clientId, string secret, CancellationToken cancellationToken = default)
        => InHostAsync(async () =>
        {
            var application = await FindAsync(clientId, cancellationToken);
            var descriptor = new OpenIddictApplicationDescriptor();
            await _applications.PopulateAsync(descriptor, application, cancellationToken);
            descriptor.ClientSecret = secret;
            await _applications.UpdateAsync(application, descriptor, cancellationToken);
        });

    /// <summary>Grants (activate) or removes (deactivate) <c>Integrations.IntakeApi.Submit</c>.</summary>
    public Task SetActiveAsync(string clientId, bool active)
        => InHostAsync(() => SetGrantAsync(clientId, active));

    /// <summary>Deletes the application and every client permission grant; an application already gone is fine.</summary>
    public Task DeleteAsync(string clientId, CancellationToken cancellationToken = default)
        => InHostAsync(async () =>
        {
            var application = await _applications.FindByClientIdAsync(clientId, cancellationToken);
            if (application != null)
            {
                await _applications.DeleteAsync(application, cancellationToken);
            }

            await _permissions.DeleteAsync(ClientPermissionValueProvider.ProviderName, clientId);
        });

    private async Task<object> FindAsync(string clientId, CancellationToken cancellationToken)
        => await _applications.FindByClientIdAsync(clientId, cancellationToken)
           ?? throw new ApplicationMissingException(clientId);

    private Task SetGrantAsync(string clientId, bool granted)
        => _permissions.SetAsync(IntegrationsPermissions.IntakeApi.Submit, ClientPermissionValueProvider.ProviderName, clientId, granted);

    private async Task InHostAsync(Func<Task> action)
    {
        using (_currentTenant.Change(null))
        {
            using var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);
            await action();
            await unitOfWork.CompleteAsync();
        }
    }

    /// <summary>The OpenIddict application of an API client is missing in the host database.</summary>
    public class ApplicationMissingException : AbpException
    {
        public ApplicationMissingException(string clientId)
            : base($"The OpenIddict application of API client {clientId} does not exist.")
        {
        }
    }
}
