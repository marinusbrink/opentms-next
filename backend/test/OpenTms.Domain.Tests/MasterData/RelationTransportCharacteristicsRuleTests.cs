using System;
using System.Linq;
using OpenTms.MasterData.Relations;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace OpenTms.MasterData;

/// <summary>
/// Relation.SetTransportCharacteristics (design 43, "Relation administration", PO 2026-10-13): the input
/// is a set (duplicates collapse), the bound of 50 counts distinct ids, and a refused call changes nothing.
/// Risk class: High — the bound is the input-validation limit of the security quickscan (child collections
/// bounded at 50) and the join's primary key (RelationId, TransportCharacteristicsId) cannot hold a duplicate.
/// </summary>
public class RelationTransportCharacteristicsRuleTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static Relation NewRelation() => new(Guid.NewGuid(), Tenant, "acm01", "ACME Transport", RelationRoles.Customer);

    private static Guid[] Distinct(int count) => Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();

    [Fact]
    public void Duplicates_in_the_input_are_collapsed_to_one_row_per_characteristic()
    {
        var relation = NewRelation();
        var adr = Guid.NewGuid();
        var reefer = Guid.NewGuid();

        relation.SetTransportCharacteristics([adr, adr, reefer, adr, reefer]);

        relation.TransportCharacteristics.Count.ShouldBe(2);
        relation.TransportCharacteristics.Select(c => c.TransportCharacteristicsId).ShouldBe([adr, reefer], ignoreOrder: true);
        relation.TransportCharacteristics.Select(c => c.GetKeys()[1]).Distinct().Count().ShouldBe(2, "one join key per characteristic");
    }

    [Fact]
    public void Duplicates_of_an_already_held_characteristic_add_nothing()
    {
        var relation = NewRelation();
        var adr = Guid.NewGuid();
        relation.SetTransportCharacteristics([adr]);
        var row = relation.TransportCharacteristics.Single();

        relation.SetTransportCharacteristics([adr, adr, adr]);

        relation.TransportCharacteristics.ShouldHaveSingleItem().ShouldBeSameAs(row);
    }

    [Fact]
    public void Exactly_fifty_distinct_characteristics_are_accepted()
    {
        var relation = NewRelation();
        var ids = Distinct(MasterDataConsts.MaxTransportCharacteristicsPerRelation);

        relation.SetTransportCharacteristics(ids);

        MasterDataConsts.MaxTransportCharacteristicsPerRelation.ShouldBe(50);
        relation.TransportCharacteristics.Count.ShouldBe(50);
        relation.TransportCharacteristics.Select(c => c.TransportCharacteristicsId).ShouldBe(ids, ignoreOrder: true);
    }

    [Fact]
    public void The_bound_counts_distinct_ids_so_fifty_distinct_with_duplicates_are_accepted()
    {
        var relation = NewRelation();
        var ids = Distinct(MasterDataConsts.MaxTransportCharacteristicsPerRelation);

        /* 60 entries, 50 distinct: below the bound once collapsed. */
        relation.SetTransportCharacteristics([.. ids, .. ids.Take(10)]);

        relation.TransportCharacteristics.Count.ShouldBe(50);
    }

    [Fact]
    public void Fifty_one_distinct_characteristics_are_TooManyChildren_with_kind_and_max()
    {
        var relation = NewRelation();

        var ex = Should.Throw<BusinessException>(() => relation.SetTransportCharacteristics(Distinct(MasterDataConsts.MaxTransportCharacteristicsPerRelation + 1)));

        ex.Code.ShouldBe(MasterDataErrorCodes.TooManyChildren);
        ex.Data["kind"].ShouldBe("transportCharacteristics");
        ex.Data["max"].ShouldBe(MasterDataConsts.MaxTransportCharacteristicsPerRelation);
        MasterDataErrorCodes.BadRequestCodes.ShouldContain(MasterDataErrorCodes.TooManyChildren, "a client error, HTTP 400");
        relation.TransportCharacteristics.ShouldBeEmpty();
    }

    [Fact]
    public void A_refused_call_leaves_the_current_characteristics_untouched()
    {
        var relation = NewRelation();
        var adr = Guid.NewGuid();
        var reefer = Guid.NewGuid();
        relation.SetTransportCharacteristics([adr, reefer]);
        var before = relation.TransportCharacteristics.ToList();

        /* The current two plus 49 new ones: 51 distinct. */
        Should.Throw<BusinessException>(() => relation.SetTransportCharacteristics([adr, reefer, .. Distinct(MasterDataConsts.MaxTransportCharacteristicsPerRelation - 1)]))
            .Code.ShouldBe(MasterDataErrorCodes.TooManyChildren);

        relation.TransportCharacteristics.Count.ShouldBe(2);
        relation.TransportCharacteristics.ShouldBe(before, ignoreOrder: true);
        relation.TransportCharacteristics.ShouldAllBe(c => c.RelationId == relation.Id && c.TenantId == Tenant);
    }
}
