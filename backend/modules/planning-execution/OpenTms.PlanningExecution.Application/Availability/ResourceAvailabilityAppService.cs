using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using OpenTms.PlanningExecution.Permissions;
using OpenTms.Platform.Features;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;
using Volo.Abp.Timing;

namespace OpenTms.PlanningExecution.Availability;

/// <summary>
/// Design 68 §2 (PO decisions D9, D10): the unavailability and working times of vehicles and drivers, managed in the
/// Planboard's Resources pane. Resource ids are validated against Master Data on write (one published lookup). Every
/// write notifies the record channel (<c>PlanningExecution.ResourceAvailability</c>, hints: the period's dates, the
/// resource id) and asks the live trips of the resource in the changed period to recalculate (their
/// <c>ResourceUnavailable</c> / <c>OutsideWorkingTime</c> warnings, §3). Feature: the Planboard's Resources pane.
/// </summary>
[RequiresFeature(AppFeatures.Planboard, AppFeatures.PlanboardApp.Resources, RequiresAll = true)]
public class ResourceAvailabilityAppService : PlanningExecutionAppServiceBase, IResourceAvailabilityAppService
{
    /// <summary>How far back a change asks trips to recalculate: older trips keep their warnings.</summary>
    private static readonly TimeSpan RecalculationLookBack = TimeSpan.FromDays(1);

    private readonly IRepository<ResourceUnavailability, Guid> _unavailabilities;
    private readonly IRepository<ResourceWorkingPattern, Guid> _patterns;
    private readonly AvailabilityReader _reader;
    private readonly MasterDataReferences _masterData;
    private readonly PlanningRecordNotices _notices;
    private readonly TripOverlapFanOut _fanOut;
    private readonly IClock _clock;

    public ResourceAvailabilityAppService(
        IRepository<ResourceUnavailability, Guid> unavailabilities,
        IRepository<ResourceWorkingPattern, Guid> patterns,
        AvailabilityReader reader,
        MasterDataReferences masterData,
        PlanningRecordNotices notices,
        TripOverlapFanOut fanOut,
        IClock clock)
    {
        _unavailabilities = unavailabilities;
        _patterns = patterns;
        _reader = reader;
        _masterData = masterData;
        _notices = notices;
        _fanOut = fanOut;
        _clock = clock;
    }

    /// <summary>Three statements: the unavailabilities reaching into the window, the patterns; the expansion is in memory.</summary>
    [Authorize(PlanningExecutionPermissions.ResourceAvailability.Default)]
    public virtual async Task<ResourceAvailabilityListDto> GetListAsync(ResourceAvailabilityListRequestDto input)
    {
        if (input.To <= input.From || input.To - input.From > TimeSpan.FromDays(PlanningExecutionConsts.MaxAvailabilityWindowDays))
        {
            throw new BusinessException(PlanningExecutionErrorCodes.InvalidAvailabilityWindow).WithData("max", PlanningExecutionConsts.MaxAvailabilityWindowDays);
        }

        var ids = input.ResourceIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count > PlanningExecutionConsts.MaxAvailabilityResourceIds)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.TooManyIds).WithData("max", PlanningExecutionConsts.MaxAvailabilityResourceIds);
        }

        var (unavailabilities, patterns) = await _reader.LoadAsync(input.ResourceKind, ids, input.From, input.To);
        var blocks = _reader.Blocks(input.ResourceKind, ids, unavailabilities, patterns, input.From, input.To);
        var shown = blocks.Where(b => b.UnavailabilityId != null).Select(b => b.UnavailabilityId!.Value).ToHashSet();
        return new ResourceAvailabilityListDto
        {
            Blocks = blocks,
            Unavailabilities = unavailabilities.Where(u => shown.Contains(u.Id)).Select(ToDto).ToList(),
            Patterns = patterns.Select(ToDto).ToList()
        };
    }

    [Authorize(PlanningExecutionPermissions.ResourceAvailability.Default)]
    public virtual async Task<ResourceUnavailabilityDto> GetAsync(Guid id) => ToDto(await _unavailabilities.GetAsync(id));

    [Authorize(PlanningExecutionPermissions.ResourceAvailability.Manage)]
    public virtual async Task<ResourceUnavailabilityDto> CreateAsync(ResourceUnavailabilityCreateDto input)
    {
        await EnsureResourceExistsAsync(input.ResourceKind, input.ResourceId);
        var unavailability = new ResourceUnavailability(GuidGenerator.Create(), CurrentTenant.Id, input.ResourceKind, input.ResourceId,
            input.From, input.Till, input.Reason, input.Remarks, input.RecurrenceWeekdays, input.RecurrenceUntil);
        await _unavailabilities.InsertAsync(unavailability, autoSave: true);
        await ChangedAsync(unavailability, null, removed: false, "AvailabilityCreated");
        Logger.LogInformation("Unavailability {UnavailabilityId} of {ResourceKind} {ResourceId} created for tenant {TenantId}.", unavailability.Id, unavailability.ResourceKind, unavailability.ResourceId, CurrentTenant.Id);
        return ToDto(unavailability);
    }

    [Authorize(PlanningExecutionPermissions.ResourceAvailability.Manage)]
    public virtual async Task<ResourceUnavailabilityDto> UpdateAsync(Guid id, ResourceUnavailabilityUpdateDto input)
    {
        var unavailability = await _unavailabilities.GetAsync(id);
        var before = Span(unavailability);
        unavailability.Update(input.From, input.Till, input.Reason, input.Remarks, input.RecurrenceWeekdays, input.RecurrenceUntil);
        unavailability.ConcurrencyStamp = input.ConcurrencyStamp;
        await _unavailabilities.UpdateAsync(unavailability, autoSave: true);
        await ChangedAsync(unavailability, before, removed: false, "AvailabilityChanged");
        return ToDto(unavailability);
    }

    [Authorize(PlanningExecutionPermissions.ResourceAvailability.Manage)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var unavailability = await _unavailabilities.GetAsync(id);
        await _unavailabilities.DeleteAsync(unavailability, autoSave: true);
        await ChangedAsync(unavailability, null, removed: true, "AvailabilityDeleted");
    }

    /// <summary>
    /// Replaces the resource's pattern: the weekdays named are set (updated in place, so the unique key never collides),
    /// the others are removed. Asks the resource's live trips from yesterday on to recalculate.
    /// </summary>
    [Authorize(PlanningExecutionPermissions.ResourceAvailability.Manage)]
    public virtual async Task<List<ResourceWorkingPatternDto>> UpdatePatternsAsync(ResourceWorkingPatternsUpdateDto input)
    {
        if (input.Days.Count > 7 || input.Days.Select(d => d.Weekday).Distinct().Count() != input.Days.Count
            || input.Days.Any(d => d.Weekday is < 0 or > 6 || d.End <= d.Start))
        {
            throw new BusinessException(PlanningExecutionErrorCodes.InvalidWorkingPattern);
        }

        await EnsureResourceExistsAsync(input.ResourceKind, input.ResourceId);
        var existing = await _patterns.GetListAsync(p => p.ResourceKind == input.ResourceKind && p.ResourceId == input.ResourceId);
        foreach (var day in input.Days)
        {
            var row = existing.FirstOrDefault(p => p.Weekday == day.Weekday);
            if (row == null)
            {
                await _patterns.InsertAsync(new ResourceWorkingPattern(GuidGenerator.Create(), CurrentTenant.Id, input.ResourceKind, input.ResourceId, day.Weekday, day.Start, day.End));
            }
            else
            {
                row.SetTimes(day.Start, day.End);
                await _patterns.UpdateAsync(row);
            }
        }

        var removed = existing.Where(p => input.Days.All(d => d.Weekday != p.Weekday)).ToList();
        if (removed.Count > 0)
        {
            await _patterns.DeleteManyAsync(removed);
        }

        await CurrentUnitOfWork!.SaveChangesAsync();
        var now = Now.UtcDateTime;
        await _fanOut.RequestForResourceAsync(input.ResourceKind, input.ResourceId, now - RecalculationLookBack,
            now.AddDays(PlanningExecutionConsts.MaxUnavailabilityDays), "WorkingPatternChanged");
        await _notices.AvailabilityAsync(input.ResourceId, input.ResourceId, null, null);
        return (await _patterns.GetListAsync(p => p.ResourceKind == input.ResourceKind && p.ResourceId == input.ResourceId))
            .OrderBy(p => p.Weekday).Select(ToDto).ToList();
    }

    private DateTimeOffset Now => new(_clock.Now.ToUniversalTime(), TimeSpan.Zero);

    private async Task EnsureResourceExistsAsync(ResourceKind kind, Guid resourceId)
    {
        var exists = kind switch
        {
            ResourceKind.Vehicle => (await _masterData.VehiclesAsync([resourceId])).ContainsKey(resourceId),
            ResourceKind.Driver => (await _masterData.DriversAsync([resourceId])).ContainsKey(resourceId),
            _ => throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", "resourceKind").WithData("value", (int)kind)
        };
        if (!exists)
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "resourceId").WithData("value", resourceId);
        }
    }

    private static (DateTime FromUtc, DateTime TillUtc) Span(ResourceUnavailability u) => (u.FromUtc, u.SpanTillUtc);

    /// <summary>The record channel with the period's dates and the resource; the trips of the period before and after recalculate.</summary>
    private async Task ChangedAsync(ResourceUnavailability unavailability, (DateTime FromUtc, DateTime TillUtc)? before, bool removed, string reason)
    {
        var earliest = Now.UtcDateTime - RecalculationLookBack;
        foreach (var (fromUtc, tillUtc) in new[] { before, Span(unavailability) }.Where(s => s != null).Select(s => s!.Value).Distinct())
        {
            await _fanOut.RequestForResourceAsync(unavailability.ResourceKind, unavailability.ResourceId, fromUtc > earliest ? fromUtc : earliest, tillUtc, reason);
        }

        var zone = _reader.Zone;
        var from = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(unavailability.From, zone).Date);
        var till = unavailability.RecurrenceUntil ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(unavailability.Till, zone).Date);
        await _notices.AvailabilityAsync(unavailability.Id, unavailability.ResourceId, from, till, removed);
    }

    private static ResourceUnavailabilityDto ToDto(ResourceUnavailability u) => new()
    {
        Id = u.Id,
        ResourceKind = u.ResourceKind,
        ResourceId = u.ResourceId,
        From = u.From,
        Till = u.Till,
        Reason = u.Reason,
        Remarks = u.Remarks,
        RecurrenceWeekdays = u.RecurrenceWeekdays,
        RecurrenceUntil = u.RecurrenceUntil,
        ConcurrencyStamp = u.ConcurrencyStamp,
        LastModificationTime = u.LastModificationTime
    };

    private static ResourceWorkingPatternDto ToDto(ResourceWorkingPattern p) => new()
    {
        ResourceKind = p.ResourceKind,
        ResourceId = p.ResourceId,
        Weekday = p.Weekday,
        Start = p.Start,
        End = p.End
    };
}
