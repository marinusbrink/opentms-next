using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Extensions.DependencyInjection;
using OpenIddict.Validation.AspNetCore;
using OpenTms.DataProtection;
using OpenTms.Impersonation;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Server.AspNetCore;
using Hangfire;
using Hangfire.PostgreSql;
using OpenTms.EntityFrameworkCore;
using OpenTms.Financial;
using OpenTms.Integrations;
using OpenTms.MasterData;
using OpenTms.MultiTenancy;
using OpenTms.HealthChecks;
using OpenTms.Orders;
using OpenTms.PlanningExecution;
using OpenTms.Platform;
using OpenTms.Platform.Events.Outbox;
using OpenTms.Platform.Maintenance.Instances;
using OpenTms.Reporting;
using Volo.Abp.BackgroundJobs.Hangfire;
using Microsoft.OpenApi;
using Volo.Abp;
using Volo.Abp.Studio;
using Volo.Abp.Account;
using Volo.Abp.Account.Web;
using Volo.Abp.AspNetCore.MultiTenancy;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc.Libs;
using Volo.Abp.Autofac;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Volo.Abp.UI.Navigation.Urls;
using Volo.Abp.VirtualFileSystem;
using Volo.Abp.AspNetCore.Mvc.UI.Bundling;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.Shared;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite.Bundling;
using Microsoft.AspNetCore.Hosting;
using Volo.Abp.AspNetCore.Serilog;
using Volo.Abp.Identity;
using Volo.Abp.OpenIddict;
using Volo.Abp.Swashbuckle;
using Volo.Abp.Studio.Client.AspNetCore;
using Volo.Abp.Security.Claims;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundWorkers;

namespace OpenTms;

[DependsOn(
    typeof(OpenTmsHttpApiModule),
    typeof(AbpStudioClientAspNetCoreModule),
    typeof(AbpAspNetCoreMvcUiLeptonXLiteThemeModule),
    typeof(AbpAutofacModule),
    typeof(AbpAspNetCoreMultiTenancyModule),
    typeof(OpenTmsApplicationModule),
    typeof(OpenTmsEntityFrameworkCoreModule),
    typeof(AbpAccountWebOpenIddictModule),
    typeof(AbpSwashbuckleModule),
    typeof(AbpAspNetCoreSerilogModule),
    typeof(AbpBackgroundJobsHangfireModule)
    )]
public class OpenTmsHttpApiHostModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        var hostingEnvironment = context.Services.GetHostingEnvironment();
        var configuration = context.Services.GetConfiguration();

        PreConfigure<OpenIddictBuilder>(builder =>
        {
            builder.AddValidation(options =>
            {
                options.AddAudiences("OpenTms");
                options.UseLocalServer();
                options.UseAspNetCore();
            });
        });

        if (!hostingEnvironment.IsDevelopment())
        {
            PreConfigure<AbpOpenIddictAspNetCoreOptions>(options =>
            {
                options.AddDevelopmentEncryptionAndSigningCertificate = false;
            });

            PreConfigure<OpenIddictServerBuilder>(serverBuilder =>
            {
                serverBuilder.AddProductionEncryptionAndSigningCertificate("openiddict.pfx", configuration["AuthServer:CertificatePassPhrase"]!);
                serverBuilder.SetIssuer(new Uri(configuration["AuthServer:Authority"]!));
            });
        }
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();
        var hostingEnvironment = context.Services.GetHostingEnvironment();

        ConfigureImpersonation(context);

        if (!configuration.GetValue<bool>("App:DisablePII"))
        {
            Microsoft.IdentityModel.Logging.IdentityModelEventSource.ShowPII = true;
            Microsoft.IdentityModel.Logging.IdentityModelEventSource.LogCompleteSecurityArtifact = true;
        }

        if (!configuration.GetValue<bool>("AuthServer:RequireHttpsMetadata"))
        {
            Configure<OpenIddictServerAspNetCoreOptions>(options =>
            {
                options.DisableTransportSecurityRequirement = true;
            });
            
            Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            });
        }

        if (hostingEnvironment.IsDevelopment())
        {
            context.Services.AddRazorPages()
                .AddRazorRuntimeCompilation();
        }

        /* wwwroot/libs is restored by `abp install-libs` (untracked by ABP convention).
         * Without this, a missing libs folder turns EVERY request into a 500 — including
         * /health and /swagger on environments that never serve the account pages (CI). */
        Configure<AbpMvcLibsOptions>(options =>
        {
            options.CheckLibs = false;
        });

        ConfigureStudio(hostingEnvironment);
        ConfigureHangfire(context, configuration);
        ConfigureAuthentication(context);
        ConfigureDataProtection(context);
        ConfigureUrls(configuration);
        ConfigureBundles(hostingEnvironment);
        ConfigureConventionalControllers();
        ConfigureHealthChecks(context);
        ConfigureSwagger(context, configuration);
        ConfigureVirtualFileSystem(context);
        ConfigureCors(context, configuration);
    }

    /* "Log in as" (docs/designs/tenants-and-editions.md): ABP's impersonator claims must survive both token
     * issuing (the claims handler) and Identity's periodic refresh of the auth cookie, which rebuilds the principal
     * from the user and would otherwise drop them. */
    private void ConfigureImpersonation(ServiceConfigurationContext context)
    {
        Configure<AbpOpenIddictClaimsPrincipalOptions>(options =>
        {
            options.ClaimsPrincipalHandlers.Add<ImpersonationClaimsPrincipalHandler>();
        });

        context.Services.Configure<SecurityStampValidatorOptions>(options =>
        {
            var previous = options.OnRefreshingPrincipal;
            options.OnRefreshingPrincipal = async refreshing =>
            {
                if (previous != null)
                {
                    await previous(refreshing);
                }

                var identity = refreshing.NewPrincipal?.Identities.FirstOrDefault();
                if (identity != null)
                {
                    foreach (var claim in ImpersonationClaims.From(refreshing.CurrentPrincipal))
                    {
                        identity.AddClaim(new System.Security.Claims.Claim(claim.Type, claim.Value));
                    }
                }
            };
        });
    }

    private void ConfigureHangfire(ServiceConfigurationContext context, IConfiguration configuration)
    {
        /* Hangfire backs IBackgroundJobManager (e.g. the Platform tenant-provisioning job).
         * Storage lives in the host database, "hangfire" schema. Hangfire manages that schema
         * itself; it is not part of the EF migrations owned by OpenTms.DbMigrator. */
        context.Services.AddHangfire(config =>
        {
            config.UsePostgreSqlStorage(options =>
            {
                options.UseNpgsqlConnection(configuration.GetConnectionString("Default"));
            });
        });
    }

    private void ConfigureStudio(IHostEnvironment hostingEnvironment)
    {
        if (hostingEnvironment.IsProduction())
        {
            Configure<AbpStudioClientOptions>(options =>
            {
                options.IsLinkEnabled = false;
            });
        }
    }

    private void ConfigureAuthentication(ServiceConfigurationContext context)
    {
        context.Services.ForwardIdentityAuthenticationForBearer(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        context.Services.Configure<AbpClaimsPrincipalFactoryOptions>(options =>
        {
            options.IsDynamicClaimsEnabled = true;
        });
    }

    private void ConfigureUrls(IConfiguration configuration)
    {
        Configure<AppUrlOptions>(options =>
        {
            options.Applications["MVC"].RootUrl = configuration["App:SelfUrl"];
            options.RedirectAllowedUrls.AddRange(configuration["App:RedirectAllowedUrls"]?.Split(',') ?? Array.Empty<string>());
        });
    }

    private void ConfigureBundles(IHostEnvironment hostingEnvironment)
    {
        Configure<AbpBundlingOptions>(options =>
        {
            options.StyleBundles.Configure(
                LeptonXLiteThemeBundles.Styles.Global,
                bundle =>
                {
                    bundle.AddFiles("/global-styles.css");
                }
            );

            options.ScriptBundles.Configure(
                LeptonXLiteThemeBundles.Scripts.Global,
                bundle =>
                {
                    bundle.AddFiles("/global-scripts.js");
                    if (hostingEnvironment.IsDevelopment())
                    {
                        bundle.AddFiles("/dev-login-helper.js");
                    }
                }
            );
        });
    }


    private void ConfigureVirtualFileSystem(ServiceConfigurationContext context)
    {
        var hostingEnvironment = context.Services.GetHostingEnvironment();

        if (hostingEnvironment.IsDevelopment())
        {
            Configure<AbpVirtualFileSystemOptions>(options =>
            {
                options.FileSets.ReplaceEmbeddedByPhysical<OpenTmsDomainSharedModule>(Path.Combine(hostingEnvironment.ContentRootPath, $"..{Path.DirectorySeparatorChar}OpenTms.Domain.Shared"));
                options.FileSets.ReplaceEmbeddedByPhysical<OpenTmsDomainModule>(Path.Combine(hostingEnvironment.ContentRootPath, $"..{Path.DirectorySeparatorChar}OpenTms.Domain"));
                options.FileSets.ReplaceEmbeddedByPhysical<OpenTmsApplicationContractsModule>(Path.Combine(hostingEnvironment.ContentRootPath, $"..{Path.DirectorySeparatorChar}OpenTms.Application.Contracts"));
                options.FileSets.ReplaceEmbeddedByPhysical<OpenTmsApplicationModule>(Path.Combine(hostingEnvironment.ContentRootPath, $"..{Path.DirectorySeparatorChar}OpenTms.Application"));
            });
        }
    }

    /* One key ring for every instance, in the host database (docs/designs/tenants-and-editions.md, "Shared
     * data-protection keys"): a cookie or "Log in as" ticket protected by one instance must be readable by the next. */
    private static void ConfigureDataProtection(ServiceConfigurationContext context)
    {
        HostDatabaseXmlRepository.AddTo(context.Services);
    }

    private void ConfigureConventionalControllers()
    {
        Configure<AbpAspNetCoreMvcOptions>(options =>
        {
            options.ConventionalControllers.Create(typeof(OpenTmsApplicationModule).Assembly);

            /* One route root per module so API paths mirror the domain map:
             * /api/orders/..., /api/planning/..., /api/financial/..., etc. */
            options.ConventionalControllers.Create(typeof(PlatformApplicationModule).Assembly, opts =>
            {
                opts.RootPath = "platform";
                /* Design 50's route for OpenTemplateEditorAsync is ".../{id}/template-editor": "Open" is not an HTTP
                 * verb prefix, so the convention alone would make it "open-template-editor". */
                opts.UrlActionNameNormalizer = c =>
                    c.ControllerName == "ReportDefinition" && c.ActionNameInUrl == "OpenTemplateEditor" ? "TemplateEditor" : c.ActionNameInUrl;
            });
            options.ConventionalControllers.Create(typeof(OrdersApplicationModule).Assembly,
                opts => opts.RootPath = "orders");
            options.ConventionalControllers.Create(typeof(PlanningExecutionApplicationModule).Assembly,
                opts => opts.RootPath = "planning");
            options.ConventionalControllers.Create(typeof(FinancialApplicationModule).Assembly,
                opts => opts.RootPath = "financial");
            options.ConventionalControllers.Create(typeof(MasterDataApplicationModule).Assembly,
                opts => opts.RootPath = "master-data");
            options.ConventionalControllers.Create(typeof(IntegrationsApplicationModule).Assembly,
                opts => opts.RootPath = "integrations");
            options.ConventionalControllers.Create(typeof(ReportingApplicationModule).Assembly,
                opts => opts.RootPath = "reporting");
        });
    }

    private static void ConfigureSwagger(ServiceConfigurationContext context, IConfiguration configuration)
    {
        context.Services.AddAbpSwaggerGenWithOidc(
            configuration["AuthServer:Authority"]!,
            ["OpenTms"],
            [AbpSwaggerOidcFlows.AuthorizationCode],
            null,
            options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo { Title = "OpenTms API", Version = "v1" });
                options.DocInclusionPredicate((docName, description) => true);
                options.CustomSchemaIds(type => type.FullName);
            });
    }

    private void ConfigureCors(ServiceConfigurationContext context, IConfiguration configuration)
    {
        context.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(builder =>
            {
                builder
                    .WithOrigins(
                        configuration["App:CorsOrigins"]?
                            .Split(",", StringSplitOptions.RemoveEmptyEntries)
                            .Select(o => o.Trim().RemovePostFix("/"))
                            .ToArray() ?? Array.Empty<string>()
                    )
                    .WithAbpExposedHeaders()
                    /* A downloaded file (a test document, design 50) keeps the server's file name across origins. */
                    .WithExposedHeaders("Content-Disposition")
                    .SetIsOriginAllowedToAllowWildcardSubdomains()
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });
    }

    private void ConfigureHealthChecks(ServiceConfigurationContext context)
    {
        context.Services.AddOpenTmsHealthChecks();
    }


    public override void OnApplicationInitialization(ApplicationInitializationContext context)
    {
        var app = context.GetApplicationBuilder();
        var env = context.GetEnvironment();

        app.UseForwardedHeaders();

        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseAbpRequestLocalization();

        if (!env.IsDevelopment())
        {
            app.UseErrorPage();
        }

        app.UseRouting();
        app.MapAbpStaticAssets();
        app.UseAbpStudioLink();
        app.UseAbpSecurityHeaders();
        app.UseCors();
        app.UseAuthentication();
        app.UseAbpOpenIddictValidation();

        if (MultiTenancyConsts.IsEnabled)
        {
            app.UseMultiTenancy();
        }

        app.UseUnitOfWork();
        app.UseDynamicClaims();
        app.UseAuthorization();

        app.UseSwagger();
        app.UseAbpSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "OpenTms API");

            var configuration = context.ServiceProvider.GetRequiredService<IConfiguration>();
            options.OAuthClientId(configuration["AuthServer:SwaggerClientId"]);
        });
        app.UseAuditing();
        app.UseAbpSerilogEnrichers();
        app.UseConfiguredEndpoints();
    }

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        await base.OnApplicationInitializationAsync(context);

        /* Transactional outbox dispatcher (docs/designs/outbox-per-tenant.md). Per-instance switch:
         * after the role split only the Jobs role keeps Outbox:DispatchEnabled on. */
        var outboxOptions = context.ServiceProvider.GetRequiredService<IOptions<OutboxOptions>>().Value;
        if (outboxOptions.DispatchEnabled)
        {
            await context.AddBackgroundWorkerAsync<OutboxDispatchWorker>();
        }

        /* Instance registry (docs/designs/maintenance-view.md): born, alive, dying. */
        var registryOptions = context.ServiceProvider.GetRequiredService<IOptions<InstanceRegistryOptions>>().Value;
        if (registryOptions.Enabled)
        {
            await context.AddBackgroundWorkerAsync<InstanceRegistryWorker>();
        }
    }
}
