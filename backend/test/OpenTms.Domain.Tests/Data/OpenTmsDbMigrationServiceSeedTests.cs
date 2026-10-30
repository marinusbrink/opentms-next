using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace OpenTms.Data;

/// <summary>
/// The host administrator a deployed environment seeds (docs/designs/gcp-infrastructure.md): its own e-mail address
/// and a generated password from Secret Manager instead of the well-known development defaults. Risk class: Critical —
/// authentication: a public environment seeded with the defaults is open to anyone who has read the repository.
/// </summary>
public class OpenTmsDbMigrationServiceSeedTests
{
    private static OpenTmsDbMigrationService Service(Dictionary<string, string?> settings) =>
        new(null!, null!, null!, [], new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

    [Fact]
    public void A_deployed_environment_seeds_its_configured_administrator()
    {
        var service = Service(new()
        {
            ["Seed:HostAdminEmail"] = "admin@example.test",
            ["Seed:HostAdminPassword"] = "Generated-Pa55word!",
        });

        service.AdminEmail.ShouldBe("admin@example.test");
        service.AdminPassword.ShouldBe("Generated-Pa55word!");
    }

    [Fact]
    public void Without_settings_local_development_keeps_the_known_defaults()
    {
        var service = Service(new() { ["Seed:HostAdminEmail"] = " ", ["Seed:HostAdminPassword"] = null });

        service.AdminEmail.ShouldBe(OpenTmsConsts.AdminEmailDefaultValue);
        service.AdminPassword.ShouldBe(OpenTmsConsts.AdminPasswordDefaultValue);
    }
}
