using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTms.Platform.Data;
using Volo.Abp.DependencyInjection;

namespace OpenTms.EntityFrameworkCore;

/* Migrates the main application DbContext: the ABP framework tables (identity, tenant store,
 * settings, permissions, OpenIddict, ...). The domain modules each have their own
 * IDbSchemaMigrator in their EntityFrameworkCore project. */
public class EntityFrameworkCoreOpenTmsDbSchemaMigrator
    : IDbSchemaMigrator, ITransientDependency
{
    private readonly IServiceProvider _serviceProvider;

    public EntityFrameworkCoreOpenTmsDbSchemaMigrator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public string ModuleName => "Framework";

    public async Task MigrateAsync()
    {
        /* We intentionally resolve the OpenTmsDbContext from IServiceProvider
         * (instead of directly injecting it) to properly get the connection
         * string of the current tenant in the current scope. */
        await _serviceProvider
            .GetRequiredService<OpenTmsDbContext>()
            .Database
            .MigrateAsync();
    }
}
