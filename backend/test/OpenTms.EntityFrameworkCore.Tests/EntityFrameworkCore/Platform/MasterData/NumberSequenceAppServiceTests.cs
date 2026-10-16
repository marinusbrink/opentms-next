using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Platform;
using OpenTms.Platform.NumberSequences;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;
using Volo.Abp.Validation;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.MasterData;

/// <summary>
/// NumberSequenceAppService and the counter repository on SQLite (docs/designs/49-platform-master-data.md, "Guards": once
/// a counter row exists the scheme, the five affixes and the minimum digits are frozen and the sequence cannot be
/// deleted, name and description stay editable; the DTOs carry IsInUse and ExampleNumber). The counter row is inserted
/// directly, as the generator cannot run on SQLite (transactions disabled, design 49 assumption 8): the refusal itself is
/// pinned here, the generator is covered on PostgreSQL (NumberGeneratorPostgreSqlTests). The repository's upsert runs on
/// SQLite (≥ 3.35) and is exercised directly. Risk class: High (guards), Critical for the counter.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class NumberSequenceAppServiceTests : PlatformMasterDataAppServiceTestBase
{
    private readonly INumberSequenceAppService _service;
    private readonly IRepository<NumberSequenceCounter> _counterRows;
    private readonly INumberSequenceCounterRepository _counters;

    public NumberSequenceAppServiceTests()
    {
        _service = GetRequiredService<INumberSequenceAppService>();
        _counterRows = GetRequiredService<IRepository<NumberSequenceCounter>>();
        _counters = GetRequiredService<INumberSequenceCounterRepository>();
    }

    private static NumberSequenceCreateDto Dto(string name, NumberSequenceScheme scheme = NumberSequenceScheme.TwoDigitYear, string? prefix = "O-", string? yearSuffix = "-", int digits = 4, Action<NumberSequenceCreateDto>? configure = null)
    {
        var dto = new NumberSequenceCreateDto { Name = name, Scheme = scheme, Prefix = prefix, YearSuffix = yearSuffix, MinimumDigits = digits };
        configure?.Invoke(dto);
        return dto;
    }

    private static NumberSequenceUpdateDto UpdateFrom(NumberSequenceDto dto, Action<NumberSequenceUpdateDto>? configure = null)
    {
        var update = new NumberSequenceUpdateDto
        {
            Name = dto.Name, Scheme = dto.Scheme, Prefix = dto.Prefix, YearPrefix = dto.YearPrefix, YearSuffix = dto.YearSuffix,
            MonthPrefix = dto.MonthPrefix, MonthSuffix = dto.MonthSuffix, MinimumDigits = dto.MinimumDigits, Description = dto.Description,
            ConcurrencyStamp = dto.ConcurrencyStamp
        };
        configure?.Invoke(update);
        return update;
    }

    private static PlatformMasterDataListRequestDto Request(string? search = null, string? sortBy = null, string? sortDir = null)
        => List<PlatformMasterDataListRequestDto>(search: search, sortBy: sortBy, sortDir: sortDir);

    private Task InsertCounterAsync(Guid sequenceId, int year = 2026, int month = 0, long lastValue = 5)
        => WithUnitOfWorkAsync(() => _counterRows.InsertAsync(new NumberSequenceCounter(CurrentTenant.Id, sequenceId, year, month, lastValue), autoSave: true));

    private static string Today(string format) => DateTime.Now.ToString(format, System.Globalization.CultureInfo.InvariantCulture);

    // ── Round trip and example ───────────────────────────────────────────────

    [Fact]
    public async Task A_sequence_round_trips_with_its_example_number_for_today_and_is_not_in_use()
    {
        using var _ = InTenantA();
        var created = await _service.CreateAsync(Dto(" Orders ", configure: d => d.Description = " Transport orders "));

        created.Name.ShouldBe("Orders");
        (created.Scheme, created.Prefix, created.YearPrefix, created.YearSuffix, created.MonthPrefix, created.MonthSuffix, created.MinimumDigits)
            .ShouldBe((NumberSequenceScheme.TwoDigitYear, "O-", (string?)null, "-", (string?)null, (string?)null, 4));
        created.Description.ShouldBe("Transport orders");
        created.ExampleNumber.ShouldBe($"O-{Today("yy")}-0001", "the first number of today's period");
        created.IsInUse.ShouldBeFalse();

        var row = (await _service.GetListAsync(Request())).Rows.ShouldHaveSingleItem();
        (row.Id, row.Name, row.Scheme, row.ExampleNumber, row.IsInUse).ShouldBe((created.Id, "Orders", NumberSequenceScheme.TwoDigitYear, $"O-{Today("yy")}-0001", false));

        var monthly = await _service.CreateAsync(Dto("Invoices", NumberSequenceScheme.FourDigitYearMonth, "F", null, 5, d => { d.YearSuffix = "/"; d.MonthSuffix = "-"; }));
        monthly.ExampleNumber.ShouldBe($"F{Today("yyyy")}/{Today("MM")}-00001");
        (await _service.CreateAsync(Dto("Counter", NumberSequenceScheme.CounterOnly, "P", "ignored", 6))).ExampleNumber.ShouldBe("P000001");
    }

    [Fact]
    public async Task Affixes_are_kept_as_typed_and_an_empty_affix_is_none()
    {
        using var _ = InTenantA();
        var created = await _service.CreateAsync(Dto("Spaced", prefix: "O ", yearSuffix: " ", configure: d => { d.YearPrefix = ""; d.MonthPrefix = ""; }));
        (created.Prefix, created.YearSuffix, created.YearPrefix, created.MonthPrefix).ShouldBe(("O ", " ", (string?)null, (string?)null));
        created.ExampleNumber.ShouldBe($"O {Today("yy")} 0001");
    }

    [Fact]
    public async Task Invalid_numbering_fields_are_refused_by_the_contract()
    {
        using var _ = InTenantA();
        await Should.ThrowAsync<AbpValidationException>(() => _service.CreateAsync(Dto("Zero", digits: 0)));
        await Should.ThrowAsync<AbpValidationException>(() => _service.CreateAsync(Dto("Thirteen", digits: 13)));
        await Should.ThrowAsync<AbpValidationException>(() => _service.CreateAsync(Dto("Unknown", (NumberSequenceScheme)0)));
        await Should.ThrowAsync<AbpValidationException>(() => _service.CreateAsync(Dto("Long", prefix: new string('P', PlatformMasterDataConsts.MaxAffixLength + 1))));
        await Should.ThrowAsync<AbpValidationException>(() => _service.CreateAsync(Dto("")));
        (await _service.CreateAsync(Dto("Twelve", digits: 12))).MinimumDigits.ShouldBe(12);
        (await _service.CreateAsync(Dto("One", digits: 1))).MinimumDigits.ShouldBe(1);
        (await _service.GetListAsync(Request())).TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task A_name_differing_only_in_case_is_a_duplicate_while_the_own_name_may_change_case()
    {
        using var _ = InTenantA();
        var orders = await _service.CreateAsync(Dto("Orders"));
        (await ShouldFailAsync(() => _service.CreateAsync(Dto("ORDERS", NumberSequenceScheme.CounterOnly)), PlatformErrorCodes.DuplicateKey)).Data["field"].ShouldBe("name");

        var trips = await _service.CreateAsync(Dto("Trips"));
        await ShouldFailAsync(() => _service.UpdateAsync(trips.Id, UpdateFrom(trips, u => u.Name = " orders ")), PlatformErrorCodes.DuplicateKey);
        (await _service.UpdateAsync(orders.Id, UpdateFrom(orders, u => u.Name = "ORDERS"))).Name.ShouldBe("ORDERS");

        await _service.DeleteAsync(trips.Id);
        (await _service.CreateAsync(Dto("trips"))).Id.ShouldNotBe(trips.Id);
    }

    [Fact]
    public async Task A_stale_concurrency_stamp_is_refused()
    {
        using var _ = InTenantA();
        var created = await _service.CreateAsync(Dto("Orders"));
        await _service.UpdateAsync(created.Id, UpdateFrom(created, u => u.Description = "first"));
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => _service.UpdateAsync(created.Id, UpdateFrom(created, u => u.Description = "second")));
        (await _service.GetAsync(created.Id)).Description.ShouldBe("first");
    }

    // ── Guards once in use (table) ───────────────────────────────────────────

    public static IEnumerable<object[]> NumberingFieldChanges() =>
    [
        ["scheme"], ["prefix"], ["yearPrefix"], ["yearSuffix"], ["monthPrefix"], ["monthSuffix"], ["minimumDigits"],
        ["prefixCleared"], ["prefixSpace"],
    ];

    private static void Change(NumberSequenceUpdateDto update, string field)
    {
        switch (field)
        {
            case "scheme": update.Scheme = NumberSequenceScheme.FourDigitYear; break;
            case "prefix": update.Prefix = "X-"; break;
            case "yearPrefix": update.YearPrefix = "Y"; break;
            case "yearSuffix": update.YearSuffix = "/"; break;
            /* The scheme shows no month, yet the month affixes freeze too (design 49, build notes). */
            case "monthPrefix": update.MonthPrefix = "M"; break;
            case "monthSuffix": update.MonthSuffix = "-"; break;
            case "minimumDigits": update.MinimumDigits = 5; break;
            case "prefixCleared": update.Prefix = null; break;
            case "prefixSpace": update.Prefix = "O- "; break;
            default: throw new ArgumentOutOfRangeException(nameof(field));
        }
    }

    [Theory]
    [MemberData(nameof(NumberingFieldChanges))]
    public async Task Once_a_counter_row_exists_every_numbering_field_is_frozen(string field)
    {
        using var _ = InTenantA();
        var created = await _service.CreateAsync(Dto("Orders"));

        /* Not in use: the field may change. */
        var free = await _service.UpdateAsync(created.Id, UpdateFrom(created, u => Change(u, field)));
        free.IsInUse.ShouldBeFalse();
        var restored = await _service.UpdateAsync(created.Id, UpdateFrom(free, u =>
        {
            u.Scheme = created.Scheme; u.Prefix = created.Prefix; u.YearPrefix = created.YearPrefix; u.YearSuffix = created.YearSuffix;
            u.MonthPrefix = created.MonthPrefix; u.MonthSuffix = created.MonthSuffix; u.MinimumDigits = created.MinimumDigits;
        }));

        await InsertCounterAsync(created.Id);

        var inUse = await _service.GetAsync(created.Id);
        inUse.IsInUse.ShouldBeTrue();
        await ShouldFailAsync(() => _service.UpdateAsync(created.Id, UpdateFrom(inUse, u => Change(u, field))), PlatformErrorCodes.NumberSequenceInUse);

        /* The name of a refused update is not asserted here: SQLite runs without transactions and ABP's auditing saves the
         * unit of work's pending changes on the error path too, so a field set before the refusal reaches the database in
         * this module only. With the real transaction (PostgreSQL) nothing is written: NumberGeneratorPostgreSqlTests. */
        var after = await _service.GetAsync(created.Id);
        (after.Scheme, after.Prefix, after.YearPrefix, after.YearSuffix, after.MonthPrefix, after.MonthSuffix, after.MinimumDigits)
            .ShouldBe((restored.Scheme, restored.Prefix, restored.YearPrefix, restored.YearSuffix, restored.MonthPrefix, restored.MonthSuffix, restored.MinimumDigits),
                "a refused update leaves the numbering fields as they were");
    }

    [Fact]
    public async Task Once_in_use_the_name_and_description_stay_editable_and_an_unchanged_format_is_accepted()
    {
        using var _ = InTenantA();
        var created = await _service.CreateAsync(Dto("Orders", configure: d => d.YearPrefix = null));
        await InsertCounterAsync(created.Id);

        var renamed = await _service.UpdateAsync(created.Id, UpdateFrom(await _service.GetAsync(created.Id), u =>
        {
            u.Name = "Transport orders";
            u.Description = "Since 2026";
            u.YearPrefix = "";   // empty is the same as none
            u.MonthSuffix = "";
        }));

        (renamed.Name, renamed.Description, renamed.IsInUse).ShouldBe(("Transport orders", "Since 2026", true));
        renamed.ExampleNumber.ShouldBe(created.ExampleNumber);
        (await _service.GetListAsync(Request())).Rows.ShouldHaveSingleItem().IsInUse.ShouldBeTrue();
    }

    [Fact]
    public async Task A_sequence_in_use_cannot_be_deleted_and_bulk_delete_skips_it_with_its_reason()
    {
        using var _ = InTenantA();
        var used = await _service.CreateAsync(Dto("Used"));
        var unused = await _service.CreateAsync(Dto("Unused"));
        await InsertCounterAsync(used.Id, 2025, 0, 17);

        var ex = await ShouldFailAsync(() => _service.DeleteAsync(used.Id), PlatformErrorCodes.InUse);
        ex.Data["reason"].ShouldBeOfType<string>().ShouldNotBeNullOrWhiteSpace();

        var result = await _service.BulkDeleteAsync(new PlatformMasterDataBulkDeleteRequestDto { Selection = FilterBased() });
        result.DeletedCount.ShouldBe(1);
        var skipped = result.SkippedRows.ShouldHaveSingleItem();
        (skipped.Id, skipped.Name).ShouldBe((used.Id, "Used"));
        skipped.Reason.ShouldNotBeNullOrWhiteSpace();

        await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(unused.Id));
        (await _service.GetAsync(used.Id)).IsInUse.ShouldBeTrue();
        (await WithUnitOfWorkAsync(() => _counterRows.CountAsync(c => c.NumberSequenceId == used.Id))).ShouldBe(1, "the counter is untouched");
        (await _service.BulkDeleteAsync(new PlatformMasterDataBulkDeleteRequestDto { Selection = Explicit(used.Id, unused.Id) })).ShouldSatisfyAllConditions(
            r => r.DeletedCount.ShouldBe(0), r => r.SkippedRows.ShouldHaveSingleItem().Id.ShouldBe(used.Id));
    }

    // ── List and lookup ──────────────────────────────────────────────────────

    [Fact]
    public async Task The_list_searches_name_and_description_sorts_on_name_and_scheme_only_and_the_lookup_lists_every_sequence()
    {
        using var _ = InTenantA();
        await _service.CreateAsync(Dto("Invoices", NumberSequenceScheme.CounterOnly, configure: d => d.Description = "outbound invoices"));
        await _service.CreateAsync(Dto("Orders", NumberSequenceScheme.FourDigitYear));
        var trips = await _service.CreateAsync(Dto("Trips", NumberSequenceScheme.TwoDigitYearMonth));
        await InsertCounterAsync(trips.Id);

        (await _service.GetListAsync(Request())).Rows.Select(r => r.Name).ShouldBe(["Invoices", "Orders", "Trips"]);
        (await _service.GetListAsync(Request(search: "OUTBOUND"))).Rows.ShouldHaveSingleItem().Name.ShouldBe("Invoices");
        (await _service.GetListAsync(Request(sortBy: "scheme"))).Rows.Select(r => r.Name).ShouldBe(["Orders", "Trips", "Invoices"]);
        (await _service.GetListAsync(Request(sortBy: "name", sortDir: "desc"))).Rows.Select(r => r.Name).ShouldBe(["Trips", "Orders", "Invoices"]);
        (await _service.GetListAsync(Request())).Rows.Select(r => r.IsInUse).ShouldBe([false, false, true]);
        await ShouldFailAsync(() => _service.GetListAsync(Request(sortBy: "exampleNumber")), PlatformErrorCodes.UnknownSortColumn);
        await ShouldFailAsync(() => _service.GetListAsync(Request(sortBy: "isInUse")), PlatformErrorCodes.UnknownSortColumn);

        (await _service.GetLookupAsync()).Select(l => l.Name).ShouldBe(["Invoices", "Orders", "Trips"], "an in-use sequence is still pickable");
        (await _service.GetLookupAsync("rip")).ShouldHaveSingleItem().Id.ShouldBe(trips.Id);
    }

    // ── The counter upsert on SQLite (the generator itself runs on PostgreSQL) ──

    [Fact]
    public async Task The_counter_upsert_starts_at_one_adds_one_per_call_and_keeps_periods_and_sequences_apart()
    {
        using var _ = InTenantA();
        var orders = await _service.CreateAsync(Dto("Orders", NumberSequenceScheme.FourDigitYearMonth));
        var trips = await _service.CreateAsync(Dto("Trips", NumberSequenceScheme.CounterOnly));

        var values = await WithUnitOfWorkAsync(async () => new[]
        {
            await _counters.IncrementAsync(TenantA, orders.Id, 2026, 12),
            await _counters.IncrementAsync(TenantA, orders.Id, 2026, 12),
            await _counters.IncrementAsync(TenantA, orders.Id, 2027, 1),
            await _counters.IncrementAsync(TenantA, orders.Id, 2026, 12),
            await _counters.IncrementAsync(TenantA, trips.Id, 0, 0),
            await _counters.IncrementAsync(TenantA, orders.Id, 2027, 1),
        });
        values.ShouldBe([1L, 2L, 1L, 3L, 1L, 2L]);

        var rows = await WithUnitOfWorkAsync(() => _counterRows.GetListAsync());
        rows.Select(r => (r.NumberSequenceId, r.PeriodYear, r.PeriodMonth, r.LastValue)).ShouldBe(
            [(orders.Id, 2026, 12, 3L), (orders.Id, 2027, 1, 2L), (trips.Id, 0, 0, 1L)], ignoreOrder: true);
        rows.ShouldAllBe(r => r.TenantId == TenantA);

        /* The rows the raw SQL wrote are the rows the in-use check reads. */
        (await _service.GetAsync(orders.Id)).IsInUse.ShouldBeTrue();
        (await _service.GetAsync(trips.Id)).IsInUse.ShouldBeTrue();
        await ShouldFailAsync(() => _service.DeleteAsync(trips.Id), PlatformErrorCodes.InUse);
    }

    [Fact]
    public async Task The_counter_continues_from_a_row_that_already_exists()
    {
        using var _ = InTenantA();
        var orders = await _service.CreateAsync(Dto("Orders"));
        await InsertCounterAsync(orders.Id, 2026, 0, 41);

        (await WithUnitOfWorkAsync(() => _counters.IncrementAsync(TenantA, orders.Id, 2026, 0))).ShouldBe(42L);
    }

    [Fact]
    public async Task The_generator_refuses_to_run_outside_a_transactional_unit_of_work_and_draws_nothing()
    {
        using var _ = InTenantA();
        var orders = await _service.CreateAsync(Dto("Orders"));
        var generator = GetRequiredService<INumberGenerator>();
        var date = new DateOnly(2026, 10, 17);

        await Should.ThrowAsync<NumberGenerationRequiresTransactionException>(() => generator.GetNextAsync(orders.Id, date));
        await Should.ThrowAsync<NumberGenerationRequiresTransactionException>(() =>
            WithUnitOfWorkAsync(new AbpUnitOfWorkOptions { IsTransactional = false }, () => generator.GetNextAsync(orders.Id, date)));

        /* The SQLite test module disables transactions, so even a transactional unit of work is refused here
         * (design 49, assumption 8): the generator is tested on PostgreSQL. */
        await Should.ThrowAsync<NumberGenerationRequiresTransactionException>(() =>
            WithUnitOfWorkAsync(new AbpUnitOfWorkOptions { IsTransactional = true }, () => generator.GetNextAsync(orders.Id, date)));

        (await WithUnitOfWorkAsync(() => _counterRows.CountAsync())).ShouldBe(0);
        (await _service.GetAsync(orders.Id)).IsInUse.ShouldBeFalse();
    }

    // ── Tenant isolation (Critical; PostgreSQL in PlatformMasterDataTenantIsolationTests) ──

    [Fact]
    public async Task Tenant_B_neither_sees_nor_touches_tenant_As_sequence_and_its_counters_are_its_own()
    {
        NumberSequenceDto mine;
        using (InTenantA())
        {
            mine = await _service.CreateAsync(Dto("Orders"));
            await InsertCounterAsync(mine.Id);
        }

        using (InTenantB())
        {
            (await _service.GetListAsync(Request())).TotalCount.ShouldBe(0);
            (await _service.GetLookupAsync()).ShouldBeEmpty();
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(mine.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.UpdateAsync(mine.Id, UpdateFrom(mine, u => u.Name = "Stolen")));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.DeleteAsync(mine.Id));
            (await _service.BulkDeleteAsync(new PlatformMasterDataBulkDeleteRequestDto { Selection = Explicit(mine.Id) })).DeletedCount.ShouldBe(0);
            (await WithUnitOfWorkAsync(() => _counterRows.CountAsync())).ShouldBe(0, "tenant A's counter rows are not tenant B's");

            var theirs = await _service.CreateAsync(Dto("ORDERS"));
            theirs.IsInUse.ShouldBeFalse("tenant A's counters do not make tenant B's sequence in use");
        }

        using (InTenantA())
        {
            (await _service.GetAsync(mine.Id)).ShouldSatisfyAllConditions(s => s.Name.ShouldBe("Orders"), s => s.IsInUse.ShouldBeTrue());
        }
    }
}
