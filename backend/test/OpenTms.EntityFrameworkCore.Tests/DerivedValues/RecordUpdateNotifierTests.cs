using System;
using System.Threading.Tasks;
using OpenTms.EntityFrameworkCore;
using OpenTms.Platform.DerivedValues;
using OpenTms.Platform.RecordUpdates;
using Shouldly;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Xunit;

namespace OpenTms.DerivedValues;

/// <summary>
/// The notifier of design 49 (flow 2 D): inside a unit of work a notice leaves only after the commit, a rolled-back
/// change tells nobody, outside a unit of work it leaves at once, and it always carries the current tenant.
/// Risk class: High.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class RecordUpdateNotifierTests : OpenTmsEntityFrameworkCoreTestBase
{
    private readonly IRecordUpdateNotifier _notifier;
    private readonly CapturingRecordUpdateTransport _transport;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ICurrentTenant _currentTenant;

    public RecordUpdateNotifierTests()
    {
        _notifier = GetRequiredService<IRecordUpdateNotifier>();
        _transport = GetRequiredService<CapturingRecordUpdateTransport>();
        _unitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
    }

    [Fact]
    public async Task Inside_a_unit_of_work_the_notice_leaves_after_the_commit()
    {
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        using (_currentTenant.Change(tenant))
        {
            using (var uow = _unitOfWorkManager.Begin(requiresNew: true))
            {
                await _notifier.NotifyAsync("Test.Probe", id, 7, DerivedValueStatus.UpToDate);
                _transport.For(id).ShouldBeEmpty("nothing leaves before the commit");
                await uow.CompleteAsync();
            }
        }

        var sent = _transport.For(id).ShouldHaveSingleItem();
        sent.ShouldBe(new RecordUpdate(tenant, "Test.Probe", id, 7, DerivedValueStatus.UpToDate));
    }

    [Fact]
    public async Task A_rolled_back_unit_of_work_tells_nobody()
    {
        var id = Guid.NewGuid();
        using (var uow = _unitOfWorkManager.Begin(requiresNew: true))
        {
            await _notifier.NotifyAsync("Test.Probe", id);
            await uow.RollbackAsync();
        }

        _transport.For(id).ShouldBeEmpty();
    }

    [Fact]
    public async Task Outside_a_unit_of_work_the_notice_leaves_at_once()
    {
        var id = Guid.NewGuid();
        _unitOfWorkManager.Current.ShouldBeNull();
        await _notifier.NotifyAsync("Test.Probe", id);
        _transport.For(id).ShouldHaveSingleItem().Status.ShouldBeNull();
    }
}
