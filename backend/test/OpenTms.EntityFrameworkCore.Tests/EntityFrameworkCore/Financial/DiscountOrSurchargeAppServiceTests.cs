using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Financial;
using OpenTms.Financial.Invoicing;
using OpenTms.Financial.Tariffs;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Financial;

/// <summary>
/// DiscountOrSurchargeAppService on SQLite (design 52). Risk class: Critical for the validity rows (§3.4); High for the
/// currency rules per value kind, the days of the week, the regions saved with the parent on create (the OpenTMS bug,
/// §6) and the Master Data references checked through the published lookup.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class DiscountOrSurchargeAppServiceTests : FinancialAppServiceTestBase
{
    private readonly IDiscountOrSurchargeAppService _service;

    public DiscountOrSurchargeAppServiceTests()
    {
        _service = GetRequiredService<IDiscountOrSurchargeAppService>();
    }

    private static DiscountOrSurchargeValueInputDto Value(DateOnly validFrom, decimal value, string? currency = null)
        => new() { ValidFrom = validFrom, Value = value, CurrencyCode = currency };

    private async Task SeedMasterDataAsync()
    {
        await CurrencyAsync("EUR");
        await CurrencyAsync("USD", active: false);
    }

    private Task<DiscountOrSurchargeDto> CreateAsync(
        string code,
        DiscountOrSurchargeValueKind kind = DiscountOrSurchargeValueKind.FixedAmount,
        DaysOfWeek days = DaysOfWeek.All,
        DiscountOrSurchargeRegionInputDto[]? regions = null,
        params DiscountOrSurchargeValueInputDto[] values)
        => _service.CreateAsync(new DiscountOrSurchargeCreateDto
        {
            Code = code,
            Name = code + " surcharge",
            ValueKind = kind,
            Side = DiscountOrSurchargeSide.Both,
            DaysOfWeek = days,
            Values = values.Length == 0 ? [Value(new DateOnly(2026, 1, 1), 25m, "EUR")] : [.. values],
            Regions = [.. regions ?? []]
        });

    private static DiscountOrSurchargeUpdateDto UpdateFrom(DiscountOrSurchargeDto dto) => new()
    {
        Code = dto.Code,
        Name = dto.Name,
        Description = dto.Description,
        Direction = dto.Direction,
        ValueKind = dto.ValueKind,
        Side = dto.Side,
        DaysOfWeek = dto.DaysOfWeek,
        VatPercentageId = dto.VatPercentageId,
        LedgerAccount = dto.LedgerAccount,
        Values = dto.Values.Select(v => Value(v.ValidFrom, v.Value, v.CurrencyCode)).ToList(),
        Regions = dto.Regions.Select(r => new DiscountOrSurchargeRegionInputDto { GeographicalRegionId = r.GeographicalRegionId, AppliesAt = r.AppliesAt }).ToList(),
        IsArchived = dto.IsArchived,
        ConcurrencyStamp = dto.ConcurrencyStamp
    };

    [Fact]
    public async Task Regions_are_saved_with_a_new_surcharge()
    {
        using var _ = InTenantA();
        await SeedMasterDataAsync();
        var randstad = await RegionAsync("Randstad");
        var limburg = await RegionAsync("Limburg");

        var created = await CreateAsync("REGIO", regions:
        [
            new() { GeographicalRegionId = randstad, AppliesAt = RegionAppliesAt.Loading },
            new() { GeographicalRegionId = limburg, AppliesAt = RegionAppliesAt.Either }
        ]);

        /* Read back in a fresh request: the regions were written in the create's own unit of work. */
        var detail = await _service.GetAsync(created.Id);
        detail.Regions.Select(r => (r.Name, r.AppliesAt)).ShouldBe([("Limburg", RegionAppliesAt.Either), ("Randstad", RegionAppliesAt.Loading)]);
        (await _service.GetListAsync(List())).Rows.ShouldHaveSingleItem().RegionCount.ShouldBe(2);
    }

    [Fact]
    public async Task An_unknown_region_is_refused()
    {
        using var _ = InTenantA();
        await SeedMasterDataAsync();

        (await Should.ThrowAsync<BusinessException>(() => CreateAsync("REGIO", regions: [new() { GeographicalRegionId = Guid.NewGuid() }])))
            .Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
    }

    [Fact]
    public async Task The_currency_follows_the_value_kind_and_must_be_known_and_active()
    {
        using var _ = InTenantA();
        await SeedMasterDataAsync();

        (await Should.ThrowAsync<BusinessException>(() => CreateAsync("FIX", values: Value(Today, 25m)))).Code.ShouldBe(FinancialErrorCodes.CurrencyRequired);
        (await Should.ThrowAsync<BusinessException>(() => CreateAsync("PCT", DiscountOrSurchargeValueKind.Percentage, values: Value(Today, 12.5m, "EUR")))).Code.ShouldBe(FinancialErrorCodes.CurrencyNotAllowed);
        (await Should.ThrowAsync<BusinessException>(() => CreateAsync("GBP", values: Value(Today, 25m, "GBP")))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        (await Should.ThrowAsync<BusinessException>(() => CreateAsync("USD", values: Value(Today, 25m, "USD")))).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
        (await Should.ThrowAsync<BusinessException>(() => CreateAsync("PCT", DiscountOrSurchargeValueKind.Percentage, values: Value(Today, 1000.5m)))).Code.ShouldBe(FinancialErrorCodes.PercentageOutOfRange);

        var perKm = await CreateAsync("DIESEL", DiscountOrSurchargeValueKind.PerKilometre, values: Value(Today, 0.1275m, "eur"));
        perKm.Values.ShouldHaveSingleItem().CurrencyCode.ShouldBe("EUR");
        var row = (await _service.GetListAsync(List())).Rows.ShouldHaveSingleItem();
        row.CurrentValue.ShouldBe(0.1275m);
        row.CurrentCurrencyCode.ShouldBe("EUR");
    }

    [Fact]
    public async Task No_day_of_the_week_is_refused()
    {
        using var _ = InTenantA();
        await SeedMasterDataAsync();

        (await Should.ThrowAsync<BusinessException>(() => CreateAsync("NODAY", days: DaysOfWeek.None))).Code.ShouldBe(FinancialErrorCodes.DaysOfWeekRequired);
        (await CreateAsync("WKND", days: DaysOfWeek.Saturday | DaysOfWeek.Sunday)).DaysOfWeek.ShouldBe(DaysOfWeek.Saturday | DaysOfWeek.Sunday);
    }

    [Fact]
    public async Task A_monthly_diesel_surcharge_adds_future_values_and_keeps_its_history()
    {
        using var _ = InTenantA();
        await SeedMasterDataAsync();
        var created = await CreateAsync("DIESEL", DiscountOrSurchargeValueKind.Percentage,
            values: [Value(Today.AddMonths(-2), 10m), Value(Today.AddMonths(-1), 11.25m)]);

        var next = UpdateFrom(created);
        next.Values.Add(Value(Today.AddMonths(1), 12.5m));
        var updated = await _service.UpdateAsync(created.Id, next);
        updated.Values.Select(v => (v.Value, v.IsFixed)).ShouldBe([(10m, true), (11.25m, true), (12.5m, false)]);
        (await _service.GetListAsync(List())).Rows.Single().CurrentValue.ShouldBe(11.25m);

        var sameDate = UpdateFrom(updated);
        sameDate.Values.Add(Value(Today.AddMonths(1), 13m));
        (await Should.ThrowAsync<BusinessException>(() => _service.UpdateAsync(created.Id, sameDate))).Code.ShouldBe(FinancialErrorCodes.DuplicateValidFrom);

        var rewrite = UpdateFrom(updated);
        rewrite.Values[1].Value = 11m;
        (await Should.ThrowAsync<BusinessException>(() => _service.UpdateAsync(created.Id, rewrite))).Code.ShouldBe(FinancialErrorCodes.FixedValidityRowChanged);
    }

    [Fact]
    public async Task A_fixed_value_keeps_a_currency_deactivated_later()
    {
        using var _ = InTenantA();
        await CurrencyAsync("EUR");
        await CurrencyAsync("CHF");
        var created = await CreateAsync("FIX", values: Value(Today.AddDays(-1), 25m, "CHF"));

        var currencies = GetRequiredService<OpenTms.MasterData.Reference.ICurrencyAppService>();
        var chf = (await currencies.GetListAsync(new OpenTms.MasterData.Reference.CurrencyListRequestDto { StartRow = 0, EndRow = 10, WildcardSearch = "CHF" })).Rows.Single();
        var chfDto = await currencies.GetAsync(chf.Id);
        await currencies.UpdateAsync(chf.Id, new OpenTms.MasterData.Reference.CurrencyUpdateDto
        {
            IsoCode = chfDto.IsoCode, IsoNumeric = chfDto.IsoNumeric, MinorUnits = chfDto.MinorUnits, Symbol = chfDto.Symbol,
            Names = chfDto.Names, IsActive = false, ConcurrencyStamp = chfDto.ConcurrencyStamp
        });

        var rename = UpdateFrom(created);
        rename.Name = "Fixed surcharge";
        (await _service.UpdateAsync(created.Id, rename)).Name.ShouldBe("Fixed surcharge");

        var add = UpdateFrom(await _service.GetAsync(created.Id));
        add.Values.Add(Value(Today.AddDays(5), 30m, "CHF"));
        (await Should.ThrowAsync<BusinessException>(() => _service.UpdateAsync(created.Id, add))).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
    }

    [Fact]
    public async Task Regions_are_replaced_on_update()
    {
        using var _ = InTenantA();
        await SeedMasterDataAsync();
        var randstad = await RegionAsync("Randstad");
        var limburg = await RegionAsync("Limburg");
        var created = await CreateAsync("REGIO", regions: [new() { GeographicalRegionId = randstad }]);

        var change = UpdateFrom(created);
        change.Regions = [new() { GeographicalRegionId = limburg, AppliesAt = RegionAppliesAt.Unloading }];
        (await _service.UpdateAsync(created.Id, change)).Regions.ShouldHaveSingleItem().GeographicalRegionId.ShouldBe(limburg);

        var none = UpdateFrom(await _service.GetAsync(created.Id));
        none.Regions = [];
        (await _service.UpdateAsync(created.Id, none)).Regions.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_vat_category_is_refused_and_a_known_one_is_named()
    {
        using var _ = InTenantA();
        await SeedMasterDataAsync();
        await CountryAsync("NL", "NLD");
        var vat = await GetRequiredService<IVatPercentageAppService>().CreateAsync(new VatPercentageCreateDto
        {
            Code = "HIGH", Names = Names("High"), CountryCode = "NL", Rates = [new() { ValidFrom = new DateOnly(2001, 1, 1), Percentage = 21m }]
        });

        var input = new DiscountOrSurchargeCreateDto { Code = "X", Name = "X", VatPercentageId = Guid.NewGuid(), Values = [Value(Today, 1m, "EUR")] };
        (await Should.ThrowAsync<BusinessException>(() => _service.CreateAsync(input))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);

        input.VatPercentageId = vat.Id;
        (await _service.CreateAsync(input)).VatPercentageName.ShouldBe("High");
    }

    [Fact]
    public async Task Another_tenant_sees_nothing()
    {
        Guid id;
        using (InTenantA())
        {
            await SeedMasterDataAsync();
            id = (await CreateAsync("DIESEL")).Id;
        }

        using (InTenantB())
        {
            (await _service.GetListAsync(List())).TotalCount.ShouldBe(0);
            (await _service.GetLookupAsync()).ShouldBeEmpty();
            await Should.ThrowAsync<Volo.Abp.Domain.Entities.EntityNotFoundException>(() => _service.GetAsync(id));
        }
    }
}
