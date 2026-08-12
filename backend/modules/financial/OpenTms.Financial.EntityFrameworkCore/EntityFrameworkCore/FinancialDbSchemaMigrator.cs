using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTms.Platform.Data;
using Volo.Abp.DependencyInjection;

namespace OpenTms.Financial.EntityFrameworkCore;

public class FinancialDbSchemaMigrator : IDbSchemaMigrator, ITransientDependency
{
    private readonly IServiceProvider _serviceProvider;

    public FinancialDbSchemaMigrator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public string ModuleName => "Financial";

    public async Task MigrateAsync()
    {
        /* Resolve the DbContext from IServiceProvider (instead of injecting it directly)
         * so the connection string of the *current tenant* is used — this is what makes
         * database-per-tenant migrations work. */
        await _serviceProvider
            .GetRequiredService<FinancialDbContext>()
            .Database
            .MigrateAsync();
    }
}
