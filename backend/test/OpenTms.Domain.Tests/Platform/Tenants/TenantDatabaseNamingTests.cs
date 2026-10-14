using System;
using System.Linq;
using System.Text;
using OpenTms.Platform.Tenants;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp.Guids;
using Xunit;

namespace OpenTms.Platform.Tenants;

/// <summary>
/// Unit tests for <see cref="TenantDatabaseNaming"/> (docs/designs/tenants-and-editions.md, "Naming").
/// Risk class: Critical — the generated name is the only thing between the drop job and someone
/// else's database: <see cref="TenantDatabaseNaming.IsGenerated"/> is the guard the provisioner and
/// the drop job rely on, so every name it accepts is a name the platform may DROP.
/// </summary>
public class TenantDatabaseNamingTests
{
    private static readonly Guid Id = Guid.Parse("0a1b2c3d-4e5f-6071-8293-a4b5c6d7e8f9");

    [Theory]
    [InlineData("Acme", "t_acme_0a1b2c3d4e5f60718293a4b5c6d7e8f9")]
    [InlineData("Acme Transport B.V.", "t_acme_transport_b_v_0a1b2c3d4e5f60718293a4b5c6d7e8f9")]
    [InlineData("  Van der Berg & Zn  ", "t_van_der_berg_zn_0a1b2c3d4e5f60718293a4b5c6d7e8f9")]
    [InlineData("DEMO-01", "t_demo_01_0a1b2c3d4e5f60718293a4b5c6d7e8f9")]
    [InlineData("Ünïcödé Logistiek", "t_n_c_d_logistiek_0a1b2c3d4e5f60718293a4b5c6d7e8f9")]
    [InlineData("", "t_tenant_0a1b2c3d4e5f60718293a4b5c6d7e8f9")]
    [InlineData("!!!", "t_tenant_0a1b2c3d4e5f60718293a4b5c6d7e8f9")]
    public void The_name_is_t_slug_and_the_whole_tenant_id(string tenantName, string expected)
    {
        /* B1 (review 2026-10-14): the whole id (32 hex), not its first eight digits. */
        TenantDatabaseNaming.For(tenantName, Id).ShouldBe(expected);
    }

    [Fact]
    public void The_name_contains_only_lowercase_letters_digits_and_underscores()
    {
        var name = TenantDatabaseNaming.For("A'; DROP DATABASE \"OpenTms\"; --", Id);

        name.ShouldAllBe(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_');
        name.ShouldStartWith("t_");
        name.ShouldEndWith("_0a1b2c3d4e5f60718293a4b5c6d7e8f9");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(27)]
    [InlineData(28)]
    [InlineData(29)]
    [InlineData(52)]
    [InlineData(64)]
    [InlineData(500)]
    public void The_name_never_exceeds_63_bytes(int nameLength)
    {
        var name = TenantDatabaseNaming.For(new string('x', nameLength), Id);

        Encoding.UTF8.GetByteCount(name).ShouldBeLessThanOrEqualTo(TenantDatabaseNaming.MaxLength);
        TenantDatabaseNaming.MaxLength.ShouldBe(63);
        TenantDatabaseNaming.IsGenerated(name).ShouldBeTrue();
    }

    [Fact]
    public void A_cut_slug_does_not_end_in_an_underscore_before_the_id_part()
    {
        /* 28 chars of room for the slug (63 - "t_" - "_" - 32 hex): the cut lands right after an underscore separator. */
        var tenantName = new string('a', 27) + " bcdef";

        var name = TenantDatabaseNaming.For(tenantName, Id);

        name.ShouldNotContain("__");
        name.ShouldBe("t_" + new string('a', 27) + "_" + Id.ToString("N"));
        name.Length.ShouldBeLessThanOrEqualTo(63);
        TenantDatabaseNaming.IsGenerated(name).ShouldBeTrue();
    }

    [Fact]
    public void The_same_tenant_always_gets_the_same_name()
    {
        TenantDatabaseNaming.For("Acme Transport", Id).ShouldBe(TenantDatabaseNaming.For("Acme Transport", Id));
    }

    [Fact]
    public void Two_tenants_with_the_same_name_get_different_names()
    {
        var first = TenantDatabaseNaming.For("Acme", Guid.NewGuid());
        var second = TenantDatabaseNaming.For("Acme", Guid.NewGuid());

        first.ShouldNotBe(second);
    }

    [Fact]
    public void Every_generated_name_is_recognised_as_generated()
    {
        foreach (var tenantName in new[] { "Acme", "", "Ünïcödé", new string('z', 200), "a b c", "123" })
        {
            var name = TenantDatabaseNaming.For(tenantName, Guid.NewGuid());
            TenantDatabaseNaming.IsGenerated(name).ShouldBeTrue(name);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("OpenTms")]
    [InlineData("OpenTms_Demo")]
    [InlineData("OpenTms_Tests_0a1b2c3d")]
    [InlineData("postgres")]
    [InlineData("template0")]
    [InlineData("template1")]
    [InlineData("t_acme")]
    [InlineData("t_acme_0a1b2c3")]
    [InlineData("t_acme_0a1b2c3g")]
    [InlineData("t_acme_0A1B2C3D")]
    [InlineData("T_ACME_0a1b2c3d")]
    [InlineData("x_acme_0a1b2c3d")]
    [InlineData(" t_acme_0a1b2c3d")]
    [InlineData("t_acme_0a1b2c3d ")]
    [InlineData("t_acme-x_0a1b2c3d")]
    [InlineData("t_acme_0a1b2c3d; DROP DATABASE \"OpenTms\"")]
    [InlineData("t_acme\"; DROP DATABASE \"OpenTms\"; --_0a1b2c3d")]
    [InlineData("t_acme'_0a1b2c3d")]
    [InlineData("t_acme_0a1b2c3d\"")]
    /* The old eight-digit form is no longer generated, so it is no longer droppable either (B1). */
    [InlineData("t_acme_0a1b2c3d")]
    [InlineData("t_acme_0a1b2c3d4e5f60718293a4b5c6d7e8f")]
    [InlineData("t_acme_0a1b2c3d4e5f60718293a4b5c6d7e8f90")]
    [InlineData("t_acme_0A1B2C3D4E5F60718293A4B5C6D7E8F9")]
    [InlineData("t_acme_0a1b2c3d4e5f60718293a4b5c6d7e8fg")]
    [InlineData("t_acme_0a1b2c3d-4e5f-6071-8293-a4b5c6d7e8f9")]
    [InlineData("T_ACME_0a1b2c3d4e5f60718293a4b5c6d7e8f9")]
    [InlineData(" t_acme_0a1b2c3d4e5f60718293a4b5c6d7e8f9")]
    [InlineData("t_acme_0a1b2c3d4e5f60718293a4b5c6d7e8f9 ")]
    [InlineData("t_acme-x_0a1b2c3d4e5f60718293a4b5c6d7e8f9")]
    [InlineData("t_acme_0a1b2c3d4e5f60718293a4b5c6d7e8f9; DROP DATABASE \"OpenTms\"")]
    [InlineData("t_acme\"; DROP DATABASE \"OpenTms\"; --_0a1b2c3d4e5f60718293a4b5c6d7e8f9")]
    [InlineData("t_0a1b2c3d4e5f60718293a4b5c6d7e8f9")]
    public void Names_the_platform_did_not_generate_are_rejected(string? name)
    {
        TenantDatabaseNaming.IsGenerated(name).ShouldBeFalse();
    }

    [Fact]
    public void A_name_with_a_trailing_newline_is_rejected()
    {
        /* The contract is "only ever [a-z0-9_]". In .NET, '$' also matches before a final '\n', so the
         * pattern alone would let "t_acme_<id>\n" through — a different identifier once quoted. */
        TenantDatabaseNaming.IsGenerated("t_acme_0a1b2c3d4e5f60718293a4b5c6d7e8f9\n").ShouldBeFalse();
    }

    [Fact]
    public void A_name_longer_than_63_characters_is_rejected_even_when_it_matches_the_pattern()
    {
        var name = "t_" + new string('a', 29) + "_0a1b2c3d4e5f60718293a4b5c6d7e8f9";
        name.Length.ShouldBe(64);

        TenantDatabaseNaming.IsGenerated(name).ShouldBeFalse();
        TenantDatabaseNaming.IsGenerated("t_" + new string('a', 28) + "_0a1b2c3d4e5f60718293a4b5c6d7e8f9").ShouldBeTrue();
    }

    [Fact]
    public void The_id_part_is_the_whole_tenant_id_as_32_lowercase_hex_digits()
    {
        var id = Guid.NewGuid();

        TenantDatabaseNaming.For("acme", id).ShouldEndWith("_" + id.ToString("N"));
        TenantDatabaseNaming.For("acme", id).Count(c => c == '_').ShouldBe(2);
    }

    [Theory]
    [InlineData(SequentialGuidType.SequentialAsString)]
    [InlineData(SequentialGuidType.SequentialAsBinary)]
    [InlineData(SequentialGuidType.SequentialAtEnd)]
    public void Tenants_created_in_the_same_moment_with_the_same_name_get_different_names(SequentialGuidType type)
    {
        /* B1: ABP's sequential GUIDs share their timestamp part when created close together, so a prefix of the id
         * (the old eight hex digits) was the same for both and two tenants named alike got one database. */
        var generator = new SequentialGuidGenerator(Options.Create(new AbpSequentialGuidGeneratorOptions { DefaultSequentialGuidType = type }));
        var ids = Enumerable.Range(0, 50).Select(_ => generator.Create(type)).ToList();

        var names = ids.Select(id => TenantDatabaseNaming.For("Acme Transport", id)).ToList();

        names.Distinct().Count().ShouldBe(ids.Count);
        names.ShouldAllBe(name => TenantDatabaseNaming.IsGenerated(name));
    }

    [Fact]
    public void The_old_eight_digit_prefix_really_collides_for_sequential_ids()
    {
        /* Guards the test above: it must exercise ids that share a prefix. */
        var generator = new SequentialGuidGenerator(Options.Create(new AbpSequentialGuidGeneratorOptions()));
        var ids = Enumerable.Range(0, 50).Select(_ => generator.Create(SequentialGuidType.SequentialAsString)).ToList();

        ids.Select(id => id.ToString("N")[..8]).Distinct().Count().ShouldBeLessThan(ids.Count);
    }
}
