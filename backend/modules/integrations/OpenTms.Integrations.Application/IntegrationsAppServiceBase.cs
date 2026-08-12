using OpenTms.Integrations.Localization;
using Volo.Abp.Application.Services;

namespace OpenTms.Integrations;

/* Inherit the Integrations module's application services from this class. */
public abstract class IntegrationsAppServiceBase : ApplicationService
{
    protected IntegrationsAppServiceBase()
    {
        LocalizationResource = typeof(IntegrationsResource);
    }
}
