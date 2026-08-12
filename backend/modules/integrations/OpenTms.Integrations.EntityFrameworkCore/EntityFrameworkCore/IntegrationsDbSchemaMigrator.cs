using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTms.Platform.Data;
using Volo.Abp.DependencyInjection;

namespace OpenTms.Integrations.EntityFrameworkCore;

public class IntegrationsDbSchemaMigrator : IDbSchemaMigrator, ITransientDependency
{
    private readonly IServiceProvider _serviceProvider;

    public IntegrationsDbSchemaMigrator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public string ModuleName => "Integrations";

    public async Task MigrateAsync()
    {
        /* Resolve the DbContext from IServiceProvider (instead of injecting it directly)
         * so the connection string of the *current tenant* is used — this is what makes
         * database-per-tenant migrations work. */
        await _serviceProvider
            .GetRequiredService<IntegrationsDbContext>()
            .Database
            .MigrateAsync();
    }
}
