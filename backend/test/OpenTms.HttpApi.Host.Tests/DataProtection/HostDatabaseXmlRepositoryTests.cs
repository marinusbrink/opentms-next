using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using OpenTms.DataProtection;
using OpenTms.Platform.DataProtection;
using Shouldly;
using Xunit;

namespace OpenTms.DataProtection;

/// <summary>
/// The host's data-protection key ring on the shared key store (docs/designs/tenants-and-editions.md, "Shared
/// data-protection keys"). The store itself is covered against PostgreSQL by HostDataProtectionKeyStoreTests; here the
/// store is an in-memory stand-in and the key manager is ASP.NET Core's own. Risk class: Critical — authentication: a
/// cookie or ticket protected by one instance must be readable by every other one, and by nobody without the keys.
/// </summary>
public class HostDatabaseXmlRepositoryTests
{
    private sealed class InMemoryKeyStore : IDataProtectionKeyStore
    {
        public List<(string? FriendlyName, string Xml)> Rows { get; } = new();

        public Task<IReadOnlyList<string>> GetAllAsync() => Task.FromResult<IReadOnlyList<string>>(Rows.Select(r => r.Xml).ToList());

        public Task AddAsync(string? friendlyName, string xml)
        {
            Rows.Add((friendlyName, xml));
            return Task.CompletedTask;
        }
    }

    /// <summary>One "instance": its own container and key manager, wired the way the host module wires them.</summary>
    private static ServiceProvider Instance(IDataProtectionKeyStore store, string? applicationName = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(store);
        HostDatabaseXmlRepository.AddTo(services);
        if (applicationName != null)
        {
            services.AddDataProtection().SetApplicationName(applicationName);
        }

        return services.BuildServiceProvider();
    }

    [Fact]
    public void What_one_instance_protects_another_instance_on_the_same_store_unprotects()
    {
        var store = new InMemoryKeyStore();
        using var first = Instance(store);
        using var second = Instance(store);

        var protectedTicket = first.GetRequiredService<IDataProtectionProvider>().CreateProtector("purpose").Protect("payload");

        second.GetRequiredService<IDataProtectionProvider>().CreateProtector("purpose").Unprotect(protectedTicket).ShouldBe("payload");
        store.Rows.ShouldHaveSingleItem("one key, created by the first instance and reused by the second");
        store.Rows[0].FriendlyName.ShouldStartWith("key-");
        XElement.Parse(store.Rows[0].Xml).Name.LocalName.ShouldBe("key");
    }

    [Fact]
    public void An_instance_on_another_store_cannot_unprotect()
    {
        using var first = Instance(new InMemoryKeyStore());
        using var stranger = Instance(new InMemoryKeyStore());

        var protectedTicket = first.GetRequiredService<IDataProtectionProvider>().CreateProtector("purpose").Protect("payload");

        Should.Throw<System.Security.Cryptography.CryptographicException>(() =>
            stranger.GetRequiredService<IDataProtectionProvider>().CreateProtector("purpose").Unprotect(protectedTicket));
    }

    [Fact]
    public void The_application_name_is_part_of_the_purpose()
    {
        /* Why the host module fixes it: by default it is the content root, which may differ between instances. */
        var store = new InMemoryKeyStore();
        using var first = Instance(store);
        using var elsewhere = Instance(store, "/some/other/content/root");

        var protectedTicket = first.GetRequiredService<IDataProtectionProvider>().CreateProtector("purpose").Protect("payload");

        Should.Throw<System.Security.Cryptography.CryptographicException>(() =>
            elsewhere.GetRequiredService<IDataProtectionProvider>().CreateProtector("purpose").Unprotect(protectedTicket));
    }
}
