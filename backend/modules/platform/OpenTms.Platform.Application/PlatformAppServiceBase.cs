using OpenTms.Platform.Localization;
using Volo.Abp.Application.Services;

namespace OpenTms.Platform;

/* Inherit the Platform module's application services from this class. */
public abstract class PlatformAppServiceBase : ApplicationService
{
    protected PlatformAppServiceBase()
    {
        LocalizationResource = typeof(PlatformResource);
    }
}
