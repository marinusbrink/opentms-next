using OpenTms.Localization;
using Volo.Abp.Application.Services;

namespace OpenTms;

/* Inherit your application services from this class.
 */
public abstract class OpenTmsAppService : ApplicationService
{
    protected OpenTmsAppService()
    {
        LocalizationResource = typeof(OpenTmsResource);
    }
}
