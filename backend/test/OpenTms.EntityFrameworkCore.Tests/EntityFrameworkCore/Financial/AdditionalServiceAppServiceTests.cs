using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Financial.Invoicing;
using OpenTms.Financial.Tariffs;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Financial;

/// <summary>
/// AdditionalServiceAppService on SQLite (design 52). Risk class: High for the VAT reference, CRUD and isolation.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class AdditionalServiceAppServiceTests : FinancialAppServiceTestBase
{
    private readonly IAdditionalServiceAppService _service;

    public AdditionalServiceAppServiceTests()
    {
        _service = GetRequiredService<IAdditionalServiceAppService>();
    }

    private Task<AdditionalServiceDto> CreateAsync(string code, string name, ServiceUnit unit = ServiceUnit.Piece, Guid? vat = null, bool archived = false)
        => _service.CreateAsync(new AdditionalServiceCreateDto { Code = code, Name = name, Unit = unit, VatPercentageId = vat, IsArchived = archived });

    private async Task<Guid> VatAsync(string code, string name)
        => (await GetRequiredService<IVatPercentageAppService>().CreateAsync(new VatPercentageCreateDto
        {
            Code = code, Names = Names(name), CountryCode = "NL", Rates = [new() { ValidFrom = new DateOnly(2001, 1, 1), Percentage = 21m }]
        })).Id;

    [Fact]
    public async Task Create_list_sort_by_vat_and_update()
    {
        using var _ = InTenantA();
        await CountryAsync("NL", "NLD");
        var high = await VatAsync("HIGH", "High");
        var low = await VatAsync("LOW", "Low");

        var waiting = await CreateAsync(" wait ", "Waiting time", ServiceUnit.Hour, high);
        waiting.Code.ShouldBe("WAIT");
        waiting.VatPercentageName.ShouldBe("High");
        await CreateAsync("TAIL", "Tail lift", vat: low);
        await CreateAsync("CRANE", "Crane");
        await CreateAsync("OLD", "Old", archived: true);

        var list = await _service.GetListAsync(List());
        list.Rows.Select(r => r.Code).ShouldBe(["CRANE", "TAIL", "WAIT"]);
        list.Rows.Single(r => r.Code == "CRANE").VatPercentageName.ShouldBeNull("null = the tenant's default VAT");
        (await _service.GetListAsync(List(sortBy: "vatPercentageName", sortDir: "desc"))).Rows.First().Code.ShouldBe("TAIL");
        (await _service.GetListAsync(List(search: "lift"))).Rows.Single().Code.ShouldBe("TAIL");
        (await _service.GetListAsync(List(includeArchived: true))).TotalCount.ShouldBe(4);

        var update = new AdditionalServiceUpdateDto
        {
            Code = waiting.Code, Name = "Waiting", Unit = ServiceUnit.Hour, VatPercentageId = null,
            RevenueLedgerAccount = "8100", CostLedgerAccount = "7100", ConcurrencyStamp = waiting.ConcurrencyStamp
        };
        var updated = await _service.UpdateAsync(waiting.Id, update);
        updated.VatPercentageId.ShouldBeNull();
        updated.RevenueLedgerAccount.ShouldBe("8100");
    }

    [Fact]
    public async Task An_unknown_vat_category_or_a_duplicate_code_is_refused()
    {
        using var _ = InTenantA();
        (await Should.ThrowAsync<BusinessException>(() => CreateAsync("WAIT", "Waiting", vat: Guid.NewGuid()))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        await CreateAsync("WAIT", "Waiting");
        (await Should.ThrowAsync<BusinessException>(() => CreateAsync("wait", "Other"))).Code.ShouldBe(ReferenceDataErrorCodes.DuplicateKey);
    }

    [Fact]
    public async Task Delete_and_isolation()
    {
        Guid id;
        using (InTenantA())
        {
            id = (await CreateAsync("WAIT", "Waiting")).Id;
        }

        using (InTenantB())
        {
            (await _service.GetListAsync(List())).TotalCount.ShouldBe(0);
            await Should.ThrowAsync<Volo.Abp.Domain.Entities.EntityNotFoundException>(() => _service.GetAsync(id));
        }

        using (InTenantA())
        {
            await _service.DeleteAsync(id);
            (await _service.GetListAsync(List())).TotalCount.ShouldBe(0);
        }
    }
}
