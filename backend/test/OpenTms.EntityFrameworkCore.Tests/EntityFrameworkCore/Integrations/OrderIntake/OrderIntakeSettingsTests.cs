using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using OpenTms.EntityFrameworkCore.Orders;
using OpenTms.Features;
using OpenTms.Integrations.Features;
using OpenTms.Integrations.OrderIntake;
using OpenTms.Integrations.Settings;
using OpenTms.Orders.Intake;
using OpenTms.Platform.Features;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Security.Claims;
using Volo.Abp.SettingManagement;
using Volo.Abp.Validation;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Integrations.Intake;

/// <summary>
/// The order intake's tenant settings (design 72 build notes 25 and 28): <c>GET</c>/<c>PUT
/// /api/integrations/order-intake-settings</c> reads the default 0.75 and stores a value in 0.50–0.95 with two decimals,
/// refuses one outside the range, refuses the host; the service sits behind <c>Integrations.OrderIntake</c> (off by
/// default) and the app features of its nav node; the setting is per tenant (tenant B never sees A's value); and the
/// pipeline hands the tenant's value to Orders with every intake (kept on the intake), with the trace step
/// <c>SubmittedWithMasterData</c> when Orders matched by a similar name or created master data. The permission attribute is
/// checked by reflection in <c>OrderIntakeAuthorizationTests</c> (this suite grants every policy to a signed-in user).
/// Risk class: High (it steers what is matched and created), Critical for tenant isolation.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class OrderIntakeSettingsTests : TransportOrderTestBase<OpenTmsEntityFrameworkCoreTestModule>
{
    private IOrderIntakeSettingsAppService Settings => GetRequiredService<IOrderIntakeSettingsAppService>();

    private static IDisposable IntakeOn()
    {
        var all = new[]
        {
            TestFeatureValueProvider.Override(IntegrationsFeatures.OrderIntake, "true"),
            TestFeatureValueProvider.Override(IntegrationsFeatures.OrderIntakeAiMapping, "false"),
            TestFeatureValueProvider.Override(AppFeatures.Integrations, "true"),
            TestFeatureValueProvider.Override(IntakeConsts.OrderIntakeFeature, "true")
        };
        return new All(all);
    }

    private sealed class All(IDisposable[] all) : IDisposable
    {
        public void Dispose()
        {
            foreach (var d in all.Reverse())
            {
                d.Dispose();
            }
        }
    }

    [Fact]
    public async Task The_default_is_read_and_a_value_in_range_is_stored_with_two_decimals()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();

        (await Settings.GetAsync()).NameSimilarityThreshold.ShouldBe(0.75m);

        (await Settings.UpdateAsync(new OrderIntakeSettingsDto { NameSimilarityThreshold = 0.5m })).NameSimilarityThreshold.ShouldBe(0.5m);
        (await Settings.GetAsync()).NameSimilarityThreshold.ShouldBe(0.5m);
        (await Settings.UpdateAsync(new OrderIntakeSettingsDto { NameSimilarityThreshold = 0.95m })).NameSimilarityThreshold.ShouldBe(0.95m);
        (await Settings.UpdateAsync(new OrderIntakeSettingsDto { NameSimilarityThreshold = 0.825m })).NameSimilarityThreshold.ShouldBe(0.83m, "two decimals, away from zero");
        (await Settings.GetAsync()).NameSimilarityThreshold.ShouldBe(0.83m);
        (await GetRequiredService<ISettingManager>().GetOrNullForTenantAsync(IntegrationsSettings.NameSimilarityThreshold, TenantA)).ShouldBe("0.83");
    }

    [Theory]
    [InlineData("0.49")]
    [InlineData("0.96")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("-0.75")]
    public async Task A_value_outside_the_range_is_refused_and_nothing_is_stored(string value)
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        await Settings.UpdateAsync(new OrderIntakeSettingsDto { NameSimilarityThreshold = 0.8m });

        var refused = await Should.ThrowAsync<Exception>(() => Settings.UpdateAsync(new OrderIntakeSettingsDto { NameSimilarityThreshold = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture) }));
        (refused is AbpValidationException || refused is BusinessException).ShouldBeTrue(refused.GetType().Name);
        (await Settings.GetAsync()).NameSimilarityThreshold.ShouldBe(0.8m);
    }

    [Fact]
    public async Task The_host_has_no_order_intake_setting_of_its_own()
    {
        using var features = IntakeOn();
        using var host = CurrentTenant.Change(null);

        await Should.ThrowAsync<BusinessException>(() => Settings.UpdateAsync(new OrderIntakeSettingsDto { NameSimilarityThreshold = 0.8m }));
    }

    [Fact]
    public async Task A_value_stored_outside_the_range_is_read_clamped()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        await GetRequiredService<ISettingManager>().SetForTenantAsync(TenantA, IntegrationsSettings.NameSimilarityThreshold, "0.2");
        (await Settings.GetAsync()).NameSimilarityThreshold.ShouldBe(0.5m);
        await GetRequiredService<ISettingManager>().SetForTenantAsync(TenantA, IntegrationsSettings.NameSimilarityThreshold, "garbage");
        (await Settings.GetAsync()).NameSimilarityThreshold.ShouldBe(0.75m);
    }

    [Fact]
    public async Task The_service_sits_behind_the_order_intake_feature_and_its_nav_node()
    {
        using var tenant = InTenantA();
        using (IntakeOn())
        using (TestFeatureValueProvider.Override(IntegrationsFeatures.OrderIntake, "false"))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => Settings.GetAsync());
            await Should.ThrowAsync<AbpAuthorizationException>(() => Settings.UpdateAsync(new OrderIntakeSettingsDto { NameSimilarityThreshold = 0.8m }));
        }

        using (IntakeOn())
        using (TestFeatureValueProvider.Override(IntegrationsFeatures.OrderIntake, null))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => Settings.GetAsync()); /* Integrations.OrderIntake defaults off (constitution rule 4) */
        }

        foreach (var node in new[] { AppFeatures.Integrations, AppFeatures.IntegrationsApp.Intake.Default, AppFeatures.IntegrationsApp.Intake.Settings })
        {
            using (IntakeOn())
            using (TestFeatureValueProvider.Override(node, "false"))
            {
                await Should.ThrowAsync<AbpAuthorizationException>(() => Settings.GetAsync(), node);
            }
        }

        using (IntakeOn())
        {
            (await Settings.GetAsync()).NameSimilarityThreshold.ShouldBe(0.75m);
        }
    }

    [Fact]
    public async Task An_anonymous_caller_is_refused()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        using var anonymous = GetRequiredService<ICurrentPrincipalAccessor>().Change(new ClaimsPrincipal(new ClaimsIdentity()));

        await Should.ThrowAsync<AbpAuthorizationException>(() => Settings.GetAsync());
    }

    [Fact]
    public async Task The_setting_is_per_tenant()
    {
        using var features = IntakeOn();
        using (InTenantA())
        {
            await Settings.UpdateAsync(new OrderIntakeSettingsDto { NameSimilarityThreshold = 0.9m });
        }

        using (InTenantB())
        {
            (await Settings.GetAsync()).NameSimilarityThreshold.ShouldBe(0.75m, "A's value is not B's");
            await Settings.UpdateAsync(new OrderIntakeSettingsDto { NameSimilarityThreshold = 0.6m });
        }

        using (InTenantA())
        {
            (await Settings.GetAsync()).NameSimilarityThreshold.ShouldBe(0.9m, "B's write did not touch A");
        }
    }

    /* ---------- The pipeline hands the tenant's value to Orders ---------- */

    private IDisposable AsApiClient(Guid apiClientId, string clientId)
    {
        var claims = new List<Claim>
        {
            new(AbpClaimTypes.UserId, Guid.NewGuid().ToString()),
            new(OrderIntakeConsts.ApiClientIdProperty, apiClientId.ToString()),
            new(AbpClaimTypes.TenantId, CurrentTenant.Id!.Value.ToString()),
            new(AbpClaimTypes.ClientId, clientId)
        };
        return GetRequiredService<ICurrentPrincipalAccessor>().Change(new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")));
    }

    private async Task<IntakeResultDto> SubmitThroughTheApiAsync(OpenTms.TransportRun.TransportRunWorld f, string externalId, bool autoConfirm)
    {
        var clientId = "otms_" + Guid.NewGuid().ToString("N")[..24].ToUpperInvariant();
        var client = new ApiClient(Guid.NewGuid(), CurrentTenant.Id, "ACME ERP", clientId, "abcd", DateTime.UtcNow, null, null, autoConfirm: autoConfirm);
        await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<ApiClient, Guid>>().InsertAsync(client, autoSave: true));
        using var principal = AsApiClient(client.Id, clientId);
        var date = Today.AddDays(1).ToString("yyyy-MM-dd");
        return await GetRequiredService<IIntakeApiAppService>().SubmitOrderAsync(new IntakeOrderRequestDto
        {
            ExternalId = externalId,
            Order = new OrderIntakeDto
            {
                CustomerReference = "ERP-" + f.Suffix,
                Customer = new OrderIntakeCustomerDto { Name = "Klant Fooods" },
                Lines =
                [
                    new OrderIntakeLineDto
                    {
                        TransportUnitName = "Gitterbox", Quantity = 1,
                        Stops =
                        [
                            new OrderIntakeStopDto { Role = IntakeStopRole.Pickup, Date = date, Address = new OrderIntakeAddressDto { Name = "DC Venlo", PostalCode = "5928 PR", HouseNumber = "12", City = "Venlo" } },
                            new OrderIntakeStopDto { Role = IntakeStopRole.Delivery, Date = date, Address = new OrderIntakeAddressDto { Name = "Supermarkt Utrecht", PostalCode = "3511BD", HouseNumber = "40", City = "Utrecht" } }
                        ]
                    }
                ]
            }
        });
    }

    [Fact]
    public async Task The_pipeline_hands_the_tenants_threshold_to_Orders_and_the_trace_tells_what_was_matched_and_created()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();
        await Settings.UpdateAsync(new OrderIntakeSettingsDto { NameSimilarityThreshold = 0.85m });

        /* "Klant Fooods" ~ "Klant Foods B.V." (one edit: 0.92) matches; "Gitterbox" is created. The API's JSON may confirm. */
        var submitted = await SubmitThroughTheApiAsync(f, "erp-" + f.Suffix, autoConfirm: true);

        var outcome = submitted.Intakes.ShouldHaveSingleItem();
        outcome.Status.ShouldBe(OrderIntakeStatus.OrderCreated);
        var intake = await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<OrderIntake, Guid>>().GetAsync(outcome.IntakeId!.Value));
        intake.NameSimilarityThreshold.ShouldBe(0.85m, "the tenant's setting, kept on the intake");
        intake.ProfileTrusted.ShouldBeTrue("build note 31: the API's JSON may confirm");

        var document = (await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<InboundDocument, Guid>>()
            .GetListAsync(d => d.InboundMessageId == submitted.InboundMessageId, includeDetails: true))).Single();
        var step = document.Steps.Single(s => s.Step == IntakeStep.Submitted);
        step.MessageKey.ShouldBe(IntakeStepKeys.SubmittedWithMasterData);
        step.Parameters.ShouldContainKeyAndValue("fuzzyMatches", "1");
        step.Parameters.ShouldContainKeyAndValue("created", "1");
    }

    [Fact]
    public async Task A_stricter_tenant_threshold_creates_instead_of_matching()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();
        await Settings.UpdateAsync(new OrderIntakeSettingsDto { NameSimilarityThreshold = 0.95m });

        var submitted = await SubmitThroughTheApiAsync(f, "erp-strict-" + f.Suffix, autoConfirm: false);

        var outcome = submitted.Intakes.ShouldHaveSingleItem();
        outcome.Status.ShouldBe(OrderIntakeStatus.OrderCreated, "without auto-confirm a Draft, not a review");
        var customer = (await GetRequiredService<IOrderIntakeAppService>().GetAsync(outcome.IntakeId!.Value)).Resolution.Single(r => r.FieldPath == IntakeFieldPaths.Customer);
        customer.Method.ShouldBe(IntakeResolutionMethod.Created, "0.92 < 0.95");
        customer.TargetId.ShouldNotBe(f.CustomerId);
    }
}
