using OpenTms.Reporting.Localization;
using Volo.Abp.Application.Services;

namespace OpenTms.Reporting;

/* Inherit the Reporting module's application services from this class. */
public abstract class ReportingAppServiceBase : ApplicationService
{
    protected ReportingAppServiceBase()
    {
        LocalizationResource = typeof(ReportingResource);
    }
}
