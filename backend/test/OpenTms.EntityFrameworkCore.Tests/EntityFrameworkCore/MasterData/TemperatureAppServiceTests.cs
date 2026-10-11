using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData;
using OpenTms.MasterData.Transport;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace OpenTms.EntityFrameworkCore.MasterData;

/// <summary>
/// TemperatureAppService on SQLite (design 41). Risk class: Critical for tenant isolation and the
/// unique name; High for the range rule, archiving, the incompatibility sync and bulk delete.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class TemperatureAppServiceTests : MasterDataAppServiceTestBase
{
    private readonly ITemperatureAppService _service;

    public TemperatureAppServiceTests()
    {
        _service = GetRequiredService<ITemperatureAppService>();
    }

    private Task<TemperatureDto> CreateAsync(string name, double? min = null, double? max = null, bool archived = false, params Guid[] incompatible)
        => _service.CreateAsync(new TemperatureCreateDto { Name = name, MinCelsius = min, MaxCelsius = max, IsArchived = archived, IncompatibleTemperatureIds = [.. incompatible] });

    private static TemperatureUpdateDto UpdateFrom(TemperatureDto dto) => new()
    {
        Name = dto.Name,
        Description = dto.Description,
        MinCelsius = dto.MinCelsius,
        MaxCelsius = dto.MaxCelsius,
        IsArchived = dto.IsArchived,
        IncompatibleTemperatureIds = dto.IncompatibleTemperatures.Select(t => t.Id).ToList(),
        ConcurrencyStamp = dto.ConcurrencyStamp
    };

    [Fact]
    public async Task Create_get_list_and_lookup_round_trip()
    {
        using var _ = InTenantA();

        var frozen = await CreateAsync(" Frozen ", -25, -18);

        frozen.Name.ShouldBe("Frozen");
        frozen.MinCelsius.ShouldBe(-25);
        frozen.MaxCelsius.ShouldBe(-18);
        frozen.IsArchived.ShouldBeFalse();
        frozen.IncompatibleTemperatures.ShouldBeEmpty();

        (await _service.GetAsync(frozen.Id)).MinCelsius.ShouldBe(-25);
        var row = (await _service.GetListAsync(List<ArchivableListRequestDto>())).Rows.ShouldHaveSingleItem();
        row.Name.ShouldBe("Frozen");
        row.MaxCelsius.ShouldBe(-18);
        (await _service.GetLookupAsync()).ShouldHaveSingleItem().Name.ShouldBe("Frozen");
    }

    [Fact]
    public async Task A_minimum_above_the_maximum_is_refused_on_create_and_update()
    {
        using var _ = InTenantA();

        (await Should.ThrowAsync<BusinessException>(() => CreateAsync("Wrong", 5, -5))).Code.ShouldBe(MasterDataErrorCodes.InvalidTemperatureRange);
        (await _service.GetListAsync(List<ArchivableListRequestDto>())).TotalCount.ShouldBe(0);

        var chilled = await CreateAsync("Chilled", 0, 4);
        var update = UpdateFrom(chilled);
        update.MinCelsius = 10;
        (await Should.ThrowAsync<BusinessException>(() => _service.UpdateAsync(chilled.Id, update))).Code.ShouldBe(MasterDataErrorCodes.InvalidTemperatureRange);
        (await _service.GetAsync(chilled.Id)).MinCelsius.ShouldBe(0);
    }

    [Fact]
    public async Task Name_is_unique_per_tenant_and_freed_by_a_soft_delete()
    {
        using var _ = InTenantA();
        var frozen = await CreateAsync("Frozen");

        var ex = await Should.ThrowAsync<BusinessException>(() => CreateAsync(" Frozen "));
        ex.Code.ShouldBe(MasterDataErrorCodes.DuplicateKey);
        ex.Data["field"].ShouldBe("name");

        await _service.DeleteAsync(frozen.Id);
        (await CreateAsync("Frozen")).Id.ShouldNotBe(frozen.Id);
    }

    [Fact]
    public async Task Archived_temperatures_are_hidden_from_the_list_and_the_lookup_unless_asked_for()
    {
        using var _ = InTenantA();
        await CreateAsync("Frozen", -25, -18);
        var old = await CreateAsync("Old range", archived: true);

        var visible = await _service.GetListAsync(List<ArchivableListRequestDto>());
        visible.TotalCount.ShouldBe(1);
        visible.Rows.ShouldHaveSingleItem().Name.ShouldBe("Frozen");

        var all = await _service.GetListAsync(new ArchivableListRequestDto { IncludeArchived = true, SortBy = "isArchived" });
        all.TotalCount.ShouldBe(2);
        all.Rows.Select(r => r.Name).ShouldBe(["Frozen", "Old range"]);

        (await _service.GetLookupAsync()).ShouldHaveSingleItem().Name.ShouldBe("Frozen");
        (await _service.GetAsync(old.Id)).IsArchived.ShouldBeTrue();

        var unarchive = UpdateFrom(old);
        unarchive.IsArchived = false;
        await _service.UpdateAsync(old.Id, unarchive);
        (await _service.GetLookupAsync()).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Incompatibilities_are_symmetric_canonical_and_synced_from_either_side()
    {
        using var _ = InTenantA();
        var frozen = await CreateAsync("Frozen", -25, -18);
        var ambient = await CreateAsync("Ambient", 10, 25);
        var chilled = await CreateAsync("Chilled", 0, 4);

        await _service.UpdateAsync(frozen.Id, UpdateWith(frozen, ambient.Id));
        var frozenWithAmbient = await _service.GetAsync(frozen.Id);
        frozenWithAmbient.IncompatibleTemperatures.ShouldHaveSingleItem().Id.ShouldBe(ambient.Id);
        frozenWithAmbient.IncompatibleTemperatures.Single().Name.ShouldBe("Ambient");

        /* The other side lists the pair without having been written. */
        (await _service.GetAsync(ambient.Id)).IncompatibleTemperatures.ShouldHaveSingleItem().Id.ShouldBe(frozen.Id);

        /* Entering the same pair from the other side adds nothing. */
        var ambientDto = await _service.GetAsync(ambient.Id);
        await _service.UpdateAsync(ambient.Id, UpdateWith(ambientDto, frozen.Id, chilled.Id, frozen.Id));
        (await _service.GetAsync(ambient.Id)).IncompatibleTemperatures.Select(t => t.Name).ShouldBe(["Chilled", "Frozen"]);
        (await _service.GetAsync(frozen.Id)).IncompatibleTemperatures.ShouldHaveSingleItem().Id.ShouldBe(ambient.Id);
        (await _service.GetAsync(chilled.Id)).IncompatibleTemperatures.ShouldHaveSingleItem().Id.ShouldBe(ambient.Id);

        /* Removing from the B side removes the pair for A too. */
        var ambientAfter = await _service.GetAsync(ambient.Id);
        await _service.UpdateAsync(ambient.Id, UpdateWith(ambientAfter, chilled.Id));
        (await _service.GetAsync(ambient.Id)).IncompatibleTemperatures.ShouldHaveSingleItem().Id.ShouldBe(chilled.Id);
        (await _service.GetAsync(frozen.Id)).IncompatibleTemperatures.ShouldBeEmpty();
    }

    /// <summary>
    /// API contract (design 41): CreateAsync and UpdateAsync return the detail DTO, which carries
    /// incompatibleTemperatures. The response must therefore already list the pairs the same call wrote.
    /// </summary>
    [Fact]
    public async Task The_create_and_update_responses_carry_the_incompatibilities_written_in_the_same_call()
    {
        using var _ = InTenantA();
        var frozen = await CreateAsync("Frozen");

        var ambient = await CreateAsync("Ambient", incompatible: frozen.Id);
        ambient.IncompatibleTemperatures.ShouldHaveSingleItem().Id.ShouldBe(frozen.Id);

        var chilled = await CreateAsync("Chilled");
        var updated = await _service.UpdateAsync(chilled.Id, UpdateWith(chilled, frozen.Id));
        updated.IncompatibleTemperatures.ShouldHaveSingleItem().Id.ShouldBe(frozen.Id);
    }

    [Fact]
    public async Task Incompatibility_with_itself_is_dropped_silently_and_an_unknown_id_is_refused()
    {
        using var _ = InTenantA();
        var frozen = await CreateAsync("Frozen");

        await _service.UpdateAsync(frozen.Id, UpdateWith(frozen, frozen.Id));
        (await _service.GetAsync(frozen.Id)).IncompatibleTemperatures.ShouldBeEmpty();

        var frozenNow = await _service.GetAsync(frozen.Id);
        var ex = await Should.ThrowAsync<BusinessException>(() => _service.UpdateAsync(frozen.Id, UpdateWith(frozenNow, Guid.NewGuid())));
        ex.Code.ShouldBe(MasterDataErrorCodes.ReferenceNotFound);
        ex.Data["field"].ShouldBe("incompatibleIds");

        (await Should.ThrowAsync<BusinessException>(() => CreateAsync("Chilled", incompatible: Guid.NewGuid()))).Code.ShouldBe(MasterDataErrorCodes.ReferenceNotFound);
        /* That the failed create leaves no row behind is the transaction's job: MasterDataTransactionTests (PostgreSQL). */
    }

    [Fact]
    public async Task Incompatibilities_can_be_set_on_create_and_are_removed_when_a_side_is_deleted()
    {
        using var _ = InTenantA();
        var frozen = await CreateAsync("Frozen");
        var ambient = await CreateAsync("Ambient", incompatible: frozen.Id);
        (await _service.GetAsync(ambient.Id)).IncompatibleTemperatures.ShouldHaveSingleItem().Id.ShouldBe(frozen.Id);
        (await _service.GetAsync(frozen.Id)).IncompatibleTemperatures.ShouldHaveSingleItem().Id.ShouldBe(ambient.Id);

        await _service.DeleteAsync(ambient.Id);

        (await _service.GetAsync(frozen.Id)).IncompatibleTemperatures.ShouldBeEmpty();

        var chilled = await CreateAsync("Chilled", incompatible: frozen.Id);
        (await _service.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = Explicit(chilled.Id) })).DeletedCount.ShouldBe(1);
        (await _service.GetAsync(frozen.Id)).IncompatibleTemperatures.ShouldBeEmpty();
    }

    [Fact]
    public async Task Update_checks_the_stamp_and_list_sorts_on_the_whitelist()
    {
        using var _ = InTenantA();
        var frozen = await CreateAsync("Frozen", -25, -18);
        await CreateAsync("Ambient", 10, 25);
        await CreateAsync("Chilled", 0, 4);

        var rename = UpdateFrom(frozen);
        rename.Name = "Deep frozen";
        (await _service.UpdateAsync(frozen.Id, rename)).Name.ShouldBe("Deep frozen");
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => _service.UpdateAsync(frozen.Id, UpdateFrom(frozen)));

        (await _service.GetListAsync(List<ArchivableListRequestDto>())).Rows.Select(r => r.Name).ShouldBe(["Ambient", "Chilled", "Deep frozen"]);
        (await _service.GetListAsync(List<ArchivableListRequestDto>(sortBy: "minCelsius"))).Rows.Select(r => r.Name).ShouldBe(["Deep frozen", "Chilled", "Ambient"]);
        (await _service.GetListAsync(List<ArchivableListRequestDto>(sortBy: "maxCelsius", sortDir: "desc"))).Rows.First().Name.ShouldBe("Ambient");
        (await _service.GetListAsync(List<ArchivableListRequestDto>(search: "chill"))).Rows.ShouldHaveSingleItem().Name.ShouldBe("Chilled");
        (await Should.ThrowAsync<BusinessException>(() => _service.GetListAsync(List<ArchivableListRequestDto>(sortBy: "incompatible")))).Code.ShouldBe(MasterDataErrorCodes.UnknownSortColumn);
    }

    [Fact]
    public async Task BulkDelete_filter_based_skips_archived_rows_unless_asked_and_a_second_run_deletes_nothing()
    {
        using var _ = InTenantA();
        var a = await CreateAsync("Range A");
        var b = await CreateAsync("Range B", archived: true);
        var c = await CreateAsync("Range C");

        var first = await _service.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = FilterBased("range", c.Id) });
        first.DeletedCount.ShouldBe(1);
        (await _service.GetListAsync(new ArchivableListRequestDto { IncludeArchived = true })).Rows.Select(r => r.Id).ShouldBe([b.Id, c.Id], ignoreOrder: true);

        (await _service.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = FilterBased("range", c.Id) })).DeletedCount.ShouldBe(0);

        (await _service.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = FilterBased("range", c.Id), IncludeArchived = true })).DeletedCount.ShouldBe(1);
        (await _service.GetListAsync(new ArchivableListRequestDto { IncludeArchived = true })).Rows.ShouldHaveSingleItem().Id.ShouldBe(c.Id);
        a.Id.ShouldNotBe(Guid.Empty);
    }

    // ── Tenant isolation (Critical) ───────────────────────────────────────────

    [Fact]
    public async Task Rows_of_tenant_A_are_invisible_and_untouchable_from_tenant_B()
    {
        TemperatureDto frozen;
        using (InTenantA())
        {
            frozen = await CreateAsync("Frozen");
        }

        using (InTenantB())
        {
            (await _service.GetListAsync(List<ArchivableListRequestDto>())).TotalCount.ShouldBe(0);
            (await _service.GetLookupAsync()).ShouldBeEmpty();
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(frozen.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.UpdateAsync(frozen.Id, UpdateFrom(frozen)));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.DeleteAsync(frozen.Id));
            (await _service.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = FilterBased() })).DeletedCount.ShouldBe(0);

            /* An incompatibility may not reach into tenant A. */
            (await Should.ThrowAsync<BusinessException>(() => CreateAsync("Ambient", incompatible: frozen.Id))).Code.ShouldBe(MasterDataErrorCodes.ReferenceNotFound);
            (await CreateAsync("Frozen")).Id.ShouldNotBe(frozen.Id);
        }

        using (InTenantA())
        {
            (await _service.GetListAsync(List<ArchivableListRequestDto>())).Rows.ShouldHaveSingleItem().Id.ShouldBe(frozen.Id);
            (await _service.GetAsync(frozen.Id)).IncompatibleTemperatures.ShouldBeEmpty();
        }
    }

    private static TemperatureUpdateDto UpdateWith(TemperatureDto dto, params Guid[] incompatible)
    {
        var update = UpdateFrom(dto);
        update.IncompatibleTemperatureIds = [.. incompatible];
        return update;
    }
}
