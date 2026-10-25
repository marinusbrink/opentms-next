using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Features;
using OpenTms.Platform.Features;
using OpenTms.Platform.ReportDefinitions;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;

namespace OpenTms.EntityFrameworkCore.Platform.DocumentRendering;

/// <summary>
/// Shared plumbing of the design-50 container tests: two tenants (A and B), report definitions created through the
/// production service, the Platform.DocumentRendering feature switch, the business-error assertion.
/// </summary>
public abstract class ReportRenderingTestBase<TModule> : OpenTmsTestBase<TModule>
    where TModule : IAbpModule
{
    protected ReportRenderingTestBase()
    {
        CurrentTenant = GetRequiredService<ICurrentTenant>();
    }

    protected ICurrentTenant CurrentTenant { get; }

    protected Guid TenantA { get; } = Guid.NewGuid();

    protected Guid TenantB { get; } = Guid.NewGuid();

    protected IReportDefinitionAppService Definitions => GetRequiredService<IReportDefinitionAppService>();

    protected IDisposable InTenantA() => CurrentTenant.Change(TenantA);

    protected IDisposable InTenantB() => CurrentTenant.Change(TenantB);

    protected static IDisposable RenderingOn() => TestFeatureValueProvider.Override(PlatformFeatures.DocumentRendering, "true");

    protected static IDisposable RenderingOff() => TestFeatureValueProvider.Override(PlatformFeatures.DocumentRendering, "false");

    /// <summary>A definition in the current tenant, created through the production service.</summary>
    protected Task<ReportDefinitionDto> CreateDefinitionAsync(
        string name = "Factuur NL",
        ReportPurpose purpose = ReportPurpose.Invoice,
        string culture = "nl",
        string templateId = "tpl-1",
        string pattern = "factuur-{{ number }}.pdf",
        bool isDefault = false)
        => Definitions.CreateAsync(new ReportDefinitionCreateDto
        {
            Name = name, Purpose = purpose, Culture = culture, IsDefault = isDefault, Provider = ReportRenderingProvider.PdfGeneratorApi,
            ProviderTemplateId = templateId, FileNamePattern = pattern
        });

    protected static async Task<BusinessException> ShouldFailAsync(Func<Task> action, string code)
    {
        var ex = await Should.ThrowAsync<BusinessException>(action);
        ex.Code.ShouldBe(code, ex.Message);
        return ex;
    }

    protected static async Task ShouldBeFeatureRefusalAsync(Func<Task> action, string feature)
    {
        var ex = await Should.ThrowAsync<Volo.Abp.Authorization.AbpAuthorizationException>(action);
        ex.Code.ShouldNotBeNull();
        ex.Code.ShouldStartWith("Volo.Feature:");
        ex.Data.Values.Cast<object?>().ShouldContain(feature);
    }
}
