using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTms.Platform.DataProtection;
using Volo.Abp.Threading;

namespace OpenTms.DataProtection;

/// <summary>
/// The data-protection key ring in the host database (docs/designs/tenants-and-editions.md, "Shared data-protection
/// keys"), so the auth cookie and the "Log in as" tickets that one instance protects are readable by every other one.
/// ASP.NET Core calls this synchronously and rarely: when it loads or refreshes its key ring (about once a day) and when
/// it creates a key. Each call gets a scope of its own because the key manager is a singleton.
/// </summary>
public class HostDatabaseXmlRepository : IXmlRepository
{
    /// <summary>The application name every instance protects under; by default it would be the content root path.</summary>
    public const string ApplicationName = "OpenTms";

    /// <summary>
    /// Data protection with its key ring in the host database. The fixed application name keeps the keys' purpose the
    /// same whatever directory an instance runs from.
    /// </summary>
    public static void AddTo(IServiceCollection services)
    {
        services.AddDataProtection().SetApplicationName(ApplicationName);
        services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(provider =>
            new ConfigureOptions<KeyManagementOptions>(options =>
                options.XmlRepository = new HostDatabaseXmlRepository(provider.GetRequiredService<IServiceScopeFactory>())));
    }

    private readonly IServiceScopeFactory _scopeFactory;

    public HostDatabaseXmlRepository(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IDataProtectionKeyStore>();
        return AsyncHelper.RunSync(store.GetAllAsync).Select(xml => XElement.Parse(xml)).ToList();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IDataProtectionKeyStore>();
        AsyncHelper.RunSync(() => store.AddAsync(friendlyName, element.ToString(SaveOptions.DisableFormatting)));
    }
}
