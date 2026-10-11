using System.IO;
using Shouldly;
using Xunit;

namespace OpenTms.PostgreSql;

/// <summary>
/// The naming rules of the per-checkout PostgreSQL test databases. Pure: no server is touched,
/// and the class deliberately stays outside <see cref="PostgreSqlTestCollection"/>.
/// </summary>
public class PostgreSqlTestDatabaseNamingTests
{
    [Fact]
    public void The_names_of_this_checkout_are_valid_identifiers_within_the_63_character_limit()
    {
        PostgreSqlTestDatabases.DatabaseSuffix.ShouldNotBeNullOrWhiteSpace();
        PostgreSqlTestDatabases.HostDatabase.ShouldBe("OpenTms_Tests_" + PostgreSqlTestDatabases.DatabaseSuffix);
        PostgreSqlTestDatabases.TenantADatabase.ShouldBe(PostgreSqlTestDatabases.HostDatabase + "_TenantA");
        PostgreSqlTestDatabases.TenantADatabase.Length.ShouldBeLessThanOrEqualTo(63);
        PostgreSqlTestDatabases.TenantADatabase.ShouldAllBe(c => char.IsAsciiLetterOrDigit(c) || c == '_');
        PostgreSqlTestDatabases.HostConnectionString.ShouldEndWith(";Database=" + PostgreSqlTestDatabases.HostDatabase);
    }

    [Fact]
    public void Two_checkouts_get_different_suffixes_and_one_checkout_always_the_same()
    {
        var mainCheckout = Path.Combine(Path.GetTempPath(), "opentms-next", "backend");
        var worktree = Path.Combine(Path.GetTempPath(), "opentms-next", ".claude", "worktrees", "x", "backend");

        var first = PostgreSqlTestDatabases.ComputeDatabaseSuffix(null, mainCheckout);
        var again = PostgreSqlTestDatabases.ComputeDatabaseSuffix("", mainCheckout + Path.DirectorySeparatorChar);
        var other = PostgreSqlTestDatabases.ComputeDatabaseSuffix(null, worktree);

        first.Length.ShouldBe(8);
        first.ShouldAllBe(c => char.IsAsciiHexDigitLower(c));
        again.ShouldBe(first);
        other.ShouldNotBe(first);
    }

    [Fact]
    public void A_configured_suffix_wins_and_is_made_safe_for_an_identifier()
    {
        PostgreSqlTestDatabases.ComputeDatabaseSuffix(" wt-1 a.b ", "/anywhere").ShouldBe("wt_1_a_b");
        PostgreSqlTestDatabases.ComputeDatabaseSuffix("Session2", "/anywhere").ShouldBe("Session2");
    }

    [Fact]
    public void A_long_configured_suffix_is_cut_so_both_names_fit()
    {
        var suffix = PostgreSqlTestDatabases.ComputeDatabaseSuffix(new string('x', 100), "/anywhere");

        ("OpenTms_Tests_" + suffix + "_TenantA").Length.ShouldBe(63);
    }
}
