using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Orders;
using OpenTms.Orders.TransportOrders;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Orders;

/// <summary>
/// TimeSlotAppService on SQLite (design 51): CRUD, the overnight flag, the zero-length refusal, sorting on the times and
/// tenant isolation. Risk class: High; Critical for isolation.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class TimeSlotAppServiceTests : ReferenceDataAppServiceTestBase
{
    private readonly ITimeSlotAppService _service;

    public TimeSlotAppServiceTests()
    {
        _service = GetRequiredService<ITimeSlotAppService>();
    }

    private Task<TimeSlotDto> CreateAsync(string code, string en, int from, int till, bool archived = false)
        => _service.CreateAsync(new TimeSlotCreateDto
        {
            Code = code,
            Names = Names(en),
            StartTime = new TimeOnly(from, 0),
            EndTime = new TimeOnly(till, 0),
            IsArchived = archived
        });

    [Fact]
    public async Task Create_stores_the_times_and_flags_an_overnight_slot()
    {
        using var _ = InTenantA();
        var morning = await CreateAsync(" morning ", "Morning", 8, 12);
        var night = await CreateAsync("NIGHT", "Night", 22, 6);

        morning.Code.ShouldBe("MORNING");
        morning.StartTime.ShouldBe(new TimeOnly(8, 0));
        morning.EndTime.ShouldBe(new TimeOnly(12, 0));
        morning.IsOvernight.ShouldBeFalse();
        night.IsOvernight.ShouldBeTrue();
        (await _service.GetListAsync(List())).Rows.Single(r => r.Code == "NIGHT").IsOvernight.ShouldBeTrue();
    }

    [Fact]
    public async Task A_slot_of_zero_length_and_a_duplicate_code_are_refused()
    {
        using var _ = InTenantA();
        (await Should.ThrowAsync<BusinessException>(() => CreateAsync("ZERO", "Zero", 8, 8))).Code.ShouldBe(OrdersErrorCodes.EmptyTimeSlot);

        var morning = await CreateAsync("MORNING", "Morning", 8, 12);
        (await Should.ThrowAsync<BusinessException>(() => CreateAsync("morning", "Other", 9, 10))).Code.ShouldBe(ReferenceDataErrorCodes.DuplicateKey);

        var change = new TimeSlotUpdateDto
        {
            Code = morning.Code, Names = Copy(morning.Names), StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(9, 0), ConcurrencyStamp = morning.ConcurrencyStamp
        };
        (await Should.ThrowAsync<BusinessException>(() => _service.UpdateAsync(morning.Id, change))).Code.ShouldBe(OrdersErrorCodes.EmptyTimeSlot);
    }

    [Fact]
    public async Task List_sorts_on_the_times_hides_archived_rows_and_update_checks_the_stamp()
    {
        using var _ = InTenantA();
        await CreateAsync("DAY", "Day", 8, 17);
        await CreateAsync("EVENING", "Evening", 17, 22);
        var afternoon = await CreateAsync("AFTERNOON", "Afternoon", 12, 17);
        await CreateAsync("OLD", "Old", 1, 2, archived: true);

        (await _service.GetListAsync(List(sortBy: "startTime", sortDir: "desc"))).Rows.Select(r => r.Code).ShouldBe(["EVENING", "AFTERNOON", "DAY"]);
        (await _service.GetListAsync(List(includeArchived: true))).TotalCount.ShouldBe(4);
        (await _service.GetLookupAsync()).Select(l => l.Code).ShouldBe(["AFTERNOON", "DAY", "EVENING"]);

        var change = new TimeSlotUpdateDto
        {
            Code = afternoon.Code, Names = Names("Late afternoon"), StartTime = new TimeOnly(14, 0), EndTime = new TimeOnly(18, 0), ConcurrencyStamp = afternoon.ConcurrencyStamp
        };
        var updated = await _service.UpdateAsync(afternoon.Id, change);
        updated.Name.ShouldBe("Late afternoon");
        updated.StartTime.ShouldBe(new TimeOnly(14, 0));

        await Should.ThrowAsync<AbpDbConcurrencyException>(() => _service.UpdateAsync(afternoon.Id, change));

        await _service.DeleteAsync(afternoon.Id);
        (await _service.GetListAsync(List())).TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task Another_tenant_sees_nothing()
    {
        Guid id;
        using (InTenantA())
        {
            id = (await CreateAsync("DAY", "Day", 8, 17)).Id;
        }

        using (InTenantB())
        {
            (await _service.GetListAsync(List())).TotalCount.ShouldBe(0);
            (await _service.GetLookupAsync()).ShouldBeEmpty();
            await Should.ThrowAsync<Volo.Abp.Domain.Entities.EntityNotFoundException>(() => _service.GetAsync(id));
            (await CreateAsync("DAY", "Day", 8, 17)).Code.ShouldBe("DAY", "codes are unique per tenant");
        }
    }
}
