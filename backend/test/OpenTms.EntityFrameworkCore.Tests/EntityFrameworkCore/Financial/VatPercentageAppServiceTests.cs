using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Financial;
using OpenTms.Financial.Invoicing;
using OpenTms.Financial.Tariffs;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Financial;

/// <summary>
/// VatPercentageAppService on SQLite (design 52). Risk class: Critical for the validity rows (§3.4: past rates fixed,
/// future rates editable, money history) and isolation; High for the country check, in-use and default refusals.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class VatPercentageAppServiceTests : FinancialAppServiceTestBase
{
    private readonly IVatPercentageAppService _service;
    private static readonly DateOnly Since2001 = new(2001, 1, 1);

    public VatPercentageAppServiceTests()
    {
        _service = GetRequiredService<IVatPercentageAppService>();
    }

    private static VatPercentageRateInputDto Rate(DateOnly validFrom, decimal percentage) => new() { ValidFrom = validFrom, Percentage = percentage };

    private Task<VatPercentageDto> CreateAsync(string code, string en, string? nl = null, VatKind kind = VatKind.Standard, string country = "NL", bool archived = false, params VatPercentageRateInputDto[] rates)
        => _service.CreateAsync(new VatPercentageCreateDto
        {
            Code = code,
            Names = Names(en, nl),
            Kind = kind,
            CountryCode = country,
            Rates = rates.Length == 0 ? [Rate(Since2001, 21m)] : [.. rates],
            IsArchived = archived
        });

    private static VatPercentageUpdateDto UpdateFrom(VatPercentageDto dto) => new()
    {
        Code = dto.Code,
        Names = Copy(dto.Names),
        Description = dto.Description,
        Kind = dto.Kind,
        CountryCode = dto.CountryCode,
        SalesLedgerAccount = dto.SalesLedgerAccount,
        PurchaseLedgerAccount = dto.PurchaseLedgerAccount,
        Rates = dto.Rates.Select(r => Rate(r.ValidFrom, r.Percentage)).ToList(),
        IsArchived = dto.IsArchived,
        ConcurrencyStamp = dto.ConcurrencyStamp
    };

    [Fact]
    public async Task Create_stores_the_category_with_its_rates_and_makes_the_first_the_default()
    {
        using var _ = InTenantA();
        using var culture = UiCulture("nl-NL");
        await CountryAsync("NL", "NLD");

        var created = await CreateAsync(" high ", "High", "Hoog", rates: [Rate(Since2001, 19m), Rate(new DateOnly(2012, 10, 1), 21m), Rate(Today.AddMonths(1), 22.5m)]);

        created.Code.ShouldBe("HIGH");
        created.Name.ShouldBe("Hoog");
        created.CountryCode.ShouldBe("NL");
        created.IsDefault.ShouldBeTrue("a tenant's first category becomes the default");
        created.CurrentPercentage.ShouldBe(21m);
        created.Rates.Select(r => (r.Percentage, r.IsFixed)).ShouldBe([(19m, true), (21m, true), (22.5m, false)]);

        var list = await _service.GetListAsync(List());
        list.Rows.ShouldHaveSingleItem().CurrentPercentage.ShouldBe(21m);
    }

    [Fact]
    public async Task An_unknown_country_is_refused()
    {
        using var _ = InTenantA();
        await CountryAsync("NL", "NLD");

        (await Should.ThrowAsync<BusinessException>(() => CreateAsync("BE21", "Belgium high", country: "BE"))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        (await Should.ThrowAsync<BusinessException>(() => CreateAsync("XX", "Bad", country: "N1"))).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
    }

    [Fact]
    public async Task A_future_rate_can_be_announced_changed_and_withdrawn()
    {
        using var _ = InTenantA();
        await CountryAsync("NL", "NLD");
        var created = await CreateAsync("HIGH", "High");

        var announce = UpdateFrom(created);
        announce.Rates.Add(Rate(Today.AddDays(30), 23m));
        var announced = await _service.UpdateAsync(created.Id, announce);
        announced.Rates.Count.ShouldBe(2);
        announced.CurrentPercentage.ShouldBe(21m);

        var change = UpdateFrom(announced);
        change.Rates[1].Percentage = 22m;
        (await _service.UpdateAsync(created.Id, change)).Rates.Last().Percentage.ShouldBe(22m);

        var withdraw = UpdateFrom(await _service.GetAsync(created.Id));
        withdraw.Rates.RemoveAt(1);
        (await _service.UpdateAsync(created.Id, withdraw)).Rates.ShouldHaveSingleItem().Percentage.ShouldBe(21m);
    }

    [Fact]
    public async Task A_rate_that_has_taken_effect_cannot_be_changed_removed_or_added()
    {
        using var _ = InTenantA();
        await CountryAsync("NL", "NLD");
        var created = await CreateAsync("HIGH", "High", rates: [Rate(Since2001, 19m), Rate(new DateOnly(2012, 10, 1), 21m)]);

        var change = UpdateFrom(created);
        change.Rates[0].Percentage = 20m;
        (await Should.ThrowAsync<BusinessException>(() => _service.UpdateAsync(created.Id, change))).Code.ShouldBe(FinancialErrorCodes.FixedValidityRowChanged);

        var remove = UpdateFrom(created);
        remove.Rates.RemoveAt(0);
        (await Should.ThrowAsync<BusinessException>(() => _service.UpdateAsync(created.Id, remove))).Code.ShouldBe(FinancialErrorCodes.FixedValidityRowChanged);

        var backdate = UpdateFrom(created);
        backdate.Rates.Add(Rate(Today, 22m));
        (await Should.ThrowAsync<BusinessException>(() => _service.UpdateAsync(created.Id, backdate))).Code.ShouldBe(FinancialErrorCodes.FixedValidityRowChanged);

        (await _service.GetAsync(created.Id)).Rates.Select(r => r.Percentage).ShouldBe([19m, 21m]);
    }

    [Fact]
    public async Task Rates_need_one_row_per_date_and_a_valid_percentage()
    {
        using var _ = InTenantA();
        await CountryAsync("NL", "NLD");

        (await Should.ThrowAsync<BusinessException>(() => _service.CreateAsync(new VatPercentageCreateDto { Code = "X", Names = Names("X"), CountryCode = "NL", Rates = [] })))
            .Code.ShouldBe(FinancialErrorCodes.ValidityRowRequired);
        (await Should.ThrowAsync<BusinessException>(() => CreateAsync("X", "X", rates: [Rate(Today, 9m), Rate(Today, 21m)]))).Code.ShouldBe(FinancialErrorCodes.DuplicateValidFrom);
        (await Should.ThrowAsync<BusinessException>(() => CreateAsync("X", "X", rates: [Rate(Today, 101m)]))).Code.ShouldBe(FinancialErrorCodes.PercentageOutOfRange);
    }

    [Fact]
    public async Task List_searches_sorts_and_hides_archived_rows()
    {
        using var _ = InTenantA();
        await CountryAsync("NL", "NLD");
        await CreateAsync("HIGH", "High");
        await CreateAsync("LOW", "Low", kind: VatKind.Reduced, rates: [Rate(Since2001, 9m)]);
        await CreateAsync("OLD", "Old", archived: true);

        (await _service.GetListAsync(List())).Rows.Select(r => r.Code).ShouldBe(["HIGH", "LOW"]);
        (await _service.GetListAsync(List(includeArchived: true))).TotalCount.ShouldBe(3);
        (await _service.GetListAsync(List(search: "low"))).Rows.Single().CurrentPercentage.ShouldBe(9m);
        (await _service.GetListAsync(List(sortBy: "kind", sortDir: "desc"))).Rows.First().Code.ShouldBe("LOW");
        (await _service.GetLookupAsync()).Select(l => l.Code).ShouldBe(["HIGH", "LOW"]);
        (await Should.ThrowAsync<BusinessException>(() => _service.GetListAsync(List(sortBy: "currentPercentage")))).Code.ShouldBe(ReferenceDataErrorCodes.UnknownSortColumn);
    }

    [Fact]
    public async Task The_default_moves_and_cannot_be_archived_or_deleted()
    {
        using var _ = InTenantA();
        await CountryAsync("NL", "NLD");
        var high = await CreateAsync("HIGH", "High");
        var low = await CreateAsync("LOW", "Low");

        var archive = UpdateFrom(high);
        archive.IsArchived = true;
        (await Should.ThrowAsync<BusinessException>(() => _service.UpdateAsync(high.Id, archive))).Code.ShouldBe(ReferenceDataErrorCodes.DefaultRequired);
        (await Should.ThrowAsync<BusinessException>(() => _service.DeleteAsync(high.Id))).Code.ShouldBe(ReferenceDataErrorCodes.DefaultRequired);

        await _service.SetDefaultAsync(low.Id);
        (await _service.GetAsync(high.Id)).IsDefault.ShouldBeFalse();
        await _service.DeleteAsync(high.Id);
        (await _service.GetListAsync(List())).Rows.ShouldHaveSingleItem().IsDefault.ShouldBeTrue();
    }

    [Fact]
    public async Task A_category_used_by_a_service_or_a_surcharge_is_not_deleted()
    {
        using var _ = InTenantA();
        await CountryAsync("NL", "NLD");
        await CreateAsync("HIGH", "High");
        var low = await CreateAsync("LOW", "Low");
        var zero = await CreateAsync("ZERO", "Zero");
        await GetRequiredService<IAdditionalServiceAppService>().CreateAsync(new AdditionalServiceCreateDto { Code = "WAIT", Name = "Waiting time", Unit = ServiceUnit.Hour, VatPercentageId = low.Id });
        await GetRequiredService<IDiscountOrSurchargeAppService>().CreateAsync(new DiscountOrSurchargeCreateDto
        {
            Code = "WKND", Name = "Weekend", ValueKind = DiscountOrSurchargeValueKind.Percentage, VatPercentageId = zero.Id,
            Values = [new DiscountOrSurchargeValueInputDto { ValidFrom = Today, Value = 10m }]
        });

        (await Should.ThrowAsync<BusinessException>(() => _service.DeleteAsync(low.Id))).Code.ShouldBe(ReferenceDataErrorCodes.InUse);
        (await Should.ThrowAsync<BusinessException>(() => _service.DeleteAsync(zero.Id))).Code.ShouldBe(ReferenceDataErrorCodes.InUse);

        var result = await _service.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = FilterBased() });
        result.DeletedCount.ShouldBe(0);
        result.SkippedRows.Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_stale_stamp_is_a_conflict()
    {
        using var _ = InTenantA();
        await CountryAsync("NL", "NLD");
        var created = await CreateAsync("HIGH", "High");

        var change = UpdateFrom(created);
        change.SalesLedgerAccount = " 8000 ";
        (await _service.UpdateAsync(created.Id, change)).SalesLedgerAccount.ShouldBe("8000");
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => _service.UpdateAsync(created.Id, change));
    }

    [Fact]
    public async Task Another_tenant_sees_nothing()
    {
        Guid id;
        using (InTenantA())
        {
            await CountryAsync("NL", "NLD");
            id = (await CreateAsync("HIGH", "High")).Id;
        }

        using (InTenantB())
        {
            (await _service.GetListAsync(List())).TotalCount.ShouldBe(0);
            (await _service.GetLookupAsync()).ShouldBeEmpty();
            await Should.ThrowAsync<Volo.Abp.Domain.Entities.EntityNotFoundException>(() => _service.GetAsync(id));

            /* Tenant A's country is not tenant B's. */
            (await Should.ThrowAsync<BusinessException>(() => CreateAsync("HIGH", "High"))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        }
    }
}
