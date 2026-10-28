using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Platform.SignIn;

/// <summary>
/// Rebuilds the sign-in lookup rows of the tenant (or the host) being seeded from its users
/// (docs/designs/62-sign-in-tenant-detection.md). The DbMigrator seeds the host and every tenant, so each run repairs
/// whatever <see cref="UserSignInLookupSynchronizer"/> missed (a crash between commit and write, users that existed
/// before the lookup); the provisioning job seeds a new tenant once. Idempotent: the rows are replaced, never added to.
/// </summary>
public class UserSignInLookupDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IReadOnlyRepository<IdentityUser, Guid> _userRepository;
    private readonly IUserSignInLookupStore _store;
    private readonly ICurrentTenant _currentTenant;

    public UserSignInLookupDataSeedContributor(
        IReadOnlyRepository<IdentityUser, Guid> userRepository,
        IUserSignInLookupStore store,
        ICurrentTenant currentTenant)
    {
        _userRepository = userRepository;
        _store = store;
        _currentTenant = currentTenant;
    }

    /// <summary>Users are read in pages of this size: a tenant's user list is never loaded at once.</summary>
    public const int PageSize = 1000;

    public async Task SeedAsync(DataSeedContext context)
    {
        using (_currentTenant.Change(context.TenantId))
        {
            var entries = new List<UserSignInLookupEntry>();
            for (var page = 0; ; page++)
            {
                var queryable = await _userRepository.GetQueryableAsync();
                var users = await _userRepository.AsyncExecuter.ToListAsync(queryable
                    .Where(u => u.NormalizedEmail != null && u.NormalizedEmail != "")
                    .OrderBy(u => u.Id)
                    .Skip(page * PageSize)
                    .Take(PageSize)
                    .Select(u => new { u.Id, u.NormalizedEmail }));
                entries.AddRange(users.Select(u => new UserSignInLookupEntry(u.Id, UserSignInLookupHash.Of(u.NormalizedEmail))));
                if (users.Count < PageSize)
                {
                    break;
                }
            }

            await _store.ReplaceTenantAsync(context.TenantId, entries);
        }
    }
}
