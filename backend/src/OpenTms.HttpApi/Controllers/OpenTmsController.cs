using OpenTms.Localization;
using Volo.Abp.AspNetCore.Mvc;

namespace OpenTms.Controllers;

/* Inherit your controllers from this class.
 */
public abstract class OpenTmsController : AbpControllerBase
{
    protected OpenTmsController()
    {
        LocalizationResource = typeof(OpenTmsResource);
    }
}
