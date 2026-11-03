using System.Collections.Generic;
using OpenTms.Integrations.OrderIntake.Channels;
using Volo.Abp.DependencyInjection;

namespace OpenTms.TestDoubles;

/// <summary>
/// The mail providers the services resolve (design 72 channels): the real Gmail and Microsoft clients unless a test sets
/// <see cref="Clients"/> to scripted ones, so a test can drive the mailbox service (folder choice, activation) without any
/// HTTP call. One instance per test application.
/// </summary>
public class TestMailProviders : ISingletonDependency
{
    public IReadOnlyList<IMailProviderClient>? Clients { get; set; }
}

/// <summary>The resolver every service gets: the test's scripted providers when set, else the registered real ones.</summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(MailProviderClientResolver))]
public class TestMailProviderClientResolver : MailProviderClientResolver, ITransientDependency
{
    public TestMailProviderClientResolver(IEnumerable<IMailProviderClient> clients, TestMailProviders overrides)
        : base(overrides.Clients ?? clients)
    {
    }
}
