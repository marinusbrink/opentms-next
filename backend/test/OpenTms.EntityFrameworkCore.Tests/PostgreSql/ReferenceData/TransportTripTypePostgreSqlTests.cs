using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.PlanningExecution.Seed;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.Localization;
using OpenTms.Platform.NumberSequences;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Xunit;

namespace OpenTms.PostgreSql.ReferenceData;

/// <summary>
/// Transport trip types against real PostgreSQL (design 53): the seed the DbMigrator ran for tenant A (own database)
/// and the shared tenant (host database), tenant isolation, the unique code and the one-default index.
/// Risk class: Critical.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class TransportTripTypePostgreSqlTests : ReferenceDataPostgreSqlTestBase
{
    private readonly IRepository<TransportTripType, Guid> _types;
    private readonly ITransportTripTypeAppService _service;
    private readonly PlanningExecutionDataSeedContributor _seed;
    private readonly IGuidGenerator _guids;

    public TransportTripTypePostgreSqlTests()
    {
        _types = GetRequiredService<IRepository<TransportTripType, Guid>>();
        _service = GetRequiredService<ITransportTripTypeAppService>();
        _seed = GetRequiredService<PlanningExecutionDataSeedContributor>();
        _guids = GetRequiredService<IGuidGenerator>();
    }

    private TransportTripType New(Guid tenantId, string code)
        => new(_guids.Create(), tenantId, code, LocalizedName.Create(new Dictionary<string, string?> { ["en"] = code }), NumberSequenceFormat.Create("T", false, false, "", 5));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Every_tenant_has_the_seeded_default_trip_type_and_seeding_again_changes_nothing(bool ownDatabase)
    {
        var tenantId = ownDatabase ? TenantA : SharedTenant;
        await InRolledBackTransactionAsync(tenantId, async () =>
        {
            var seeded = (await _types.GetListAsync()).ShouldHaveSingleItem();
            seeded.Code.ShouldBe("STD");
            seeded.IsDefault.ShouldBeTrue();
            seeded.TenantId.ShouldBe(tenantId);
            seeded.Name.Resolve("nl").ShouldBe("Standaard");
            seeded.Numbering.ShouldBe(NumberSequenceFormat.Create("TR", true, false, "-", 5));

            await _seed.SeedAsync(new DataSeedContext(tenantId));
            await FlushAsync();
            (await _types.GetCountAsync()).ShouldBe(1);
        });
    }

    [Fact]
    public async Task A_tenant_never_sees_another_tenants_trip_types()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            await _service.CreateAsync(new TransportTripTypeCreateDto { Code = "ONLYA", Names = Names("Only A"), Numbering = Numbering() });
            (await _service.GetListAsync(List())).Rows.Select(r => r.Code).ShouldBe(["ONLYA", "STD"], ignoreOrder: true);
        });

        await InRolledBackTransactionAsync(SharedTenant, async () =>
        {
            (await _service.GetListAsync(List())).Rows.Select(r => r.Code).ShouldBe(["STD"]);
            (await _types.GetListAsync()).ShouldAllBe(t => t.TenantId == SharedTenant);
        });
    }

    [Fact]
    public async Task A_duplicate_code_is_refused_by_the_index()
    {
        await InRolledBackTransactionAsync(TenantA, () => ShouldViolateUniqueIndexAsync(
            () => _types.InsertAsync(New(TenantA, "STD"), autoSave: true),
            "IX_TransportTripTypes_TenantId_Code"));
    }

    [Fact]
    public async Task A_second_default_is_refused_by_the_index()
    {
        await InRolledBackTransactionAsync(TenantA, () => ShouldViolateUniqueIndexAsync(
            async () =>
            {
                var second = New(TenantA, "SECOND");
                second.SetDefault(true);
                await _types.InsertAsync(second, autoSave: true);
            },
            "IX_TransportTripTypes_TenantId_Default"));
    }

    [Fact]
    public async Task Set_default_moves_the_default_without_tripping_the_index()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var charter = await _service.CreateAsync(new TransportTripTypeCreateDto { Code = "CHT", Names = Names("Charter"), Numbering = Numbering() });
            await _service.SetDefaultAsync(charter.Id);
            (await _types.GetListAsync(t => t.IsDefault)).ShouldHaveSingleItem().Code.ShouldBe("CHT");
        });
    }
}
