using OpenTms.MasterData.Localization;
using Volo.Abp.Application.Services;

namespace OpenTms.MasterData;

/* Inherit the MasterData module's application services from this class. */
public abstract class MasterDataAppServiceBase : ApplicationService
{
    protected MasterDataAppServiceBase()
    {
        LocalizationResource = typeof(MasterDataResource);
    }
}
