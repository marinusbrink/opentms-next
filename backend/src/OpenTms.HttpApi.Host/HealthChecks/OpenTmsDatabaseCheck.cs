using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTms.Platform.Maintenance;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;

namespace OpenTms.HealthChecks;

/// <summary>
/// The host database: one read through the repository. The only check that takes part in /health/ready, and the only one
/// the polled /health-status writes out without sign-in: so its facts name the setting, never the server.
/// </summary>
public class OpenTmsDatabaseCheck : IHealthCheck, ITransientDependency
{
    public const string Name = "database";

    private static readonly string[] Settings = ["ConnectionStrings:Default"];

    protected readonly IIdentityRoleRepository IdentityRoleRepository;

    public OpenTmsDatabaseCheck(IIdentityRoleRepository identityRoleRepository)
    {
        IdentityRoleRepository = identityRoleRepository;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await IdentityRoleRepository.GetListAsync(sorting: nameof(IdentityRole.Id), maxResultCount: 1, cancellationToken: cancellationToken);

            return HealthCheckResult.Healthy("Could connect to database and get record.", HealthCheckFacts.Data(HealthCheckFacts.Reasons.Ok, Settings));
        }
        catch (Exception e)
        {
            return HealthCheckResult.Unhealthy("Error when trying to get database record. ", e, HealthCheckFacts.Data(HealthCheckFacts.Reasons.Unreachable, Settings));
        }
    }
}
