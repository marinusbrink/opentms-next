using System;
using Microsoft.Extensions.DependencyInjection;
using OpenTms.Financial.EntityFrameworkCore;
using OpenTms.Integrations.EntityFrameworkCore;
using OpenTms.MasterData.EntityFrameworkCore;
using OpenTms.Orders.EntityFrameworkCore;
using OpenTms.PlanningExecution.EntityFrameworkCore;
using OpenTms.Platform.EntityFrameworkCore;
using OpenTms.Reporting.EntityFrameworkCore;
using Volo.Abp.Uow;
using Volo.Abp.AuditLogging.EntityFrameworkCore;
using Volo.Abp.BackgroundJobs.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.PostgreSql;
using Volo.Abp.FeatureManagement.EntityFrameworkCore;
using Volo.Abp.Identity.EntityFrameworkCore;
using Volo.Abp.OpenIddict.EntityFrameworkCore;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement.EntityFrameworkCore;
using Volo.Abp.SettingManagement.EntityFrameworkCore;
using Volo.Abp.BlobStoring.Database.EntityFrameworkCore;
using Volo.Abp.TenantManagement.EntityFrameworkCore;
using Volo.Abp.Studio;

namespace OpenTms.EntityFrameworkCore;

[DependsOn(
    typeof(OpenTmsDomainModule),
    typeof(AbpPermissionManagementEntityFrameworkCoreModule),
    typeof(AbpSettingManagementEntityFrameworkCoreModule),
    typeof(AbpEntityFrameworkCorePostgreSqlModule),
    typeof(AbpBackgroundJobsEntityFrameworkCoreModule),
    typeof(AbpAuditLoggingEntityFrameworkCoreModule),
    typeof(AbpFeatureManagementEntityFrameworkCoreModule),
    typeof(AbpIdentityEntityFrameworkCoreModule),
    typeof(AbpOpenIddictEntityFrameworkCoreModule),
    typeof(AbpTenantManagementEntityFrameworkCoreModule),
    typeof(BlobStoringDatabaseEntityFrameworkCoreModule),
    typeof(PlatformEntityFrameworkCoreModule),
    typeof(OrdersEntityFrameworkCoreModule),
    typeof(PlanningExecutionEntityFrameworkCoreModule),
    typeof(FinancialEntityFrameworkCoreModule),
    typeof(MasterDataEntityFrameworkCoreModule),
    typeof(IntegrationsEntityFrameworkCoreModule),
    typeof(ReportingEntityFrameworkCoreModule)
    )]
public class OpenTmsEntityFrameworkCoreModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        // https://www.npgsql.org/efcore/release-notes/6.0.html#opting-out-of-the-new-timestamp-mapping-logic
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        OpenTmsEfCoreEntityExtensionMappings.Configure();
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddAbpDbContext<OpenTmsDbContext>(options =>
        {
                /* Remove "includeAllEntities: true" to create
                 * default repositories only for aggregate roots */
            options.AddDefaultRepositories(includeAllEntities: true);
        });

        if (AbpStudioAnalyzeHelper.IsInAnalyzeMode)
        {
            return;
        }

        Configure<AbpDbContextOptions>(options =>
        {
            /* The main point to change your DBMS.
             * See also OpenTmsDbContextFactory for EF Core tooling. */

            options.UseNpgsql();

        });
        
    }
}
