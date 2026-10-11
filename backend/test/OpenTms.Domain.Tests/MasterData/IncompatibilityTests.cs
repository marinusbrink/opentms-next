using System;
using System.Collections.Generic;
using System.Linq;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace OpenTms.MasterData.Incompatibilities;

/// <summary>
/// Unit tests for the canonical undirected incompatibility pair and its diff (design 41, join tables).
/// Risk class: High — a non-canonical pair would let "A with B" and "B with A" drift apart; a wrong
/// diff would silently drop or duplicate a tenant's "do not combine" rules.
/// </summary>
public class IncompatibilityTests
{
    private static readonly Guid Low = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Mid = Guid.Parse("7fffffff-ffff-ffff-ffff-ffffffffffff");
    private static readonly Guid High = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    [Fact]
    public void Canonical_puts_the_id_whose_hex_form_sorts_first_on_the_left_regardless_of_input_order()
    {
        Incompatibility.Canonical(Low, High).ShouldBe((Low, High));
        Incompatibility.Canonical(High, Low).ShouldBe((Low, High));
        Incompatibility.Canonical(Mid, Low).ShouldBe((Low, Mid));
        Incompatibility.Canonical(High, Mid).ShouldBe((Mid, High));
    }

    [Fact]
    public void Canonical_order_is_the_ordinal_order_of_the_32_character_hex_form()
    {
        var ids = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToList();

        foreach (var a in ids)
        {
            foreach (var b in ids.Where(b => b != a))
            {
                var (left, right) = Incompatibility.Canonical(a, b);
                string.CompareOrdinal(left.ToString("N"), right.ToString("N")).ShouldBeLessThan(0);
                Incompatibility.Canonical(b, a).ShouldBe((left, right));
            }
        }
    }

    [Fact]
    public void Canonical_rejects_a_pair_of_the_same_id()
    {
        var self = Guid.NewGuid();

        var ex = Should.Throw<BusinessException>(() => Incompatibility.Canonical(self, self));

        ex.Code.ShouldBe(MasterDataErrorCodes.SelfIncompatibility);
        ex.Data["id"].ShouldBe(self);
    }

    [Fact]
    public void A_pair_stores_the_canonical_order_and_the_tenant()
    {
        var tenant = Guid.NewGuid();

        var pair = new ProductIncompatibility(tenant, High, Low);

        pair.LeftId.ShouldBe(Low);
        pair.RightId.ShouldBe(High);
        pair.TenantId.ShouldBe(tenant);
        pair.GetKeys().ShouldBe([Low, High]);
        pair.Involves(Low).ShouldBeTrue();
        pair.Involves(High).ShouldBeTrue();
        pair.Involves(Mid).ShouldBeFalse();
        pair.OtherThan(Low).ShouldBe(High);
        pair.OtherThan(High).ShouldBe(Low);
    }

    [Fact]
    public void Every_concrete_pair_type_rejects_a_self_pair()
    {
        var self = Guid.NewGuid();

        Should.Throw<BusinessException>(() => new TemperatureIncompatibility(null, self, self)).Code.ShouldBe(MasterDataErrorCodes.SelfIncompatibility);
        Should.Throw<BusinessException>(() => new ProductIncompatibility(null, self, self)).Code.ShouldBe(MasterDataErrorCodes.SelfIncompatibility);
        Should.Throw<BusinessException>(() => new TransportUnitIncompatibility(null, self, self)).Code.ShouldBe(MasterDataErrorCodes.SelfIncompatibility);
    }

    [Fact]
    public void Diff_adds_the_missing_others_and_removes_the_pairs_no_longer_wanted()
    {
        var id = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var existing = new List<(Guid, Guid)> { Incompatibility.Canonical(id, a), Incompatibility.Canonical(b, id) };

        var (toAdd, toRemove) = Incompatibility.Diff(id, existing, [b, c]);

        toAdd.ShouldBe([c]);
        toRemove.ShouldBe([Incompatibility.Canonical(id, a)]);
    }

    [Fact]
    public void Diff_ignores_the_own_id_and_duplicates_in_the_wanted_set()
    {
        var id = Guid.NewGuid();
        var a = Guid.NewGuid();

        var (toAdd, toRemove) = Incompatibility.Diff(id, [], [id, a, a, id]);

        toAdd.ShouldBe([a]);
        toRemove.ShouldBeEmpty();
    }

    [Fact]
    public void Diff_is_empty_when_the_wanted_set_equals_the_existing_set()
    {
        var id = Guid.NewGuid();
        var a = Guid.NewGuid();
        var existing = new[] { Incompatibility.Canonical(a, id) };

        var (toAdd, toRemove) = Incompatibility.Diff(id, existing, [a]);

        toAdd.ShouldBeEmpty();
        toRemove.ShouldBeEmpty();
    }

    [Fact]
    public void Diff_with_an_empty_wanted_set_removes_every_existing_pair()
    {
        var id = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var existing = new[] { Incompatibility.Canonical(id, a), Incompatibility.Canonical(id, b) };

        var (toAdd, toRemove) = Incompatibility.Diff(id, existing, []);

        toAdd.ShouldBeEmpty();
        toRemove.Count.ShouldBe(2);
        toRemove.ShouldContain(Incompatibility.Canonical(id, a));
        toRemove.ShouldContain(Incompatibility.Canonical(id, b));
    }
}
