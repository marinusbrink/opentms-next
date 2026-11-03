using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using OpenTms.Integrations.Features;
using OpenTms.Integrations.Permissions;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;
using Volo.Abp.Timing;
using Volo.Abp.UI.Navigation.Urls;
using Volo.Abp.Validation;

namespace OpenTms.Integrations.OrderIntake.ApiClients;

/// <summary>
/// Integrations app → Order intake → API clients (design 72 "API client"). Each client is an OpenIddict
/// client-credentials application in the host database (<see cref="ApiClientApplicationRegistrar"/>) plus this tenant
/// row; the secret is returned once, by create and regenerate, and only its last four characters are kept. Deactivate
/// removes the client's permission grant, so a token it still holds no longer passes the intake API; delete also removes
/// the application. Customer and default order type are stored as given (their names come from the screen's lookups).
/// </summary>
[Authorize(IntegrationsPermissions.ApiClients.Default)]
[RequiresFeature(
    IntegrationsFeatures.OrderIntake,
    AppFeatures.Integrations,
    AppFeatures.IntegrationsApp.Intake.Default,
    AppFeatures.IntegrationsApp.Intake.ApiClients,
    RequiresAll = true)]
public class ApiClientAppService : IntegrationsAppServiceBase, IApiClientAppService
{
    /// <summary>OpenIddict's token endpoint below <c>App:SelfUrl</c>.</summary>
    public const string TokenPath = "connect/token";

    private readonly IRepository<ApiClient, Guid> _clients;
    private readonly ApiClientApplicationRegistrar _registrar;
    private readonly IAppUrlProvider _appUrlProvider;
    private readonly IClock _clock;

    public ApiClientAppService(
        IRepository<ApiClient, Guid> clients,
        ApiClientApplicationRegistrar registrar,
        IAppUrlProvider appUrlProvider,
        IClock clock)
    {
        _clients = clients;
        _registrar = registrar;
        _appUrlProvider = appUrlProvider;
        _clock = clock;
    }

    public async Task<GridResponse<ApiClientRowDto>> GetListAsync(ApiClientListRequestDto input)
    {
        var query = await _clients.GetQueryableAsync();
        var total = await AsyncExecuter.LongCountAsync(query);

        if (input.IsActive != null)
        {
            query = query.Where(c => c.IsActive == input.IsActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(input.WildcardSearch))
        {
            var term = input.WildcardSearch.Trim().ToLower();
            query = query.Where(c => c.Name.ToLower().Contains(term) || c.ClientId.Contains(term));
        }

        var filtered = await AsyncExecuter.LongCountAsync(query);
        var rows = await AsyncExecuter.ToListAsync(Sort(query, input.SortBy, input.Descending).Skip(input.StartRow).Take(input.EndRow - input.StartRow));
        return new GridResponse<ApiClientRowDto>
        {
            Rows = rows.Select(c => new ApiClientRowDto
            {
                Id = c.Id,
                Name = c.Name,
                ClientId = c.ClientId,
                CustomerRelationId = c.CustomerRelationId,
                IsActive = c.IsActive,
                LastUsedAt = c.LastUsedAt
            }).ToList(),
            TotalCount = total,
            FilteredCount = filtered
        };
    }

    public async Task<ApiClientDto> GetAsync(Guid id) => ToDto(await _clients.GetAsync(id));

    [Authorize(IntegrationsPermissions.ApiClients.Manage)]
    public async Task<ApiClientSecretDto> CreateAsync(ApiClientCreateDto input)
    {
        var tenantId = CurrentTenant.Id ?? throw new AbpAuthorizationException("API clients belong to a tenant.");
        var id = GuidGenerator.Create();
        var clientId = ApiClientApplicationRegistrar.NewClientId();
        var secret = ApiClientApplicationRegistrar.NewSecret();
        var client = new ApiClient(
            id, tenantId, input.Name, clientId, LastFour(secret), _clock.Now,
            input.CustomerRelationId, input.DefaultTransportOrderTypeId, input.AutoConfirm);

        await _registrar.CreateAsync(clientId, secret, client.Name, tenantId, id);
        try
        {
            await _clients.InsertAsync(client, autoSave: true);
        }
        catch
        {
            /* The application is committed in the host database already: take it back, or it would stay without a row. */
            await _registrar.DeleteAsync(clientId);
            throw;
        }

        Logger.LogInformation("API client {ApiClientId} created by user {UserId}.", id, CurrentUser.Id);
        return await SecretDtoAsync(client, secret);
    }

    [Authorize(IntegrationsPermissions.ApiClients.Manage)]
    public async Task<ApiClientDto> UpdateAsync(Guid id, ApiClientUpdateDto input)
    {
        var client = await _clients.GetAsync(id);
        client.ConcurrencyStamp = input.ConcurrencyStamp;
        client.Update(input.Name, input.CustomerRelationId, input.DefaultTransportOrderTypeId, input.AutoConfirm);
        await _clients.UpdateAsync(client, autoSave: true);
        return ToDto(client);
    }

    [Authorize(IntegrationsPermissions.ApiClients.Manage)]
    public async Task<ApiClientSecretDto> RegenerateSecretAsync(Guid id)
    {
        var client = await _clients.GetAsync(id);
        var secret = ApiClientApplicationRegistrar.NewSecret();
        await _registrar.SetSecretAsync(client.ClientId, secret);
        client.RecordSecret(LastFour(secret), _clock.Now);
        await _clients.UpdateAsync(client, autoSave: true);

        Logger.LogInformation("API client {ApiClientId}: secret regenerated by user {UserId}.", id, CurrentUser.Id);
        return await SecretDtoAsync(client, secret);
    }

    [Authorize(IntegrationsPermissions.ApiClients.Manage)]
    public async Task<ApiClientDto> ActivateAsync(Guid id)
    {
        var client = await _clients.GetAsync(id);
        await _registrar.SetActiveAsync(client.ClientId, true);
        client.Activate();
        await _clients.UpdateAsync(client, autoSave: true);
        Logger.LogInformation("API client {ApiClientId} activated by user {UserId}.", id, CurrentUser.Id);
        return ToDto(client);
    }

    [Authorize(IntegrationsPermissions.ApiClients.Manage)]
    public async Task<ApiClientDto> DeactivateAsync(Guid id)
    {
        var client = await _clients.GetAsync(id);
        await _registrar.SetActiveAsync(client.ClientId, false);
        client.Deactivate();
        await _clients.UpdateAsync(client, autoSave: true);
        Logger.LogInformation("API client {ApiClientId} deactivated by user {UserId}.", id, CurrentUser.Id);
        return ToDto(client);
    }

    [Authorize(IntegrationsPermissions.ApiClients.Manage)]
    public async Task DeleteAsync(Guid id)
    {
        var client = await _clients.GetAsync(id);
        await _registrar.DeleteAsync(client.ClientId);
        await _clients.DeleteAsync(client, autoSave: true);
        Logger.LogInformation("API client {ApiClientId} deleted by user {UserId}.", id, CurrentUser.Id);
    }

    private async Task<ApiClientSecretDto> SecretDtoAsync(ApiClient client, string secret)
    {
        string tokenUrl;
        try
        {
            tokenUrl = (await _appUrlProvider.GetUrlAsync("MVC")).TrimEnd('/') + "/" + TokenPath;
        }
        catch (AbpException)
        {
            tokenUrl = "/" + TokenPath;
        }

        return new ApiClientSecretDto
        {
            Id = client.Id,
            ClientId = client.ClientId,
            ClientSecret = secret,
            TokenUrl = tokenUrl,
            Scope = OrderIntakeConsts.ApiScope
        };
    }

    private static string LastFour(string secret) => secret[^OrderIntakeConsts.SecretLastFourLength..];

    private static IQueryable<ApiClient> Sort(IQueryable<ApiClient> query, string? sortBy, bool descending)
        => (sortBy?.ToLowerInvariant()) switch
        {
            null or "" or "name" => Order(query, c => c.Name, descending),
            "clientid" => Order(query, c => c.ClientId, descending),
            "isactive" => Order(query, c => c.IsActive, descending),
            "lastusedat" => Order(query, c => c.LastUsedAt, descending),
            "creationtime" => Order(query, c => c.CreationTime, descending),
            _ => throw new AbpValidationException([new System.ComponentModel.DataAnnotations.ValidationResult($"Unknown sort column '{sortBy}'.", ["SortBy"])])
        };

    private static IQueryable<ApiClient> Order<TKey>(IQueryable<ApiClient> query, Expression<Func<ApiClient, TKey>> key, bool descending)
        => (descending ? query.OrderByDescending(key) : query.OrderBy(key)).ThenBy(c => c.Id);

    private static ApiClientDto ToDto(ApiClient c) => new()
    {
        Id = c.Id,
        CreationTime = c.CreationTime,
        CreatorId = c.CreatorId,
        LastModificationTime = c.LastModificationTime,
        LastModifierId = c.LastModifierId,
        ConcurrencyStamp = c.ConcurrencyStamp,
        Name = c.Name,
        ClientId = c.ClientId,
        IsActive = c.IsActive,
        CustomerRelationId = c.CustomerRelationId,
        DefaultTransportOrderTypeId = c.DefaultTransportOrderTypeId,
        AutoConfirm = c.AutoConfirm,
        SecretLastFour = c.SecretLastFour,
        SecretCreatedAt = c.SecretCreatedAt,
        LastUsedAt = c.LastUsedAt
    };
}
