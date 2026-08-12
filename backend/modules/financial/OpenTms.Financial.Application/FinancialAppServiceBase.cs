using OpenTms.Financial.Localization;
using Volo.Abp.Application.Services;

namespace OpenTms.Financial;

/* Inherit the Financial module's application services from this class. */
public abstract class FinancialAppServiceBase : ApplicationService
{
    protected FinancialAppServiceBase()
    {
        LocalizationResource = typeof(FinancialResource);
    }
}
