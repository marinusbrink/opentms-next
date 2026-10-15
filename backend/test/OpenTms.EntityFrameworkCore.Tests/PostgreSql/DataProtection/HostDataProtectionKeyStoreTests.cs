using System;
using System.Linq;
using System.Threading.Tasks;
using Npgsql;
using OpenTms.Platform.DataProtection;
using Shouldly;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace OpenTms.PostgreSql.DataProtection;

/// <summary>
/// The data-protection key store against real PostgreSQL (docs/designs/tenants-and-editions.md, "Shared data-protection
/// keys"). Risk class: Critical — authentication: a key read from or written to a tenant's own database would give the
/// requests of that tenant other keys than every other request, so cookies and "Log in as" tickets stop working between
/// instances, or only for some tenants.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class HostDataProtectionKeyStoreTests : OpenTmsPostgreSqlTestBase
{
    private readonly IDataProtectionKeyStore _store;
    private readonly ICurrentTenant _currentTenant;

    public HostDataProtectionKeyStoreTests()
    {
        _store = GetRequiredService<IDataProtectionKeyStore>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
    }

    private static async Task<long> CountAsync(string database, string marker)
    {
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(database));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM platform.\"DataProtectionKeys\" WHERE \"Xml\" LIKE @marker", connection);
        command.Parameters.AddWithValue("marker", "%" + marker + "%");
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task DeleteAsync(string marker)
    {
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.HostConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("DELETE FROM platform.\"DataProtectionKeys\" WHERE \"Xml\" LIKE @marker", connection);
        command.Parameters.AddWithValue("marker", "%" + marker + "%");
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task A_key_added_inside_a_tenant_lands_in_the_host_database_and_every_tenant_reads_it()
    {
        var marker = Guid.NewGuid().ToString("N");
        var first = $"<key id=\"{marker}-1\" />";
        var second = $"<key id=\"{marker}-2\" />";
        try
        {
            using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
            {
                await _store.AddAsync("key-" + marker, first);
            }

            await _store.AddAsync("key-" + marker, second);

            (await CountAsync(PostgreSqlTestDatabases.HostDatabase, marker)).ShouldBe(2);
            (await CountAsync(PostgreSqlTestDatabases.TenantADatabase, marker)).ShouldBe(0, "never in the tenant's own database");

            foreach (var tenant in new Guid?[] { null, PostgreSqlTestDatabases.TenantAId, PostgreSqlTestDatabases.SharedTenantId })
            {
                using (_currentTenant.Change(tenant))
                {
                    var all = (await _store.GetAllAsync()).ToList();
                    all.ShouldContain(first, $"tenant {tenant}");
                    all.ShouldContain(second, $"tenant {tenant}");
                    all.IndexOf(first).ShouldBeLessThan(all.IndexOf(second), "oldest first");
                }

                _currentTenant.Id.ShouldBeNull("the store restores the current tenant");
            }
        }
        finally
        {
            await DeleteAsync(marker);
        }
    }
}
