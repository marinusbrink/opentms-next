using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenTms.Integrations.Features;
using OpenTms.Integrations.OrderIntake;
using OpenTms.Integrations.Permissions;
using OpenTms.Orders.Intake;
using OpenTms.Platform.Features;
using Shouldly;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.FeatureManagement;
using Volo.Abp.MultiTenancy;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Security.Claims;
using Volo.Abp.Uow;
using Xunit;

namespace OpenTms.HostIntegration;

/// <summary>
/// Design 72 "Tokens for API clients" and the test risk analysis' first row, end to end through the real host: an API
/// client created in tenant A by the API client service gets a token from <c>POST /connect/token</c> (client credentials)
/// while asking for tenant B through <c>__tenant</c> in the query, a header or the form; the token's tenant is A and the
/// order it posts to <c>POST /api/integrations/intake/orders</c> lands in tenant A's own database, never in B's (B lives in
/// the host database). A client whose permission grant was removed, a deactivated client and an inactive client row get
/// 403 and record nothing; another OpenIddict client-credentials application gets no tenant or API-client claim from the
/// handler and cannot submit even with the permission; the SPA's public application gets no client-credentials token at
/// all. Also review B1 (concurrent submits of one client, throttled LastUsedAt) and minor 7 (a concurrent duplicate) on
/// PostgreSQL through HTTP. Risk class: Critical — authentication, authorization and tenant isolation.
/// </summary>
[Collection(HostIntegrationCollection.Name)]
public class ApiClientTokenRoundTripTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HostIntegrationFixture _host;

    public ApiClientTokenRoundTripTests(HostIntegrationFixture host)
    {
        _host = host;
    }

    private Guid TenantA => _host.TenantAId;

    private Guid TenantB => _host.TenantBId;

    /* ---------- Arrangement through the host's own services ---------- */

    private static readonly string[] IntakeFeatures =
    [
        IntegrationsFeatures.OrderIntake,
        AppFeatures.Integrations,
        AppFeatures.IntegrationsApp.Intake.Default,
        AppFeatures.IntegrationsApp.Intake.ApiClients
    ];

    private async Task InScopeAsync(Guid? tenant, Func<IServiceProvider, Task> action, ClaimsPrincipal? principal = null)
    {
        using var scope = _host.Services.CreateScope();
        var services = scope.ServiceProvider;
        using var tenantScope = services.GetRequiredService<ICurrentTenant>().Change(tenant);
        using var principalScope = principal == null ? null : services.GetRequiredService<ICurrentPrincipalAccessor>().Change(principal);
        var unitOfWorkManager = services.GetRequiredService<IUnitOfWorkManager>();
        using var unitOfWork = unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);
        await action(services);
        await unitOfWork.CompleteAsync();
    }

    private async Task<T> InScopeAsync<T>(Guid? tenant, Func<IServiceProvider, Task<T>> action, ClaimsPrincipal? principal = null)
    {
        T result = default!;
        await InScopeAsync(tenant, async services => { result = await action(services); }, principal);
        return result;
    }

    private async Task EnableIntakeAsync()
    {
        await InScopeAsync(null, async services =>
        {
            var features = services.GetRequiredService<IFeatureManager>();
            foreach (var tenant in new[] { TenantA, TenantB })
            {
                foreach (var feature in IntakeFeatures)
                {
                    await features.SetForTenantAsync(tenant, feature, "true");
                }
            }
        });
    }

    /// <summary>A tenant-A user who may manage API clients (granted to the user, so no seeded role is relied on).</summary>
    private async Task<ClaimsPrincipal> ManagerOfAsync(Guid tenant)
    {
        var userId = Guid.NewGuid();
        await InScopeAsync(tenant, async services =>
        {
            var permissions = services.GetRequiredService<IPermissionManager>();
            await permissions.SetForUserAsync(userId, IntegrationsPermissions.ApiClients.Default, true);
            await permissions.SetForUserAsync(userId, IntegrationsPermissions.ApiClients.Manage, true);
        });
        return new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(AbpClaimTypes.UserId, userId.ToString()),
            new Claim(AbpClaimTypes.TenantId, tenant.ToString()),
            new Claim(AbpClaimTypes.UserName, "api-client-manager")
        ], "Test"));
    }

    /// <summary>Creates an API client in <paramref name="tenant"/> through <see cref="IApiClientAppService"/>, as the screen does.</summary>
    private async Task<ApiClientSecretDto> CreateClientAsync(Guid tenant, string name)
    {
        await EnableIntakeAsync();
        var manager = await ManagerOfAsync(tenant);
        return await InScopeAsync(tenant,
            services => services.GetRequiredService<IApiClientAppService>().CreateAsync(new ApiClientCreateDto { Name = name }),
            manager);
    }

    private async Task DeactivateViaServiceAsync(Guid tenant, Guid id)
    {
        var manager = await ManagerOfAsync(tenant);
        await InScopeAsync(tenant, services => services.GetRequiredService<IApiClientAppService>().DeactivateAsync(id), manager);
    }

    private Task<ApiClient> ReadClientAsync(Guid id)
        => InScopeAsync(TenantA, services => services.GetRequiredService<IRepository<ApiClient, Guid>>().GetAsync(id));

    private Task<List<InboundMessage>> MessagesAsync(Guid? tenant, string externalId)
        => InScopeAsync(tenant, services => services.GetRequiredService<IRepository<InboundMessage, Guid>>()
            .GetListAsync(m => m.ExternalId == externalId));

    /* ---------- HTTP ---------- */

    private sealed record TokenAnswer(HttpStatusCode Status, string? AccessToken, string Body);

    private async Task<TokenAnswer> RequestTokenAsync(string clientId, string secret, string? tenantInQuery = null, string? tenantInHeader = null, string? tenantInForm = null)
    {
        using var client = _host.CreateClient();
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = OpenIddictConstants.GrantTypes.ClientCredentials,
            ["client_id"] = clientId,
            ["client_secret"] = secret,
            ["scope"] = OrderIntakeConsts.ApiScope
        };
        if (tenantInForm != null)
        {
            form["__tenant"] = tenantInForm;
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "/connect/token" + (tenantInQuery == null ? string.Empty : "?__tenant=" + Uri.EscapeDataString(tenantInQuery)))
        {
            Content = new FormUrlEncodedContent(form)
        };
        if (tenantInHeader != null)
        {
            request.Headers.Add("__tenant", tenantInHeader);
        }

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        string? token = null;
        if (response.IsSuccessStatusCode)
        {
            token = JsonDocument.Parse(body).RootElement.GetProperty("access_token").GetString();
        }

        return new TokenAnswer(response.StatusCode, token, body);
    }

    /// <summary>The access token's claims, decrypted and validated with the host's own OpenIddict server credentials.</summary>
    private async Task<IReadOnlyList<Claim>> ClaimsOfAsync(string accessToken)
    {
        var options = _host.Services.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>().CurrentValue;
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(accessToken, new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            IssuerSigningKeys = options.SigningCredentials.Select(c => c.Key).ToList(),
            TokenDecryptionKeys = options.EncryptionCredentials.Select(c => c.Key).ToList()
        });
        result.IsValid.ShouldBeTrue(result.Exception?.Message);
        return result.ClaimsIdentity.Claims.ToList();
    }

    private static IntakeOrderRequestDto Order(string externalId) => new()
    {
        ExternalId = externalId,
        Order = new OrderIntakeDto
        {
            CustomerReference = "HOST-" + externalId,
            Lines =
            [
                new OrderIntakeLineDto
                {
                    TransportUnitName = "Europallet",
                    Stops =
                    [
                        new OrderIntakeStopDto { Role = IntakeStopRole.Pickup, Date = "2026-11-04", Address = new OrderIntakeAddressDto { PostalCode = "1234AB", HouseNumber = "1" } },
                        new OrderIntakeStopDto { Role = IntakeStopRole.Delivery, Date = "2026-11-05", Address = new OrderIntakeAddressDto { PostalCode = "5678CD", HouseNumber = "2" } }
                    ]
                }
            ]
        }
    };

    /// <summary>Posts an order with the token, while the request itself asks for tenant B in the query and a header.</summary>
    private async Task<HttpResponseMessage> SubmitAsync(string accessToken, string externalId, bool askForTenantB = true)
    {
        using var client = _host.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/intake/orders" + (askForTenantB ? "?__tenant=" + HostIntegrationFixture.TenantBName : string.Empty))
        {
            Content = JsonContent.Create(Order(externalId), options: Json)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (askForTenantB)
        {
            request.Headers.Add("__tenant", TenantB.ToString());
        }

        return await client.SendAsync(request);
    }

    private async Task<string> TokenFor(ApiClientSecretDto client)
    {
        var answer = await RequestTokenAsync(client.ClientId, client.ClientSecret);
        answer.Status.ShouldBe(HttpStatusCode.OK, answer.Body);
        return answer.AccessToken!;
    }

    /* ---------- The tenant comes from the application, never from __tenant ---------- */

    [Theory]
    [InlineData("query")]
    [InlineData("header")]
    [InlineData("form")]
    public async Task A_client_of_tenant_A_asking_for_tenant_B_gets_a_tenant_A_token_and_its_order_lands_in_tenant_A(string where)
    {
        var created = await CreateClientAsync(TenantA, "ERP " + where);
        var answer = where switch
        {
            "query" => await RequestTokenAsync(created.ClientId, created.ClientSecret, tenantInQuery: HostIntegrationFixture.TenantBName),
            "header" => await RequestTokenAsync(created.ClientId, created.ClientSecret, tenantInHeader: TenantB.ToString()),
            _ => await RequestTokenAsync(created.ClientId, created.ClientSecret, tenantInForm: HostIntegrationFixture.TenantBName)
        };
        answer.Status.ShouldBe(HttpStatusCode.OK, answer.Body);

        var claims = await ClaimsOfAsync(answer.AccessToken!);
        claims.Where(c => c.Type == AbpClaimTypes.TenantId).Select(c => c.Value).ShouldBe([TenantA.ToString()], "the application's tenant, never the requested one");
        claims.Where(c => c.Type == OrderIntakeConsts.ApiClientIdProperty).Select(c => c.Value).ShouldBe([created.Id.ToString()]);

        var externalId = "rt-" + where + "-" + Guid.NewGuid().ToString("N")[..8];
        using var response = await SubmitAsync(answer.AccessToken!, externalId);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        var result = JsonSerializer.Deserialize<IntakeResultDto>(body, Json)!;

        var inA = (await MessagesAsync(TenantA, externalId)).ShouldHaveSingleItem("the order lands in tenant A's own database");
        inA.Id.ShouldBe(result.InboundMessageId);
        inA.TenantId.ShouldBe(TenantA);
        inA.SourceId.ShouldBe(created.Id);
        (await MessagesAsync(TenantB, externalId)).ShouldBeEmpty("tenant B (host database) receives nothing");
        (await MessagesAsync(null, externalId)).ShouldBeEmpty("nor does the host");
        (await InScopeAsync(TenantA, services => services.GetRequiredService<IRepository<OrderIntake, Guid>>()
            .CountAsync(i => i.CustomerReference == "HOST-" + externalId))).ShouldBe(1, "Orders' intake is tenant A's too");
    }

    [Fact]
    public async Task The_api_resolves_an_api_client_token_to_its_own_tenant_whatever_the_request_asks_for()
    {
        var created = await CreateClientAsync(TenantA, "ERP config");
        var token = await TokenFor(created);

        using var client = _host.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/abp/application-configuration?__tenant=" + HostIntegrationFixture.TenantBName);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("__tenant", TenantB.ToString());
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);

        var currentTenant = JsonDocument.Parse(body).RootElement.GetProperty("currentTenant");
        currentTenant.GetProperty("id").GetString().ShouldBe(TenantA.ToString());
    }

    /* ---------- Refusals ---------- */

    [Fact]
    public async Task A_client_whose_permission_grant_was_removed_gets_403_and_records_nothing()
    {
        var created = await CreateClientAsync(TenantA, "ERP no grant");
        await InScopeAsync(null, services => services.GetRequiredService<IPermissionManager>()
            .SetAsync(IntegrationsPermissions.IntakeApi.Submit, ClientPermissionValueProvider.ProviderName, created.ClientId, false));

        var token = await TokenFor(created);
        var externalId = "rt-nogrant-" + Guid.NewGuid().ToString("N")[..8];
        using var response = await SubmitAsync(token, externalId, askForTenantB: false);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        (await MessagesAsync(TenantA, externalId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_deactivated_client_gets_403_with_a_new_token_and_with_the_one_it_still_holds()
    {
        var created = await CreateClientAsync(TenantA, "ERP deactivated");
        var heldToken = await TokenFor(created);
        await DeactivateViaServiceAsync(TenantA, created.Id);

        var externalId = "rt-deact-" + Guid.NewGuid().ToString("N")[..8];
        using (var response = await SubmitAsync(heldToken, externalId, askForTenantB: false))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        }

        var fresh = await RequestTokenAsync(created.ClientId, created.ClientSecret);
        if (fresh.Status == HttpStatusCode.OK)
        {
            using var response = await SubmitAsync(fresh.AccessToken!, externalId, askForTenantB: false);
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        }

        (await MessagesAsync(TenantA, externalId)).ShouldBeEmpty();
        (await ReadClientAsync(created.Id)).IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task An_inactive_client_row_whose_grant_is_still_in_place_gets_403()
    {
        /* The service's own check (design 72 "Intake API"), independent of the permission grant. */
        var created = await CreateClientAsync(TenantA, "ERP inactive row");
        await InScopeAsync(TenantA, async services =>
        {
            var repository = services.GetRequiredService<IRepository<ApiClient, Guid>>();
            var row = await repository.GetAsync(created.Id);
            row.Deactivate();
            await repository.UpdateAsync(row, autoSave: true);
        });

        var externalId = "rt-inactive-" + Guid.NewGuid().ToString("N")[..8];
        using var response = await SubmitAsync(await TokenFor(created), externalId, askForTenantB: false);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, body);
        body.ShouldContain(OpenTms.Integrations.IntegrationsErrorCodes.ApiClientInactive);
        (await MessagesAsync(TenantA, externalId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Another_client_credentials_application_gets_no_tenant_or_api_client_claim_from_the_handler_and_cannot_submit()
    {
        await EnableIntakeAsync();
        const string otherId = "host-test-other-confidential";
        const string otherSecret = "a-long-enough-secret-for-the-other-client-0123456789";
        await InScopeAsync(null, async services =>
        {
            var applications = services.GetRequiredService<IOpenIddictApplicationManager>();
            if (await applications.FindByClientIdAsync(otherId) == null)
            {
                await applications.CreateAsync(new OpenIddictApplicationDescriptor
                {
                    ClientId = otherId,
                    ClientSecret = otherSecret,
                    ClientType = OpenIddictConstants.ClientTypes.Confidential,
                    DisplayName = "Not an API client",
                    Permissions =
                    {
                        OpenIddictConstants.Permissions.Endpoints.Token,
                        OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                        OpenIddictConstants.Permissions.Prefixes.Scope + OrderIntakeConsts.ApiScope
                    }
                });
            }

            /* Even with the intake permission it is not an API client of any tenant. */
            await services.GetRequiredService<IPermissionManager>()
                .SetAsync(IntegrationsPermissions.IntakeApi.Submit, ClientPermissionValueProvider.ProviderName, otherId, true);
        });

        var answer = await RequestTokenAsync(otherId, otherSecret, tenantInQuery: HostIntegrationFixture.TenantBName);
        answer.Status.ShouldBe(HttpStatusCode.OK, answer.Body);
        var claims = await ClaimsOfAsync(answer.AccessToken!);
        claims.ShouldNotContain(c => c.Type == OrderIntakeConsts.ApiClientIdProperty);
        claims.Where(c => c.Type == AbpClaimTypes.TenantId).Select(c => c.Value).ShouldNotContain(TenantA.ToString());

        var externalId = "rt-other-" + Guid.NewGuid().ToString("N")[..8];
        using var response = await SubmitAsync(answer.AccessToken!, externalId);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        (await MessagesAsync(TenantA, externalId)).ShouldBeEmpty();
        (await MessagesAsync(TenantB, externalId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_spa_application_gets_no_client_credentials_token()
    {
        /* OpenTms_App is a public authorization-code client: the handler never sees a client-credentials token for it. */
        var answer = await RequestTokenAsync("OpenTms_App", "anything", tenantInQuery: HostIntegrationFixture.TenantBName);
        answer.Status.ShouldNotBe(HttpStatusCode.OK, answer.Body);
        answer.AccessToken.ShouldBeNull();
    }

    /* ---------- Review B1 and minor 7 on PostgreSQL through HTTP ---------- */

    [Fact]
    public async Task Two_concurrent_submits_of_one_client_both_succeed_and_LastUsedAt_is_throttled_to_one_write_per_five_minutes()
    {
        var created = await CreateClientAsync(TenantA, "ERP concurrent");
        var token = await TokenFor(created);
        var stampBefore = (await ReadClientAsync(created.Id)).ConcurrencyStamp;

        var ids = new[] { "rt-par-1-" + Guid.NewGuid().ToString("N")[..8], "rt-par-2-" + Guid.NewGuid().ToString("N")[..8] };
        var responses = await Task.WhenAll(ids.Select(id => SubmitAsync(token, id, askForTenantB: false)));
        foreach (var response in responses)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            response.Dispose();
        }

        var afterFirst = await ReadClientAsync(created.Id);
        afterFirst.LastUsedAt.ShouldNotBeNull();
        afterFirst.ConcurrencyStamp.ShouldBe(stampBefore, "the use stamp is written outside the aggregate's concurrency check");
        foreach (var id in ids)
        {
            (await MessagesAsync(TenantA, id)).ShouldHaveSingleItem();
        }

        /* Within five minutes: not written again. */
        using (var third = await SubmitAsync(token, "rt-par-3-" + Guid.NewGuid().ToString("N")[..8], askForTenantB: false))
        {
            third.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await ReadClientAsync(created.Id)).LastUsedAt.ShouldBe(afterFirst.LastUsedAt);

        /* Older than five minutes: written again. */
        var backdated = afterFirst.LastUsedAt!.Value.AddMinutes(-6);
        await using (var connection = new NpgsqlConnection(HostIntegrationFixture.ConnectionStringFor(HostIntegrationFixture.TenantADatabase)))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("UPDATE integrations.\"ApiClients\" SET \"LastUsedAt\" = @at WHERE \"Id\" = @id", connection);
            command.Parameters.AddWithValue("at", DateTime.SpecifyKind(backdated, DateTimeKind.Unspecified));
            command.Parameters.AddWithValue("id", created.Id);
            (await command.ExecuteNonQueryAsync()).ShouldBe(1);
        }

        using (var fourth = await SubmitAsync(token, "rt-par-4-" + Guid.NewGuid().ToString("N")[..8], askForTenantB: false))
        {
            fourth.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await ReadClientAsync(created.Id)).LastUsedAt!.Value.ShouldBeGreaterThan(backdated.AddMinutes(5));
        (await ReadClientAsync(created.Id)).ConcurrencyStamp.ShouldBe(stampBefore);
    }

    [Fact]
    public async Task Eight_concurrent_submits_of_one_client_with_different_external_ids_all_succeed()
    {
        /* Review B1 under a little load: a customer's ERP sends a batch in parallel. */
        var created = await CreateClientAsync(TenantA, "ERP batch");
        var token = await TokenFor(created);
        var ids = Enumerable.Range(1, 8).Select(i => "rt-batch-" + i + "-" + Guid.NewGuid().ToString("N")[..8]).ToList();

        var responses = await Task.WhenAll(ids.Select(id => SubmitAsync(token, id, askForTenantB: false)));
        var failures = new List<string>();
        foreach (var response in responses)
        {
            if (response.StatusCode != HttpStatusCode.OK)
            {
                failures.Add((int)response.StatusCode + " " + await response.Content.ReadAsStringAsync());
            }

            response.Dispose();
        }

        failures.ShouldBeEmpty();
        foreach (var id in ids)
        {
            (await MessagesAsync(TenantA, id)).ShouldHaveSingleItem();
        }
    }

    [Fact]
    public async Task Two_concurrent_submits_with_the_same_external_id_answer_the_same_message_and_store_one()
    {
        var created = await CreateClientAsync(TenantA, "ERP duplicate");
        var token = await TokenFor(created);
        var externalId = "rt-dup-" + Guid.NewGuid().ToString("N")[..8];

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => SubmitAsync(token, externalId, askForTenantB: false)));
        var results = new List<IntakeResultDto>();
        foreach (var response in responses)
        {
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
            results.Add(JsonSerializer.Deserialize<IntakeResultDto>(body, Json)!);
            response.Dispose();
        }

        results.Select(r => r.InboundMessageId).Distinct().ShouldHaveSingleItem("every caller gets the one stored message");
        (await MessagesAsync(TenantA, externalId)).ShouldHaveSingleItem();
        (await InScopeAsync(TenantA, services => services.GetRequiredService<IRepository<OrderIntake, Guid>>()
            .CountAsync(i => i.CustomerReference == "HOST-" + externalId))).ShouldBe(1, "one intake, whoever won");
    }
}
