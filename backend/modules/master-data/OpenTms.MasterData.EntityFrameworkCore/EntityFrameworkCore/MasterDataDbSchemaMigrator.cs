using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTms.Platform.Data;
using Volo.Abp.DependencyInjection;

namespace OpenTms.MasterData.EntityFrameworkCore;

public class MasterDataDbSchemaMigrator : IDbSchemaMigrator, ITransientDependency
{
    private readonly IServiceProvider _serviceProvider;

    public MasterDataDbSchemaMigrator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public string ModuleName => "MasterData";

    public async Task MigrateAsync()
    {
        /* Resolve the DbContext from IServiceProvider (instead of injecting it directly)
         * so the connection string of the *current tenant* is used — this is what makes
         * database-per-tenant migrations work. */
        await _serviceProvider
            .GetRequiredService<MasterDataDbContext>()
            .Database
            .MigrateAsync();
    }
}
