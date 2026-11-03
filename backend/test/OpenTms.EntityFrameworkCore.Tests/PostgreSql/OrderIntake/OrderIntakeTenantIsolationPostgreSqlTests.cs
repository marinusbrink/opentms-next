using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenTms.Features;
using OpenTms.Integrations.Features;
using OpenTms.Integrations.OrderIntake;
using OpenTms.Integrations.OrderIntake.Ai;
using OpenTms.Integrations.OrderIntake.Profiles;
using OpenTms.Integrations.Permissions;
using OpenTms.Orders.Intake;
using OpenTms.Orders.TransportOrders;
using OpenTms.Platform.Features;
using OpenTms.PostgreSql.ReferenceData;
using Shouldly;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Security.Claims;
using Xunit;

namespace OpenTms.PostgreSql.Intake;

using DocumentFormat = OpenTms.Integrations.OrderIntake.DocumentFormat;

/// <summary>
/// Tenant isolation of every table and service design 72 adds, against real PostgreSQL with database per tenant
/// (constitution rule 1; design 72 test risk analysis "Tenant isolation of every new repository and app service", Critical):
/// rows of tenant A (own database) are unreachable from the shared-tier tenant and the host by id and by list through the
/// services, and physically absent from the host database; an API client's OpenIddict application and its client permission
/// grant live in the host database with the tenant in the application's properties (design 72 "API client"), deactivation
/// removes the grant, deletion the application; AI usage records land in the host database (AP-05); and the intake API's
/// JSON path runs under the request's real transaction. The services are resolved per call, as a request does.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class OrderIntakeTenantIsolationPostgreSqlTests : ReferenceDataPostgreSqlTestBase
{
    private static IDisposable IntakeOn()
    {
        var a = TestFeatureValueProvider.Override(IntegrationsFeatures.OrderIntake, "true");
        var b = TestFeatureValueProvider.Override(AppFeatures.Integrations, "true");
        return new Both(a, b);
    }

    private sealed class Both(params IDisposable[] all) : IDisposable
    {
        public void Dispose()
        {
            foreach (var d in all.Reverse())
            {
                d.Dispose();
            }
        }
    }

    private sealed record Probe(Guid Mailbox, Guid ApiClient, Guid Message, Guid Document, Guid Profile, Guid Intake, Guid Alias);

    private async Task<Probe> WriteProbeAsync()
    {
        var tenant = CurrentTenant.Id;
        var mailbox = new MailboxConnection(Guid.NewGuid(), tenant, MailProvider.Gmail, "QQ isolation mailbox");
        mailbox.CompleteConsent("qq.isolation@acme.nl", "encrypted", DateTime.UtcNow);
        await GetRequiredService<IRepository<MailboxConnection, Guid>>().InsertAsync(mailbox);

        var client = new ApiClient(Guid.NewGuid(), tenant, "QQ isolation client", "otms_" + Guid.NewGuid().ToString("N")[..24].ToUpperInvariant(), "abcd", DateTime.UtcNow, null, null, false);
        await GetRequiredService<IRepository<ApiClient, Guid>>().InsertAsync(client);

        var message = new InboundMessage(Guid.NewGuid(), tenant, InboundChannel.Gmail, mailbox.Id, "qq-isolation-" + Guid.NewGuid().ToString("N")[..8], "qq@acme.nl", "QQ isolation order", DateTime.UtcNow, 10);
        await GetRequiredService<IRepository<InboundMessage, Guid>>().InsertAsync(message);
        var document = new InboundDocument(Guid.NewGuid(), tenant, message.Id, InboundDocumentKind.Attachment, "qq.csv", "text/csv", 10, new string('a', 64), message.Id + "/doc", DocumentFormat.Csv);
        document.AddStep(Guid.NewGuid(), DateTime.UtcNow, IntakeStep.Received, StepOutcome.Info, "Intake:Step:Received");
        await GetRequiredService<IRepository<InboundDocument, Guid>>().InsertAsync(document);

        var profile = new ImportProfile(Guid.NewGuid(), tenant, "QQ isolation profile", "qq@acme.nl", DocumentFormat.Csv, OrderIntakeConsts.TransportOrderTarget,
            new ImportProfileRules { Fields = [new FieldRule { Target = "order.customerReference", Source = new FieldSource { Kind = FieldSourceKind.Constant, Value = "x" } }] },
            ["Ref"], ProfileOrigin.Ai, "claude-opus-5-5", document.Id);
        await GetRequiredService<IRepository<ImportProfile, Guid>>().InsertAsync(profile);

        var intake = new OrderIntake(Guid.NewGuid(), tenant, document.Id, 0, "Gmail", OrderSource.Import, "{\"customerReference\":\"QQ-ISO\",\"lines\":[]}",
            new OrderIntakeChannelSettings("qq@acme.nl", "QQ isolation order", "qq.csv", 0.9m, false, false, null, null, null));
        await GetRequiredService<IRepository<OrderIntake, Guid>>().InsertAsync(intake);
        var alias = new IntakeAlias(Guid.NewGuid(), tenant, IntakeAliasKind.Customer, null, "qq isolation klant", Guid.NewGuid(), null);
        await GetRequiredService<IRepository<IntakeAlias, Guid>>().InsertAsync(alias);

        await FlushAsync();
        return new Probe(mailbox.Id, client.Id, message.Id, document.Id, profile.Id, intake.Id, alias.Id);
    }

    [Fact]
    public async Task Every_order_intake_table_of_tenant_A_is_unreachable_from_the_shared_tenant_and_the_host()
    {
        using var features = IntakeOn();
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var a = await WriteProbeAsync();

            foreach (var other in new Guid?[] { SharedTenant, null })
            {
                using (CurrentTenant.Change(other))
                {
                    var who = other == null ? "host" : "shared tenant";

                    (await GetRequiredService<IRepository<MailboxConnection, Guid>>().FindAsync(a.Mailbox)).ShouldBeNull(who);
                    (await GetRequiredService<IRepository<ApiClient, Guid>>().FindAsync(a.ApiClient)).ShouldBeNull(who);
                    (await GetRequiredService<IRepository<InboundMessage, Guid>>().FindAsync(a.Message)).ShouldBeNull(who);
                    (await GetRequiredService<IRepository<InboundDocument, Guid>>().FindAsync(a.Document)).ShouldBeNull(who);
                    (await GetRequiredService<IRepository<ImportProfile, Guid>>().FindAsync(a.Profile)).ShouldBeNull(who);
                    (await GetRequiredService<IRepository<OrderIntake, Guid>>().FindAsync(a.Intake)).ShouldBeNull(who);
                    (await GetRequiredService<IRepository<IntakeAlias, Guid>>().FindAsync(a.Alias)).ShouldBeNull(who);

                    /* Physically absent: even without the tenant filter the host database holds none of A's rows. */
                    using (GetRequiredService<IDataFilter>().Disable<IMultiTenant>())
                    {
                        (await GetRequiredService<IRepository<MailboxConnection, Guid>>().CountAsync(c => c.Id == a.Mailbox)).ShouldBe(0, who);
                        (await GetRequiredService<IRepository<InboundMessage, Guid>>().CountAsync(m => m.Id == a.Message)).ShouldBe(0, who);
                        (await GetRequiredService<IRepository<InboundDocument, Guid>>().CountAsync(d => d.Id == a.Document)).ShouldBe(0, who);
                        (await GetRequiredService<IRepository<OrderIntake, Guid>>().CountAsync(i => i.Id == a.Intake)).ShouldBe(0, who);
                        (await GetRequiredService<IRepository<IntakeAlias, Guid>>().CountAsync(i => i.Id == a.Alias)).ShouldBe(0, who);
                    }

                    if (other != null)
                    {
                        /* Through the services, as a user of the shared tenant would ask (the host has no such tenant-side screens). */
                        var mailboxes = GetRequiredService<IMailboxConnectionAppService>();
                        (await mailboxes.GetListAsync(new MailboxConnectionListRequestDto { WildcardSearch = "qq" })).Rows.ShouldBeEmpty(who);
                        await Should.ThrowAsync<EntityNotFoundException>(() => mailboxes.GetAsync(a.Mailbox));

                        var clients = GetRequiredService<IApiClientAppService>();
                        (await clients.GetListAsync(new ApiClientListRequestDto { WildcardSearch = "qq" })).Rows.ShouldBeEmpty(who);
                        await Should.ThrowAsync<EntityNotFoundException>(() => clients.GetAsync(a.ApiClient));
                        await Should.ThrowAsync<EntityNotFoundException>(() => clients.RegenerateSecretAsync(a.ApiClient));
                        await Should.ThrowAsync<EntityNotFoundException>(() => clients.DeactivateAsync(a.ApiClient));

                        var messages = GetRequiredService<IInboundMessageAppService>();
                        (await messages.GetListAsync(new InboundMessageListRequestDto { WildcardSearch = "qq" })).Rows.ShouldBeEmpty(who);
                        await Should.ThrowAsync<EntityNotFoundException>(() => messages.GetAsync(a.Message));
                        await Should.ThrowAsync<EntityNotFoundException>(() => messages.GetDocumentContentAsync(a.Document));

                        var profiles = GetRequiredService<IImportProfileAppService>();
                        (await profiles.GetListAsync(new ImportProfileListRequestDto { WildcardSearch = "qq" })).Rows.ShouldBeEmpty(who);
                        await Should.ThrowAsync<EntityNotFoundException>(() => profiles.GetAsync(a.Profile));

                        var review = GetRequiredService<IOrderIntakeAppService>();
                        (await review.GetListAsync(new OrderIntakeListRequestDto { WildcardSearch = "qq" })).Rows.ShouldBeEmpty(who);
                        await Should.ThrowAsync<EntityNotFoundException>(() => review.GetAsync(a.Intake));
                        (await review.GetAliasesAsync(new IntakeAliasListRequestDto { WildcardSearch = "qq" })).Rows.ShouldBeEmpty(who);
                    }
                }
            }
        });
    }

    [Fact]
    public async Task An_api_clients_application_and_grant_live_in_the_host_with_the_tenant_in_its_properties()
    {
        using var features = IntakeOn();
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var service = GetRequiredService<IApiClientAppService>();
            var created = await service.CreateAsync(new ApiClientCreateDto { Name = "QQ ERP" });
            try
            {
                created.ClientId.ShouldStartWith(OrderIntakeConsts.ClientIdPrefix);
                created.ClientId.Length.ShouldBe(OrderIntakeConsts.ClientIdPrefix.Length + OrderIntakeConsts.ClientIdRandomLength);
                created.ClientSecret.Length.ShouldBe(OrderIntakeConsts.ClientSecretLength);
                JsonSerializer.Serialize(await service.GetAsync(created.Id)).ShouldNotContain(created.ClientSecret, Case.Sensitive, "shown once, never again");

                using (CurrentTenant.Change(null))
                {
                    var applications = GetRequiredService<IOpenIddictApplicationManager>();
                    var application = await applications.FindByClientIdAsync(created.ClientId);
                    application.ShouldNotBeNull("the OpenIddict application is in the host database");
                    var properties = await applications.GetPropertiesAsync(application);
                    properties[OrderIntakeConsts.TenantIdProperty].GetString().ShouldBe(TenantA.ToString());
                    properties[OrderIntakeConsts.ApiClientIdProperty].GetString().ShouldBe(created.Id.ToString());
                    (await applications.GetClientTypeAsync(application)).ShouldBe(OpenIddictConstants.ClientTypes.Confidential);
                    (await applications.HasPermissionAsync(application, OpenIddictConstants.Permissions.GrantTypes.ClientCredentials)).ShouldBeTrue();
                    (await applications.HasPermissionAsync(application, OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode)).ShouldBeFalse();
                    (await applications.ValidateClientSecretAsync(application, created.ClientSecret)).ShouldBeTrue();
                }

                (await GrantAsync(created.ClientId)).ShouldBeTrue("the intake permission is granted to the client, host side");

                await service.DeactivateAsync(created.Id);
                (await GrantAsync(created.ClientId)).ShouldBeFalse("deactivate removes the grant");
                await service.ActivateAsync(created.Id);
                (await GrantAsync(created.ClientId)).ShouldBeTrue();

                var regenerated = await service.RegenerateSecretAsync(created.Id);
                using (CurrentTenant.Change(null))
                {
                    var applications = GetRequiredService<IOpenIddictApplicationManager>();
                    var application = (await applications.FindByClientIdAsync(created.ClientId))!;
                    (await applications.ValidateClientSecretAsync(application, regenerated.ClientSecret)).ShouldBeTrue();
                    (await applications.ValidateClientSecretAsync(application, created.ClientSecret)).ShouldBeFalse("the old secret stops working");
                }
            }
            finally
            {
                await service.DeleteAsync(created.Id);
            }

            using (CurrentTenant.Change(null))
            {
                (await GetRequiredService<IOpenIddictApplicationManager>().FindByClientIdAsync(created.ClientId)).ShouldBeNull("delete removes the application");
            }

            (await GrantAsync(created.ClientId)).ShouldBeFalse();
        });
    }

    private async Task<bool> GrantAsync(string clientId)
    {
        using (CurrentTenant.Change(null))
        {
            var grant = await GetRequiredService<IPermissionGrantRepository>().FindAsync(IntegrationsPermissions.IntakeApi.Submit, ClientPermissionValueProvider.ProviderName, clientId);
            return grant != null;
        }
    }

    [Fact]
    public async Task An_ai_usage_record_lands_in_the_host_database_naming_its_tenant()
    {
        /* As the pipeline calls it: in the tenant, outside any open transaction (design 72 build note 6). */
        var document = Guid.NewGuid();
        using (CurrentTenant.Change(TenantA))
        {
            await GetRequiredService<AiUsageMeter>().RecordAsync(OrderIntakeConsts.LearnAgent, new LanguageModelResponse
            {
                ModelId = "claude-opus-5-5", StopReason = LanguageModelStopReason.EndTurn,
                Usage = new LanguageModelUsage { InputTokens = 12_000, OutputTokens = 5_000 }
            }, "claude-opus-5-5", document);
        }

        await InRolledBackTransactionAsync(null, async () =>
        {
            var record = await GetRequiredService<IRepository<AiUsageRecord, Guid>>().SingleAsync(r => r.InboundDocumentId == document);
            record.TenantId.ShouldBe(TenantA);
            record.Agent.ShouldBe(OrderIntakeConsts.LearnAgent);
            (record.InputTokens, record.OutputTokens).ShouldBe((12_000, 5_000));
        });
    }

    [Fact]
    public async Task The_intake_apis_json_order_is_recorded_and_processed_in_one_request_transaction()
    {
        using var features = IntakeOn();
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var client = new ApiClient(Guid.NewGuid(), TenantA, "QQ ERP", "otms_" + Guid.NewGuid().ToString("N")[..24].ToUpperInvariant(), "abcd", DateTime.UtcNow, null, null, false);
            await GetRequiredService<IRepository<ApiClient, Guid>>().InsertAsync(client, autoSave: true);
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(AbpClaimTypes.UserId, Guid.NewGuid().ToString()), new Claim(OrderIntakeConsts.ApiClientIdProperty, client.Id.ToString()),
                new Claim(AbpClaimTypes.TenantId, TenantA.ToString()), new Claim(AbpClaimTypes.ClientId, client.ClientId)
            ], "Bearer"));

            using (GetRequiredService<ICurrentPrincipalAccessor>().Change(principal))
            {
                var result = await GetRequiredService<IIntakeApiAppService>().SubmitOrderAsync(new IntakeOrderRequestDto
                {
                    ExternalId = "qq-erp-1",
                    Order = new OrderIntakeDto { CustomerReference = "QQ-1", Customer = new OrderIntakeCustomerDto { Name = "Onbekend" } }
                });

                result.Intakes.ShouldHaveSingleItem().Status.ShouldBe(OrderIntakeStatus.NeedsReview);
            }
        });
    }
}
