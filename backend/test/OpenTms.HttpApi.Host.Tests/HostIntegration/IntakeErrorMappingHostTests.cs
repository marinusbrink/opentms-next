using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp.AspNetCore.ExceptionHandling;
using Xunit;
using IntegrationsErrorCodes = OpenTms.Integrations.IntegrationsErrorCodes;

namespace OpenTms.HostIntegration;

/// <summary>
/// The HTTP status of design 72's configuration gap as the real host maps it (review minor 6: a missing App:SelfUrl or an
/// unconfigured provider is ProviderNotConfigured, answered 409, never a 500); ApiClientInactive stays unmapped and travels
/// as ABP's default 403 for a business exception (asserted over HTTP in ApiClientTokenRoundTripTests). Risk class: Medium.
/// </summary>
[Collection(HostIntegrationCollection.Name)]
public class IntakeErrorMappingHostTests(HostIntegrationFixture host)
{
    [Fact]
    public void Provider_not_configured_travels_as_409_and_an_inactive_api_client_is_not_remapped()
    {
        var mappings = host.Services.GetRequiredService<IOptions<AbpExceptionHttpStatusCodeOptions>>().Value.ErrorCodeToHttpStatusCodeMappings;

        mappings[IntegrationsErrorCodes.ProviderNotConfigured].ShouldBe(HttpStatusCode.Conflict);
        mappings.ShouldNotContainKey(IntegrationsErrorCodes.ApiClientInactive);
    }
}
