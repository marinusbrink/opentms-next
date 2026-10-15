using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Features;
using OpenTms.MasterData;
using OpenTms.MasterData.Reference;
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
/// ProductRelationCodeAppService and IProductRelationCodeResolver on SQLite (design 48). Risk class: High for the
/// target-required rule, the normalised-code uniqueness, the list with destination city, the in-use refusals in the
/// Relation / Product / TransportUnit deletes and the resolver (normalised match, archived excluded, never creates,
/// at most 500 codes); Critical for the tenant isolation and the feature gate (PostgreSQL proof in
/// VehicleTenantIsolationTests).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class ProductRelationCodeAppServiceTests : MasterDataAppServiceTestBase
{
    /// <summary>A fresh instance per call, as a request resolves it (the service caches IQueryables; see the finding test).</summary>
    private IProductRelationCodeAppService Service => GetRequiredService<IProductRelationCodeAppService>();
    private readonly IProductRelationCodeResolver _resolver;
    private readonly IRelationAppService _relations;
    private readonly IProductAppService _products;
    private readonly ITransportUnitAppService _units;
    private readonly ICountryAppService _countries;
    private readonly IAddressTypeAppService _addressTypes;
    private readonly IRepository<ProductRelationCode, Guid> _codeRows;

    private Guid _destination;
    private Guid _product;
    private Guid _unit;

    public ProductRelationCodeAppServiceTests()
    {
        _resolver = GetRequiredService<IProductRelationCodeResolver>();
        _relations = GetRequiredService<IRelationAppService>();
        _products = GetRequiredService<IProductAppService>();
        _units = GetRequiredService<ITransportUnitAppService>();
        _countries = GetRequiredService<ICountryAppService>();
        _addressTypes = GetRequiredService<IAddressTypeAppService>();
        _codeRows = GetRequiredService<IRepository<ProductRelationCode, Guid>>();
    }

    private async Task SeedAsync()
    {
        var nl = (await _countries.CreateAsync(new CountryCreateDto { IsoAlpha2 = "NL", IsoAlpha3 = "NLD", Names = Names("Netherlands") })).Id;
        var visiting = (await _addressTypes.CreateAsync(new AddressTypeCreateDto { Kind = AddressTypeKind.Visiting, Names = Names("Visiting address") })).Id;
        var postal = (await _addressTypes.CreateAsync(new AddressTypeCreateDto { Kind = AddressTypeKind.Postal, Names = Names("Postal address") })).Id;
        _destination = (await _relations.CreateAsync(new RelationCreateDto
        {
            Code = "DC", Name = "Distribution Centre", Roles = RelationRoles.Customer,
            Addresses =
            [
                new RelationAddressDto { AddressTypeId = postal, CountryId = nl, Street = "Postbus", PostalCode = "1000 AA", City = "Amsterdam" },
                new RelationAddressDto { AddressTypeId = visiting, CountryId = nl, Street = "Main", PostalCode = "3511 AA", City = "Utrecht" },
            ]
        })).Id;
        _product = (await _products.CreateAsync(new ProductCreateDto { Name = "Bananas" })).Id;
        _unit = (await _units.CreateAsync(new TransportUnitCreateDto { Name = "Euro pallet" })).Id;
    }

    private ProductRelationCodeCreateDto Dto(string code, bool relation = true, bool product = true, bool unit = true, Action<ProductRelationCodeCreateDto>? configure = null)
    {
        var dto = new ProductRelationCodeCreateDto
        {
            Code = code, RelationId = relation ? _destination : null, ProductId = product ? _product : null, TransportUnitId = unit ? _unit : null
        };
        configure?.Invoke(dto);
        return dto;
    }

    /// <summary>The resolver runs in its caller's unit of work (the Orders import's app service), as IRelationMatcher.</summary>
    private Task<System.Collections.Generic.List<ProductRelationCodeMatchDto>> ResolveAsync(System.Collections.Generic.IReadOnlyCollection<string> codes)
        => WithUnitOfWorkAsync(() => _resolver.ResolveAsync(codes));

    private static ProductRelationCodeUpdateDto UpdateFrom(ProductRelationCodeDto dto, Action<ProductRelationCodeUpdateDto>? configure = null)
    {
        var update = new ProductRelationCodeUpdateDto
        {
            Code = dto.Code, RelationId = dto.RelationId, ProductId = dto.ProductId, TransportUnitId = dto.TransportUnitId, IsArchived = dto.IsArchived,
            Description = dto.Description, ConcurrencyStamp = dto.ConcurrencyStamp
        };
        configure?.Invoke(update);
        return update;
    }

    private static ArchivableListRequestDto Request(string? search = null, string? sortBy = null, string? sortDir = null, bool includeArchived = false)
    {
        var request = List<ArchivableListRequestDto>(search: search, sortBy: sortBy, sortDir: sortDir);
        request.IncludeArchived = includeArchived;
        return request;
    }

    private static async Task<BusinessException> ShouldFailAsync(Func<Task> action, string code, string? field = null)
    {
        var ex = await Should.ThrowAsync<BusinessException>(action);
        ex.Code.ShouldBe(code);
        if (field != null)
        {
            ex.Data["field"].ShouldBe(field);
        }

        return ex;
    }

    // ── CRUD and rules (High) ────────────────────────────────────────────────

    [Fact]
    public async Task Create_get_list_and_lookup_round_trip_with_the_destination_city_product_and_unit()
    {
        using var _ = InTenantA();
        await SeedAsync();

        var created = await Service.CreateAsync(Dto(" art-4711 ", configure: d => d.Description = "yellow"));
        created.Code.ShouldBe("art-4711", "stored as typed, trimmed");
        created.RelationId.ShouldBe(_destination);
        created.RelationCode.ShouldBe("DC");
        created.RelationName.ShouldBe("Distribution Centre");
        created.RelationCity.ShouldBe("Utrecht", "the visiting address's city, as the relation list");
        created.ProductName.ShouldBe("Bananas");
        created.TransportUnitName.ShouldBe("Euro pallet");
        created.Description.ShouldBe("yellow");

        var row = (await Service.GetListAsync(Request())).Rows.ShouldHaveSingleItem();
        row.Code.ShouldBe("art-4711");
        row.RelationName.ShouldBe("Distribution Centre");
        row.RelationCity.ShouldBe("Utrecht");
        row.ProductName.ShouldBe("Bananas");
        row.TransportUnitName.ShouldBe("Euro pallet");

        var lookup = (await Service.GetLookupAsync()).ShouldHaveSingleItem();
        lookup.Name.ShouldBe("art-4711");
        lookup.Code.ShouldBe("ART-4711");
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task One_target_of_destination_product_and_transport_unit_is_enough(bool relation, bool product, bool unit)
    {
        using var _ = InTenantA();
        await SeedAsync();

        var created = await Service.CreateAsync(Dto("C1", relation, product, unit));
        (created.RelationId != null).ShouldBe(relation);
        (created.ProductId != null).ShouldBe(product);
        (created.TransportUnitId != null).ShouldBe(unit);
    }

    [Fact]
    public async Task A_code_that_maps_to_nothing_is_refused_on_create_and_update()
    {
        using var _ = InTenantA();
        await SeedAsync();

        await ShouldFailAsync(() => Service.CreateAsync(Dto("C1", false, false, false)), MasterDataErrorCodes.ProductRelationCodeTargetRequired);
        var created = await Service.CreateAsync(Dto("C1"));
        await ShouldFailAsync(() => Service.UpdateAsync(created.Id, UpdateFrom(created, u => { u.RelationId = null; u.ProductId = null; u.TransportUnitId = null; })), MasterDataErrorCodes.ProductRelationCodeTargetRequired);
        (await Service.GetAsync(created.Id)).ProductId.ShouldBe(_product);
    }

    [Fact]
    public async Task The_code_is_unique_per_tenant_regardless_of_case_and_surrounding_spaces()
    {
        using var _ = InTenantA();
        await SeedAsync();
        var first = await Service.CreateAsync(Dto("Art-4711"));

        await ShouldFailAsync(() => Service.CreateAsync(Dto(" ART-4711 ")), MasterDataErrorCodes.DuplicateKey, "code");
        var second = await Service.CreateAsync(Dto("Art-4712"));
        await ShouldFailAsync(() => Service.UpdateAsync(second.Id, UpdateFrom(second, u => u.Code = "art-4711")), MasterDataErrorCodes.DuplicateKey, "code");

        /* Changing only the case of its own code is allowed. */
        (await Service.UpdateAsync(first.Id, UpdateFrom(first, u => u.Code = "ART-4711"))).Code.ShouldBe("ART-4711");

        /* A soft delete frees the code. */
        await Service.DeleteAsync(first.Id);
        (await Service.CreateAsync(Dto("art-4711"))).Code.ShouldBe("art-4711");
    }

    [Fact]
    public async Task An_unknown_destination_product_or_transport_unit_is_refused()
    {
        using var _ = InTenantA();
        await SeedAsync();

        await ShouldFailAsync(() => Service.CreateAsync(Dto("C1", configure: d => d.RelationId = Guid.NewGuid())), MasterDataErrorCodes.ReferenceNotFound, "relationId");
        await ShouldFailAsync(() => Service.CreateAsync(Dto("C1", configure: d => d.ProductId = Guid.NewGuid())), MasterDataErrorCodes.ReferenceNotFound, "productId");
        await ShouldFailAsync(() => Service.CreateAsync(Dto("C1", configure: d => d.TransportUnitId = Guid.NewGuid())), MasterDataErrorCodes.ReferenceNotFound, "transportUnitId");
    }

    [Fact]
    public async Task List_searches_code_destination_and_product_and_sorts_on_the_whitelist()
    {
        using var _ = InTenantA();
        await SeedAsync();
        var apples = (await _products.CreateAsync(new ProductCreateDto { Name = "Apples" })).Id;
        var a = await Service.CreateAsync(Dto("B-100", relation: false, unit: false, configure: d => d.ProductId = apples));
        var b = await Service.CreateAsync(Dto("a-200", unit: false));
        var archived = await Service.CreateAsync(Dto("C-300", configure: d => d.IsArchived = true));

        (await Service.GetListAsync(Request())).TotalCount.ShouldBe(2);
        (await Service.GetListAsync(Request(includeArchived: true))).TotalCount.ShouldBe(3);
        (await Service.GetListAsync(Request(search: "b-1"))).Rows.ShouldHaveSingleItem().Id.ShouldBe(a.Id);
        (await Service.GetListAsync(Request(search: "distribution"))).Rows.ShouldHaveSingleItem().Id.ShouldBe(b.Id);
        (await Service.GetListAsync(Request(search: "apple"))).Rows.ShouldHaveSingleItem().Id.ShouldBe(a.Id);

        (await Service.GetListAsync(Request())).Rows.Select(r => r.Code).ShouldBe(["a-200", "B-100"], "ordered by the normalised code");
        (await Service.GetListAsync(Request(sortBy: "code", sortDir: "desc"))).Rows.Select(r => r.Code).ShouldBe(["B-100", "a-200"]);
        (await Service.GetListAsync(Request(sortBy: "productName"))).Rows.Select(r => r.Code).ShouldBe(["B-100", "a-200"]);
        (await Service.GetListAsync(Request(sortBy: "relationName"))).Rows.Count.ShouldBe(2);
        (await Service.GetListAsync(Request(sortBy: "transportUnitName"))).Rows.Count.ShouldBe(2);
        await ShouldFailAsync(() => Service.GetListAsync(Request(sortBy: "description")), MasterDataErrorCodes.UnknownSortColumn);
        (await Service.GetLookupAsync()).Select(l => l.Id).ShouldNotContain(archived.Id);
    }

    [Fact]
    public async Task Archive_unarchive_and_bulk_delete_are_idempotent()
    {
        using var _ = InTenantA();
        await SeedAsync();
        var code = await Service.CreateAsync(Dto("C1"));
        var other = await Service.CreateAsync(Dto("C2"));

        await Service.ArchiveAsync(code.Id);
        await Service.ArchiveAsync(code.Id);
        (await Service.GetAsync(code.Id)).IsArchived.ShouldBeTrue();
        await Service.UnarchiveAsync(code.Id);
        await Service.UnarchiveAsync(code.Id);
        (await Service.GetAsync(code.Id)).IsArchived.ShouldBeFalse();

        (await Service.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = Explicit(code.Id, other.Id) })).DeletedCount.ShouldBe(2);
        (await Service.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = Explicit(code.Id, other.Id) })).DeletedCount.ShouldBe(0);
    }

    // ── In use (High) ────────────────────────────────────────────────────────

    [Fact]
    public async Task A_destination_product_or_transport_unit_used_by_a_code_cannot_be_deleted()
    {
        using var _ = InTenantA();
        await SeedAsync();
        var code = await Service.CreateAsync(Dto("C1"));

        await ShouldFailAsync(() => _relations.DeleteAsync(_destination), MasterDataErrorCodes.InUse);
        await ShouldFailAsync(() => _products.DeleteAsync(_product), MasterDataErrorCodes.InUse);
        await ShouldFailAsync(() => _units.DeleteAsync(_unit), MasterDataErrorCodes.InUse);
        (await _products.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = Explicit(_product) })).SkippedRows.ShouldHaveSingleItem();
        (await _units.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = Explicit(_unit) })).SkippedRows.ShouldHaveSingleItem();

        await Service.DeleteAsync(code.Id);
        await _relations.DeleteAsync(_destination);
        await _products.DeleteAsync(_product);
        await _units.DeleteAsync(_unit);
    }

    // ── Resolver (High; the published interface the Orders import calls) ─────

    [Fact]
    public async Task The_resolver_matches_on_the_normalised_code_and_returns_what_the_code_maps_to()
    {
        using var _ = InTenantA();
        await SeedAsync();
        var code = await Service.CreateAsync(Dto("Art-4711"));
        await Service.CreateAsync(Dto("Art-4712", relation: false, unit: false));

        var matches = await ResolveAsync([" art-4711 ", "ART-4711", "unknown", "", "   "]);
        var match = matches.ShouldHaveSingleItem("the same normalised code twice is one match; unknown and blank codes are absent");
        match.Id.ShouldBe(code.Id);
        match.Code.ShouldBe("Art-4711");
        match.NormalizedCode.ShouldBe("ART-4711");
        match.RelationId.ShouldBe(_destination);
        match.ProductId.ShouldBe(_product);
        match.TransportUnitId.ShouldBe(_unit);

        (await ResolveAsync(["art-4711", "art-4712"])).Select(m => m.Code).ShouldBe(["Art-4711", "Art-4712"], ignoreOrder: true);
        (await ResolveAsync([])).ShouldBeEmpty();
        (await ResolveAsync(["art-47"])).ShouldBeEmpty("an exact match on the normalised code, not a contains search");
    }

    [Fact]
    public async Task The_resolver_excludes_archived_and_deleted_codes_and_never_creates_one()
    {
        using var _ = InTenantA();
        await SeedAsync();
        var archived = await Service.CreateAsync(Dto("ARCH-1"));
        await Service.ArchiveAsync(archived.Id);
        var deleted = await Service.CreateAsync(Dto("DEL-1"));
        await Service.DeleteAsync(deleted.Id);

        (await ResolveAsync(["arch-1", "del-1", "new-1"])).ShouldBeEmpty();
        (await WithUnitOfWorkAsync(() => _codeRows.CountAsync())).ShouldBe(1, "no find-or-create (§3.15): the unknown code was not created");

        await Service.UnarchiveAsync(archived.Id);
        (await ResolveAsync(["arch-1"])).ShouldHaveSingleItem().Id.ShouldBe(archived.Id);
    }

    [Fact]
    public async Task The_resolver_takes_at_most_500_distinct_codes_per_call()
    {
        using var _ = InTenantA();
        await SeedAsync();
        await Service.CreateAsync(Dto("CODE-0"));

        (await ResolveAsync(Enumerable.Range(0, MasterDataConsts.MaxResolverCodes).Select(i => $"code-{i}").ToList())).ShouldHaveSingleItem();
        await Should.ThrowAsync<AbpValidationException>(() => ResolveAsync(Enumerable.Range(0, MasterDataConsts.MaxResolverCodes + 1).Select(i => $"code-{i}").ToList()));
        MasterDataConsts.MaxResolverCodes.ShouldBe(500);
    }

    [Fact]
    public async Task One_service_instance_serves_two_searched_list_calls_in_separate_units_of_work()
    {
        /* FINDING: ProductRelationCodeAppService keeps the IQueryables of the first call's DbContext in
         * _relationQuery/_productQuery/_transportUnitQuery (PrepareQueriesAsync, "??="); a second searched or
         * name-sorted call on the same instance in another unit of work throws "Cannot use multiple context instances
         * within a single query execution". */
        using var _ = InTenantA();
        await SeedAsync();
        await Service.CreateAsync(Dto("C1"));
        var service = GetRequiredService<IProductRelationCodeAppService>();

        (await service.GetListAsync(Request(search: "c1"))).Rows.ShouldHaveSingleItem();
        (await service.GetListAsync(Request(search: "c1"))).Rows.ShouldHaveSingleItem();
        (await service.GetListAsync(Request(sortBy: "productName"))).Rows.ShouldHaveSingleItem();
        (await service.GetLookupAsync("c1")).ShouldHaveSingleItem();
    }

    // ── Tenant isolation (Critical) ───────────────────────────────────────────

    [Fact]
    public async Task Rows_of_tenant_A_are_invisible_to_tenant_B_through_the_service_and_the_resolver_and_its_references_cannot_be_borrowed()
    {
        ProductRelationCodeDto code;
        Guid destinationA, productA, unitA;
        using (InTenantA())
        {
            await SeedAsync();
            code = await Service.CreateAsync(Dto("PROBE-1"));
            (destinationA, productA, unitA) = (_destination, _product, _unit);
        }

        using (InTenantB())
        {
            (await Service.GetListAsync(Request(includeArchived: true))).TotalCount.ShouldBe(0);
            (await Service.GetListAsync(Request(search: "probe"))).Rows.ShouldBeEmpty();
            (await Service.GetListAsync(Request(search: "distribution"))).Rows.ShouldBeEmpty();
            (await Service.GetLookupAsync()).ShouldBeEmpty();
            (await ResolveAsync(["probe-1"])).ShouldBeEmpty();
            await Should.ThrowAsync<EntityNotFoundException>(() => Service.GetAsync(code.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => Service.UpdateAsync(code.Id, UpdateFrom(code)));
            await Should.ThrowAsync<EntityNotFoundException>(() => Service.ArchiveAsync(code.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => Service.UnarchiveAsync(code.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => Service.DeleteAsync(code.Id));
            (await Service.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = Explicit(code.Id) })).DeletedCount.ShouldBe(0);
            (await Service.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = FilterBased(), IncludeArchived = true })).DeletedCount.ShouldBe(0);

            await ShouldFailAsync(() => Service.CreateAsync(new ProductRelationCodeCreateDto { Code = "X", RelationId = destinationA }), MasterDataErrorCodes.ReferenceNotFound, "relationId");
            await ShouldFailAsync(() => Service.CreateAsync(new ProductRelationCodeCreateDto { Code = "X", ProductId = productA }), MasterDataErrorCodes.ReferenceNotFound, "productId");
            await ShouldFailAsync(() => Service.CreateAsync(new ProductRelationCodeCreateDto { Code = "X", TransportUnitId = unitA }), MasterDataErrorCodes.ReferenceNotFound, "transportUnitId");

            /* Tenant B has its own key space and its own resolver result. */
            await SeedAsync();
            var own = await Service.CreateAsync(Dto("PROBE-1"));
            own.Id.ShouldNotBe(code.Id);
            (await ResolveAsync(["probe-1"])).ShouldHaveSingleItem().Id.ShouldBe(own.Id);
        }

        using (InTenantA())
        {
            (await ResolveAsync(["probe-1"])).ShouldHaveSingleItem().Id.ShouldBe(code.Id);
            (await Service.GetListAsync(Request())).Rows.ShouldHaveSingleItem().Id.ShouldBe(code.Id);
        }
    }

    // ── Feature gate (Critical) ──────────────────────────────────────────────

    [Fact]
    public async Task With_the_ProductRelationCodes_node_off_the_screen_is_refused_but_the_lookup_and_the_resolver_work()
    {
        using var _ = InTenantA();
        await SeedAsync();
        var code = await Service.CreateAsync(Dto("C1"));

        foreach (var feature in new[] { AppFeatures.MasterData.Transport.ProductRelationCodes, AppFeatures.MasterData.Transport.Default })
        {
            using (TestFeatureValueProvider.Override(feature, "false"))
            {
                await Should.ThrowAsync<AbpAuthorizationException>(() => Service.GetListAsync(Request()));
                await Should.ThrowAsync<AbpAuthorizationException>(() => Service.GetAsync(code.Id));
                await Should.ThrowAsync<AbpAuthorizationException>(() => Service.CreateAsync(Dto("C2")));
                await Should.ThrowAsync<AbpAuthorizationException>(() => Service.UpdateAsync(code.Id, UpdateFrom(code, u => u.Code = "C3")));
                await Should.ThrowAsync<AbpAuthorizationException>(() => Service.DeleteAsync(code.Id));
                await Should.ThrowAsync<AbpAuthorizationException>(() => Service.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = Explicit(code.Id) }));
                await Should.ThrowAsync<AbpAuthorizationException>(() => Service.ArchiveAsync(code.Id));
                await Should.ThrowAsync<AbpAuthorizationException>(() => Service.UnarchiveAsync(code.Id));

                /* The picker and the resolver need only the app (design 48: "not the resolver"). */
                (await Service.GetLookupAsync()).ShouldHaveSingleItem();
                (await ResolveAsync(["c1"])).ShouldHaveSingleItem();
            }
        }

        using (TestFeatureValueProvider.Override(AppFeatures.MasterData.Default, "false"))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => Service.GetLookupAsync());
            await Should.ThrowAsync<AbpAuthorizationException>(() => ResolveAsync(["c1"]));
        }

        (await Service.GetAsync(code.Id)).Code.ShouldBe("C1");
    }
}
