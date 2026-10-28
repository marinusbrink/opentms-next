using Localization.Resources.AbpUi;
using System.Net;
using OpenTms.Financial;
using OpenTms.Localization;
using OpenTms.MasterData;
using OpenTms.Orders;
using OpenTms.Platform;
using OpenTms.Platform.DerivedValues;
using OpenTms.Platform.ReferenceData;
using OpenTms.SignIn;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.ExceptionHandling;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Account;
using Volo.Abp.SettingManagement;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement.HttpApi;
using Volo.Abp.Localization;
using Volo.Abp.TenantManagement;

namespace OpenTms;

 [DependsOn(
    typeof(OpenTmsApplicationContractsModule),
    typeof(AbpPermissionManagementHttpApiModule),
    typeof(AbpSettingManagementHttpApiModule),
    typeof(AbpAccountHttpApiModule),
    typeof(AbpIdentityHttpApiModule),
    typeof(AbpTenantManagementHttpApiModule),
    typeof(AbpFeatureManagementHttpApiModule)
    )]
public class OpenTmsHttpApiModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        ConfigureLocalization();
        ConfigureExceptionStatusCodes();
        RemoveBypassedAbpControllers();
        Configure<MvcOptions>(options => options.Conventions.Add(new RemoveSelfRegistrationEndpointConvention()));
    }

    /* ABP's own users, roles, user-lookup and tenant endpoints stay out of the API (docs/designs/tenants-and-editions.md,
     * "ABP endpoints that are not served"). Administration and Tenants replace them, and only those check the app
     * features, refuse what the product does not allow and run tenant provisioning; ABP's tenant endpoints would also
     * create a tenant without its database and hand out its connection string, password included. The application
     * services behind them stay registered: our own controllers use them. */
    private void RemoveBypassedAbpControllers()
    {
        Configure<AbpAspNetCoreMvcOptions>(options =>
        {
            options.ControllersToRemove.Add(typeof(IdentityUserController));
            options.ControllersToRemove.Add(typeof(IdentityRoleController));
            options.ControllersToRemove.Add(typeof(IdentityUserLookupController));
            options.ControllersToRemove.Add(typeof(TenantController));
        });
    }

    /* ABP answers a BusinessException with 403 by default, which the UI would read as a permission
     * problem. The Master Data conflict codes (duplicate key, in use, default) travel as 409 and the
     * request-shape codes as 400 (docs/designs/41-master-data-first-run.md, API contract). */
    private void ConfigureExceptionStatusCodes()
    {
        Configure<AbpExceptionHttpStatusCodeOptions>(options =>
        {
            foreach (var code in MasterDataErrorCodes.ConflictCodes)
            {
                options.Map(code, HttpStatusCode.Conflict);
            }

            foreach (var code in MasterDataErrorCodes.BadRequestCodes)
            {
                options.Map(code, HttpStatusCode.BadRequest);
            }

            /* The Platform master-data lists follow the same split (docs/designs/54-platform-master-data.md). */
            foreach (var code in PlatformErrorCodes.ConflictCodes)
            {
                options.Map(code, HttpStatusCode.Conflict);
            }

            /* The reference-data base of Orders, Financial and Planning & Execution (design 50); their own codes below. */
            foreach (var code in ReferenceDataErrorCodes.ConflictCodes)
            {
                options.Map(code, HttpStatusCode.Conflict);
            }

            foreach (var code in PlatformErrorCodes.BadRequestCodes)
            {
                options.Map(code, HttpStatusCode.BadRequest);
            }

            foreach (var code in ReferenceDataErrorCodes.BadRequestCodes)
            {
                options.Map(code, HttpStatusCode.BadRequest);
            }

            /* Document rendering (docs/designs/55-report-rendering.md): each code carries its own status. */
            foreach (var (code, status) in PlatformErrorCodes.DocumentRenderingCodes)
            {
                options.Map(code, status);
            }

            /* Design 49. */
            foreach (var code in DerivedValueErrorCodes.BadRequestCodes)
            {
                options.Map(code, HttpStatusCode.BadRequest);
            }

            /* Orders' own codes (design 51). */
            foreach (var code in OrdersErrorCodes.ConflictCodes)
            {
                options.Map(code, HttpStatusCode.Conflict);
            }

            foreach (var code in OrdersErrorCodes.BadRequestCodes)
            {
                options.Map(code, HttpStatusCode.BadRequest);
            }

            /* Financial master data (design 52): validity rows, currencies, days of the week. */
            foreach (var code in FinancialErrorCodes.ConflictCodes)
            {
                options.Map(code, HttpStatusCode.Conflict);
            }

            foreach (var code in FinancialErrorCodes.BadRequestCodes)
            {
                options.Map(code, HttpStatusCode.BadRequest);
            }
        });
    }

    private void ConfigureLocalization()
    {
        Configure<AbpLocalizationOptions>(options =>
        {
            options.Resources
                .Get<OpenTmsResource>()
                .AddBaseTypes(
                    typeof(AbpUiResource)
                );
        });
    }
}
