using OpenTms.Financial.Permissions;
using OpenTms.Financial.Tariffs.Calculators;
using OpenTms.Platform.DerivedValues;
using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace OpenTms.Financial;

[DependsOn(
    typeof(FinancialDomainModule),
    typeof(FinancialApplicationContractsModule),
    typeof(AbpDddApplicationModule)
)]
public class FinancialApplicationModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        /* Design 57 (flow 2, design 49): the two stored-value calculators of Financial; screens watch them with the rate-lines permission. */
        Configure<DerivedValueOptions>(options =>
        {
            options.AddCalculator<OrderRateLineCalculator>(OrderRateLineCalculator.TargetType, FinancialPermissions.RateLines.Default);
            options.AddCalculator<TripRateLineCalculator>(TripRateLineCalculator.TargetType, FinancialPermissions.RateLines.Default);
        });
    }
}
