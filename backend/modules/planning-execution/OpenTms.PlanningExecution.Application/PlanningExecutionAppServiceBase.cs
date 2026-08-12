using OpenTms.PlanningExecution.Localization;
using Volo.Abp.Application.Services;

namespace OpenTms.PlanningExecution;

/* Inherit the PlanningExecution module's application services from this class. */
public abstract class PlanningExecutionAppServiceBase : ApplicationService
{
    protected PlanningExecutionAppServiceBase()
    {
        LocalizationResource = typeof(PlanningExecutionResource);
    }
}
