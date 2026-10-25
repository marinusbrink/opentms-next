using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.Permissions;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.DerivedValues;
using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace OpenTms.PlanningExecution;

[DependsOn(
    typeof(PlanningExecutionDomainModule),
    typeof(PlanningExecutionApplicationContractsModule),
    typeof(AbpDddApplicationModule)
)]
public class PlanningExecutionApplicationModule : AbpModule
{
    /// <summary>Design 49 target types of this module (design 59 "Calculators").</summary>
    public const string LegTargetType = "PlanningExecution.Leg";

    public const string TripTargetType = "PlanningExecution.TransportTrip";

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<DerivedValueOptions>(options =>
        {
            options.AddCalculator<LegRouteCalculator>(LegTargetType, PlanningExecutionPermissions.Legs.Default);
            options.AddCalculator<TripCalculator>(TripTargetType, PlanningExecutionPermissions.TransportTrips.Default);
        });
    }
}
