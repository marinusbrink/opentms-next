using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Features;
using OpenTms.MasterData;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Relations;
using OpenTms.MasterData.Transport;
using OpenTms.Platform.Features;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Validation;
using Xunit;

namespace OpenTms.EntityFrameworkCore.MasterData;

/// <summary>
/// VehicleCombinationAppService on SQLite (design 48, test risk analysis "Combination rules", High): the motorised
/// vehicle first, trailers after, 2 to 4 members, each vehicle once, one active combination per vehicle (an archived
/// or deleted combination frees its vehicles, unarchiving into a conflict is refused), the default name, the vehicle
/// in a combination that cannot be deleted. Tenant isolation and the feature gate (Critical) are covered here and
/// against PostgreSQL in VehicleTenantIsolationTests; the partial unique index itself in VehicleUniqueIndexTests.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class VehicleCombinationAppServiceTests : MasterDataAppServiceTestBase
{
    private readonly IVehicleCombinationAppService _service;
    private readonly IVehicleAppService _vehicles;
    private readonly IVehicleTypeAppService _types;
    private readonly IRelationAppService _relations;
    private readonly IRepository<VehicleCombinationMember> _memberRows;

    private readonly Dictionary<VehicleTypeKind, Guid> _typeOf = new();
    private Guid _owner;

    public VehicleCombinationAppServiceTests()
    {
        _service = GetRequiredService<IVehicleCombinationAppService>();
        _vehicles = GetRequiredService<IVehicleAppService>();
        _types = GetRequiredService<IVehicleTypeAppService>();
        _relations = GetRequiredService<IRelationAppService>();
        _memberRows = GetRequiredService<IRepository<VehicleCombinationMember>>();
    }

    // ── set-up ───────────────────────────────────────────────────────────────

    private async Task SeedAsync()
    {
        foreach (var kind in Enum.GetValues<VehicleTypeKind>())
        {
            _typeOf[kind] = (await _types.CreateAsync(new VehicleTypeCreateDto { Name = $"{kind} type", Kind = kind })).Id;
        }

        _owner = (await _relations.CreateAsync(new RelationCreateDto { Code = "OWN", Name = "Own Transport B.V.", Roles = RelationRoles.OwnCompany })).Id;
    }

    private async Task<Guid> VehicleAsync(string name, VehicleTypeKind kind)
        => (await _vehicles.CreateAsync(new VehicleCreateDto { Name = name, VehicleTypeId = _typeOf[kind], OwnerRelationId = _owner })).Id;

    private static VehicleCombinationCreateDto Dto(string? name, params Guid[] vehicleIds) => new()
    {
        Name = name,
        Members = vehicleIds.Select((id, index) => new VehicleCombinationMemberInputDto { VehicleId = id, Position = index + 1 }).ToList()
    };

    private static VehicleCombinationUpdateDto UpdateFrom(VehicleCombinationDto dto, Action<VehicleCombinationUpdateDto>? configure = null)
    {
        var update = new VehicleCombinationUpdateDto
        {
            Name = dto.Name, IsArchived = dto.IsArchived, Description = dto.Description,
            Members = dto.Members.Select(m => new VehicleCombinationMemberInputDto { VehicleId = m.VehicleId, Position = m.Position }).ToList(),
            ConcurrencyStamp = dto.ConcurrencyStamp
        };
        configure?.Invoke(update);
        return update;
    }

    private static ArchivableListRequestDto Request(string? search = null, bool includeArchived = false)
    {
        var request = List<ArchivableListRequestDto>(search: search);
        request.IncludeArchived = includeArchived;
        return request;
    }

    private static async Task<BusinessException> ShouldFailAsync(Func<Task> action, string code)
    {
        var ex = await Should.ThrowAsync<BusinessException>(action);
        ex.Code.ShouldBe(code);
        return ex;
    }

    // ── The member rules, table-driven (High) ────────────────────────────────

    [Theory]
    [InlineData("Tractor,Trailer", null)]
    [InlineData("Rigid,Trailer", null)]
    [InlineData("Van,Trailer", null)]
    [InlineData("Tractor,Trailer,Trailer", null)]
    [InlineData("Rigid,Trailer,Trailer,Trailer", null)]
    [InlineData("Trailer,Tractor", MasterDataErrorCodes.CombinationMotorisedFirst)]
    [InlineData("Trailer,Trailer", MasterDataErrorCodes.CombinationMotorisedFirst)]
    [InlineData("Tractor,Tractor", MasterDataErrorCodes.CombinationMotorisedFirst)]
    [InlineData("Tractor,Rigid", MasterDataErrorCodes.CombinationMotorisedFirst)]
    [InlineData("Tractor,Trailer,Van", MasterDataErrorCodes.CombinationMotorisedFirst)]
    [InlineData("Tractor", MasterDataErrorCodes.CombinationMemberCount)]
    [InlineData("", MasterDataErrorCodes.CombinationMemberCount)]
    public async Task The_first_member_is_motorised_every_later_one_a_trailer_and_a_combination_has_two_to_four(string kinds, string? error)
    {
        using var _ = InTenantA();
        await SeedAsync();
        var ids = new List<Guid>();
        var list = kinds.Length == 0 ? [] : kinds.Split(',').Select(Enum.Parse<VehicleTypeKind>).ToList();
        for (var i = 0; i < list.Count; i++)
        {
            ids.Add(await VehicleAsync($"V{i}", list[i]));
        }

        if (error == null)
        {
            var created = await _service.CreateAsync(Dto(null, [.. ids]));
            created.Members.Select(m => m.VehicleId).ShouldBe(ids);
            created.Members.Select(m => m.Position).ShouldBe(Enumerable.Range(1, ids.Count));
            created.Members.Select(m => m.Kind).ShouldBe(list);
        }
        else
        {
            await ShouldFailAsync(() => _service.CreateAsync(Dto("C", [.. ids])), error);
            (await _service.GetListAsync(Request(includeArchived: true))).TotalCount.ShouldBe(0);
            (await WithUnitOfWorkAsync(() => _memberRows.CountAsync())).ShouldBe(0);
        }
    }

    [Fact]
    public async Task Five_members_are_refused_by_the_position_range_or_by_the_member_count()
    {
        using var _ = InTenantA();
        await SeedAsync();
        var ids = new List<Guid> { await VehicleAsync("T", VehicleTypeKind.Tractor) };
        for (var i = 0; i < 4; i++)
        {
            ids.Add(await VehicleAsync($"TR{i}", VehicleTypeKind.Trailer));
        }

        /* Positions 1..5: the DTO's [Range(1, 4)] answers first (400). */
        await Should.ThrowAsync<AbpValidationException>(() => _service.CreateAsync(Dto("C", [.. ids])));

        /* Positions within range but five rows: the service's count rule. */
        await ShouldFailAsync(() => _service.CreateAsync(new VehicleCombinationCreateDto
        {
            Name = "C", Members = ids.Select((id, i) => new VehicleCombinationMemberInputDto { VehicleId = id, Position = Math.Min(i + 1, 4) }).ToList()
        }), MasterDataErrorCodes.CombinationMemberCount);
        (await WithUnitOfWorkAsync(() => _memberRows.CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task The_same_vehicle_twice_and_an_unknown_vehicle_are_refused()
    {
        using var _ = InTenantA();
        await SeedAsync();
        var tractor = await VehicleAsync("T1", VehicleTypeKind.Tractor);
        var trailer = await VehicleAsync("TR1", VehicleTypeKind.Trailer);

        var twice = await Should.ThrowAsync<BusinessException>(() => _service.CreateAsync(Dto("C", tractor, trailer, trailer)));
        new[] { MasterDataErrorCodes.DuplicateKey, MasterDataErrorCodes.ReferenceNotFound }.ShouldContain(twice.Code);
        await ShouldFailAsync(() => _service.CreateAsync(Dto("C", tractor, Guid.NewGuid())), MasterDataErrorCodes.ReferenceNotFound);
    }

    [Fact]
    public async Task Members_are_ordered_by_position_and_renumbered_and_the_default_name_joins_their_names()
    {
        using var _ = InTenantA();
        await SeedAsync();
        var tractor = await VehicleAsync("Tractor 01", VehicleTypeKind.Tractor);
        var trailer1 = await VehicleAsync("Trailer A", VehicleTypeKind.Trailer);
        var trailer2 = await VehicleAsync("Trailer B", VehicleTypeKind.Trailer);

        var created = await _service.CreateAsync(new VehicleCombinationCreateDto
        {
            Members =
            [
                new VehicleCombinationMemberInputDto { VehicleId = trailer2, Position = 4 },
                new VehicleCombinationMemberInputDto { VehicleId = tractor, Position = 1 },
                new VehicleCombinationMemberInputDto { VehicleId = trailer1, Position = 2 },
            ]
        });

        created.Name.ShouldBe("Tractor 01 + Trailer A + Trailer B");
        created.Members.Select(m => (m.VehicleId, m.Position)).ShouldBe([(tractor, 1), (trailer1, 2), (trailer2, 3)]);

        var row = (await _service.GetListAsync(Request())).Rows.ShouldHaveSingleItem();
        row.Members.ShouldBe("Tractor 01 + Trailer A + Trailer B");
        row.MotorisedVehicleName.ShouldBe("Tractor 01");

        /* The vehicle list shows the active combination. */
        (await _vehicles.GetListAsync(new VehicleListRequestDto())).Rows.ShouldAllBe(r => r.CombinationName == created.Name);
        (await _vehicles.GetAsync(tractor)).CombinationName.ShouldBe(created.Name);
    }

    [Fact]
    public async Task A_typed_name_is_kept_and_unique_per_tenant()
    {
        using var _ = InTenantA();
        await SeedAsync();
        var t1 = await VehicleAsync("T1", VehicleTypeKind.Tractor);
        var t2 = await VehicleAsync("T2", VehicleTypeKind.Tractor);
        var tr1 = await VehicleAsync("TR1", VehicleTypeKind.Trailer);
        var tr2 = await VehicleAsync("TR2", VehicleTypeKind.Trailer);

        (await _service.CreateAsync(Dto(" Night shift ", t1, tr1))).Name.ShouldBe("Night shift");
        var ex = await ShouldFailAsync(() => _service.CreateAsync(Dto("Night shift", t2, tr2)), MasterDataErrorCodes.DuplicateKey);
        ex.Data["field"].ShouldBe("name");
    }

    // ── One active combination per vehicle (High; the index on PostgreSQL) ───

    [Fact]
    public async Task A_vehicle_in_an_active_combination_cannot_join_another_one()
    {
        using var _ = InTenantA();
        await SeedAsync();
        var tractor = await VehicleAsync("T1", VehicleTypeKind.Tractor);
        var tractor2 = await VehicleAsync("T2", VehicleTypeKind.Tractor);
        var trailer = await VehicleAsync("TR1", VehicleTypeKind.Trailer);
        var trailer2 = await VehicleAsync("TR2", VehicleTypeKind.Trailer);
        await _service.CreateAsync(Dto("C1", tractor, trailer));

        var ex = await ShouldFailAsync(() => _service.CreateAsync(Dto("C2", tractor2, trailer)), MasterDataErrorCodes.VehicleAlreadyInCombination);
        ex.Data["name"].ShouldBe("TR1");
        await ShouldFailAsync(() => _service.CreateAsync(Dto("C2", tractor, trailer2)), MasterDataErrorCodes.VehicleAlreadyInCombination);

        /* An update that takes another combination's vehicle is refused too. */
        var c2 = await _service.CreateAsync(Dto("C2", tractor2, trailer2));
        await ShouldFailAsync(() => _service.UpdateAsync(c2.Id, UpdateFrom(c2, u => u.Members = [new() { VehicleId = tractor2, Position = 1 }, new() { VehicleId = trailer, Position = 2 }])), MasterDataErrorCodes.VehicleAlreadyInCombination);

        /* A combination keeps its own vehicles on update. */
        (await _service.UpdateAsync(c2.Id, UpdateFrom(c2, u => u.Description = "kept"))).Description.ShouldBe("kept");
    }

    [Fact]
    public async Task An_archived_combination_frees_its_vehicles_and_unarchiving_into_a_conflict_is_refused()
    {
        using var _ = InTenantA();
        await SeedAsync();
        var tractor = await VehicleAsync("T1", VehicleTypeKind.Tractor);
        var tractor2 = await VehicleAsync("T2", VehicleTypeKind.Tractor);
        var trailer = await VehicleAsync("TR1", VehicleTypeKind.Trailer);
        var c1 = await _service.CreateAsync(Dto("C1", tractor, trailer));

        /* Archive twice is one effect. */
        await _service.ArchiveAsync(c1.Id);
        await _service.ArchiveAsync(c1.Id);
        (await _service.GetAsync(c1.Id)).IsArchived.ShouldBeTrue();
        (await WithUnitOfWorkAsync(() => _memberRows.CountAsync(m => m.VehicleCombinationId == c1.Id && m.IsActive))).ShouldBe(0);
        (await _vehicles.GetAsync(trailer)).CombinationName.ShouldBeNull("an archived combination is not the vehicle's combination");
        (await _service.GetListAsync(Request())).TotalCount.ShouldBe(0);
        (await _service.GetLookupAsync()).ShouldBeEmpty();

        /* The trailer is free for another combination. */
        var c2 = await _service.CreateAsync(Dto("C2", tractor2, trailer));
        (await _vehicles.GetAsync(trailer)).CombinationName.ShouldBe("C2");

        /* Unarchiving C1 would put the trailer in two active combinations. */
        var ex = await ShouldFailAsync(() => _service.UnarchiveAsync(c1.Id), MasterDataErrorCodes.VehicleAlreadyInCombination);
        ex.Data["name"].ShouldBe("TR1");
        var archivedC1 = await _service.GetAsync(c1.Id);
        await ShouldFailAsync(() => _service.UpdateAsync(c1.Id, UpdateFrom(archivedC1, u => u.IsArchived = false)), MasterDataErrorCodes.VehicleAlreadyInCombination);
        (await _service.GetAsync(c1.Id)).IsArchived.ShouldBeTrue();

        /* With C2 archived, C1 comes back (twice is one effect). */
        await _service.ArchiveAsync(c2.Id);
        await _service.UnarchiveAsync(c1.Id);
        await _service.UnarchiveAsync(c1.Id);
        (await _service.GetAsync(c1.Id)).IsArchived.ShouldBeFalse();
        (await WithUnitOfWorkAsync(() => _memberRows.CountAsync(m => m.VehicleId == trailer && m.IsActive))).ShouldBe(1);
    }

    [Fact]
    public async Task A_combination_created_archived_does_not_claim_its_vehicles()
    {
        using var _ = InTenantA();
        await SeedAsync();
        var tractor = await VehicleAsync("T1", VehicleTypeKind.Tractor);
        var trailer = await VehicleAsync("TR1", VehicleTypeKind.Trailer);
        await _service.CreateAsync(Dto("Active", tractor, trailer));

        var archived = await _service.CreateAsync(new VehicleCombinationCreateDto
        {
            Name = "Archived", IsArchived = true,
            Members = [new() { VehicleId = tractor, Position = 1 }, new() { VehicleId = trailer, Position = 2 }]
        });
        archived.IsArchived.ShouldBeTrue();
        (await WithUnitOfWorkAsync(() => _memberRows.CountAsync(m => m.VehicleCombinationId == archived.Id && m.IsActive))).ShouldBe(0);
    }

    [Fact]
    public async Task A_vehicle_in_a_combination_cannot_be_deleted_until_the_combination_is_deleted()
    {
        using var _ = InTenantA();
        await SeedAsync();
        var tractor = await VehicleAsync("T1", VehicleTypeKind.Tractor);
        var trailer = await VehicleAsync("TR1", VehicleTypeKind.Trailer);
        var combination = await _service.CreateAsync(Dto("C1", tractor, trailer));

        var ex = await ShouldFailAsync(() => _vehicles.DeleteAsync(trailer), MasterDataErrorCodes.InUse);
        ex.Data["reason"].ShouldNotBeNull();
        (await _vehicles.BulkDeleteAsync(new VehicleBulkDeleteRequestDto { Selection = Explicit(tractor, trailer) })).SkippedRows.Count.ShouldBe(2);

        /* Also while the combination is archived (design 48: a vehicle in a combination is in use). */
        await _service.ArchiveAsync(combination.Id);
        await ShouldFailAsync(() => _vehicles.DeleteAsync(trailer), MasterDataErrorCodes.InUse);

        /* Deleting the combination removes its members, frees the vehicles; a second delete finds nothing. */
        await _service.DeleteAsync(combination.Id);
        (await WithUnitOfWorkAsync(() => _memberRows.CountAsync(m => m.VehicleCombinationId == combination.Id))).ShouldBe(0);
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.DeleteAsync(combination.Id));
        await _vehicles.DeleteAsync(trailer);
        (await _service.CreateAsync(Dto("C1", tractor, await VehicleAsync("TR2", VehicleTypeKind.Trailer)))).Name.ShouldBe("C1", "a deleted combination frees its name");
    }

    [Fact]
    public async Task Update_replaces_the_members_and_frees_the_dropped_vehicle()
    {
        using var _ = InTenantA();
        await SeedAsync();
        var tractor = await VehicleAsync("T1", VehicleTypeKind.Tractor);
        var trailer1 = await VehicleAsync("TR1", VehicleTypeKind.Trailer);
        var trailer2 = await VehicleAsync("TR2", VehicleTypeKind.Trailer);
        var created = await _service.CreateAsync(Dto("C1", tractor, trailer1));

        var updated = await _service.UpdateAsync(created.Id, UpdateFrom(created, u => u.Members = [new() { VehicleId = tractor, Position = 1 }, new() { VehicleId = trailer2, Position = 2 }]));
        updated.Members.Select(m => m.VehicleId).ShouldBe([tractor, trailer2]);
        (await WithUnitOfWorkAsync(() => _memberRows.CountAsync(m => m.VehicleCombinationId == created.Id))).ShouldBe(2);
        (await _vehicles.GetAsync(trailer1)).CombinationName.ShouldBeNull();
        await _vehicles.DeleteAsync(trailer1);

        /* The rules hold on update as well. */
        await ShouldFailAsync(() => _service.UpdateAsync(created.Id, UpdateFrom(updated, u => u.Members = [new() { VehicleId = trailer2, Position = 1 }, new() { VehicleId = tractor, Position = 2 }])), MasterDataErrorCodes.CombinationMotorisedFirst);
        await ShouldFailAsync(() => _service.UpdateAsync(created.Id, UpdateFrom(updated, u => u.Members = [new() { VehicleId = tractor, Position = 1 }])), MasterDataErrorCodes.CombinationMemberCount);
    }

    [Fact]
    public async Task Bulk_delete_frees_the_vehicles_and_a_second_bulk_delete_removes_nothing()
    {
        using var _ = InTenantA();
        await SeedAsync();
        var c1 = await _service.CreateAsync(Dto("C1", await VehicleAsync("T1", VehicleTypeKind.Tractor), await VehicleAsync("TR1", VehicleTypeKind.Trailer)));
        var c2 = await _service.CreateAsync(Dto("C2", await VehicleAsync("T2", VehicleTypeKind.Rigid), await VehicleAsync("TR2", VehicleTypeKind.Trailer)));

        (await _service.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = Explicit(c1.Id, c2.Id) })).DeletedCount.ShouldBe(2);
        (await _service.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = Explicit(c1.Id, c2.Id) })).DeletedCount.ShouldBe(0);
        (await WithUnitOfWorkAsync(() => _memberRows.CountAsync())).ShouldBe(0);
        (await _vehicles.BulkDeleteAsync(new VehicleBulkDeleteRequestDto { Selection = FilterBased() })).DeletedCount.ShouldBe(4);
    }

    // ── Tenant isolation (Critical) ───────────────────────────────────────────

    [Fact]
    public async Task Rows_of_tenant_A_are_invisible_and_untouchable_from_tenant_B_and_its_vehicles_cannot_be_borrowed()
    {
        VehicleCombinationDto combination;
        Guid tractorA, trailerA;
        using (InTenantA())
        {
            await SeedAsync();
            tractorA = await VehicleAsync("T1", VehicleTypeKind.Tractor);
            trailerA = await VehicleAsync("TR1", VehicleTypeKind.Trailer);
            combination = await _service.CreateAsync(Dto("Probe combination", tractorA, trailerA));
        }

        using (InTenantB())
        {
            (await _service.GetListAsync(Request(includeArchived: true))).TotalCount.ShouldBe(0);
            (await _service.GetListAsync(Request(search: "probe"))).Rows.ShouldBeEmpty();
            (await _service.GetLookupAsync()).ShouldBeEmpty();
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(combination.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.UpdateAsync(combination.Id, UpdateFrom(combination)));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.ArchiveAsync(combination.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.UnarchiveAsync(combination.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.DeleteAsync(combination.Id));
            (await _service.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = Explicit(combination.Id) })).DeletedCount.ShouldBe(0);
            (await _service.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = FilterBased(), IncludeArchived = true })).DeletedCount.ShouldBe(0);

            /* Tenant A's vehicles are not tenant B's: a combination over them is refused. */
            await ShouldFailAsync(() => _service.CreateAsync(Dto("Borrowed", tractorA, trailerA)), MasterDataErrorCodes.ReferenceNotFound);

            /* Tenant B has its own key space for the name. */
            await SeedAsync();
            var own = await _service.CreateAsync(Dto("Probe combination", await VehicleAsync("T1", VehicleTypeKind.Tractor), await VehicleAsync("TR1", VehicleTypeKind.Trailer)));
            own.Id.ShouldNotBe(combination.Id);
        }

        using (InTenantA())
        {
            var mine = await _service.GetAsync(combination.Id);
            mine.Members.Count.ShouldBe(2);
            mine.IsArchived.ShouldBeFalse();
            (await _service.GetListAsync(Request())).Rows.ShouldHaveSingleItem().Id.ShouldBe(combination.Id);
        }
    }

    // ── Feature gate (Critical) ──────────────────────────────────────────────

    [Fact]
    public async Task With_the_VehicleCombinations_node_off_every_screen_method_is_refused_but_the_lookup_works()
    {
        using var _ = InTenantA();
        await SeedAsync();
        var combination = await _service.CreateAsync(Dto("C1", await VehicleAsync("T1", VehicleTypeKind.Tractor), await VehicleAsync("TR1", VehicleTypeKind.Trailer)));

        foreach (var feature in new[] { AppFeatures.MasterData.General.Fleet.VehicleCombinations, AppFeatures.MasterData.General.Fleet.Default })
        {
            using (TestFeatureValueProvider.Override(feature, "false"))
            {
                await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetListAsync(Request()));
                await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetAsync(combination.Id));
                await Should.ThrowAsync<AbpAuthorizationException>(() => _service.CreateAsync(Dto("C2")));
                await Should.ThrowAsync<AbpAuthorizationException>(() => _service.UpdateAsync(combination.Id, UpdateFrom(combination, u => u.Name = "Changed")));
                await Should.ThrowAsync<AbpAuthorizationException>(() => _service.DeleteAsync(combination.Id));
                await Should.ThrowAsync<AbpAuthorizationException>(() => _service.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = Explicit(combination.Id) }));
                await Should.ThrowAsync<AbpAuthorizationException>(() => _service.ArchiveAsync(combination.Id));
                await Should.ThrowAsync<AbpAuthorizationException>(() => _service.UnarchiveAsync(combination.Id));
                (await _service.GetLookupAsync()).ShouldHaveSingleItem();
            }
        }

        using (TestFeatureValueProvider.Override(AppFeatures.MasterData.Default, "false"))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetLookupAsync());
        }

        var intact = await _service.GetAsync(combination.Id);
        intact.Name.ShouldBe("C1");
        intact.IsArchived.ShouldBeFalse();
    }
}
