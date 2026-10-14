using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTms.Platform.Administration;
using OpenTms.Platform.Data;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;

namespace OpenTms.Platform.Tenants;

/* Provisions a tenant's own database (docs/designs/tenants-and-editions.md):
 *   1. the database and its login role on the server (ITenantDatabaseProvisioner);
 *   2. every module schema, Framework first, exactly as OpenTmsDbMigrationService does it;
 *   3. the seed, with a random administrator password that is discarded;
 *   4. the administrator's invitation, once.
 * Every step is idempotent, so "Retry provisioning" finishes a partial run. Later
 * migrations of this database belong to the DbMigrator, as for every other database. */
public class TenantProvisioningJob : AsyncBackgroundJob<TenantProvisioningArgs>, ITransientDependency
{
    private readonly TenantStateWriter _stateWriter;
    private readonly ITenantDatabaseProvisioner _provisioner;
    private readonly IEnumerable<IDbSchemaMigrator> _schemaMigrators;
    private readonly IDataSeeder _dataSeeder;
    private readonly ICurrentTenant _currentTenant;
    private readonly TenantInvitationSender _invitationSender;
    private readonly IOptions<IdentityOptions> _identityOptions;

    public TenantProvisioningJob(
        TenantStateWriter stateWriter,
        ITenantDatabaseProvisioner provisioner,
        IEnumerable<IDbSchemaMigrator> schemaMigrators,
        IDataSeeder dataSeeder,
        ICurrentTenant currentTenant,
        TenantInvitationSender invitationSender,
        IOptions<IdentityOptions> identityOptions)
    {
        _stateWriter = stateWriter;
        _provisioner = provisioner;
        _schemaMigrators = schemaMigrators;
        _dataSeeder = dataSeeder;
        _currentTenant = currentTenant;
        _invitationSender = invitationSender;
        _identityOptions = identityOptions;
    }

    public override async Task ExecuteAsync(TenantProvisioningArgs args)
    {
        var tenant = await _stateWriter.FindAsync(args.TenantId);
        if (tenant == null)
        {
            Logger.LogWarning("Tenant {TenantId} no longer exists; provisioning skipped", args.TenantId);
            return;
        }

        Logger.LogInformation("Tenant provisioning started for tenant {TenantId}", tenant.Id);
        await _stateWriter.SetStateAsync(tenant.Id, TenantProvisioningState.Running);

        try
        {
            var connectionString = tenant.FindDefaultConnectionString()
                                   ?? throw new InvalidOperationException("The tenant has no database of its own to provision.");

            await _provisioner.EnsureDatabaseAsync(connectionString);

            using (_currentTenant.Change(tenant.Id, tenant.Name))
            {
                foreach (var migrator in _schemaMigrators
                             .OrderBy(m => m.ModuleName == "Framework" ? 0 : 1)
                             .ThenBy(m => m.ModuleName, StringComparer.Ordinal))
                {
                    Logger.LogInformation("Tenant {TenantId}: migrating module {ModuleName}", tenant.Id, migrator.ModuleName);
                    await migrator.MigrateAsync();
                }

                /* The seeder creates the administrator only when it does not exist yet; the password is never kept. */
                await _dataSeeder.SeedAsync(new DataSeedContext(tenant.Id)
                    .WithProperty(IdentityDataSeedContributor.AdminEmailPropertyName,
                        tenant.GetProperty<string?>(TenantProperties.PendingAdminEmail))
                    .WithProperty(IdentityDataSeedContributor.AdminPasswordPropertyName,
                        RandomPasswordGenerator.Generate(_identityOptions.Value.Password)));
            }

            /* The tenant is usable once seeded; an invitation that cannot be sent (mail server, address) does not
             * undo that. It is noted on the tenant and "Resend invitation" tries again. Only the exception type is
             * kept: a mail server's message often contains the address (constitution rule 5). */
            string? invitationProblem = null;
            if (tenant.GetProperty<DateTime?>(TenantProperties.InvitationSentAt) == null)
            {
                try
                {
                    await _invitationSender.SendAsync(tenant.Id, tenant.Name);
                    await _stateWriter.MarkInvitationSentAsync(tenant.Id);
                }
                catch (Exception ex)
                {
                    invitationProblem = $"InvitationNotSent: {ex.GetType().Name}";
                    Logger.LogWarning(
                        "Tenant {TenantId} is provisioned but its invitation could not be sent ({ExceptionType})",
                        tenant.Id, ex.GetType().Name);
                }
            }

            await _stateWriter.SetStateAsync(tenant.Id, TenantProvisioningState.Succeeded, invitationProblem);
            Logger.LogInformation("Tenant provisioning succeeded for tenant {TenantId}", tenant.Id);
        }
        catch (Exception ex)
        {
            /* Type and message only: a provider message may name the database, never the password. */
            await _stateWriter.SetStateAsync(tenant.Id, TenantProvisioningState.Failed, $"{ex.GetType().Name}: {ex.Message}");
            /* Not rethrown: a Hangfire retry could overlap the "Retry provisioning" a user starts from the
             * Failed state. The state is the signal; the retry endpoint accepts only Failed, so runs never overlap. */
            Logger.LogError(ex, "Tenant provisioning FAILED for tenant {TenantId}", tenant.Id);
        }
    }
}
