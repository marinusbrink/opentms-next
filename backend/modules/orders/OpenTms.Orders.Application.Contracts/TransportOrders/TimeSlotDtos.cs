using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Application.Services;

namespace OpenTms.Orders.TransportOrders;

public class TimeSlotRowDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Local time at the stop.</summary>
    public TimeOnly StartTime { get; set; }
    /// <summary>Local time at the stop; before the start for an overnight slot.</summary>
    public TimeOnly EndTime { get; set; }
    public bool IsOvernight { get; set; }
    public bool IsArchived { get; set; }
    public string? Description { get; set; }
}

public class TimeSlotDto : ReferenceDataDetailDtoBase
{
    public string Code { get; set; } = string.Empty;
    /// <summary>The name in the request culture.</summary>
    public string Name { get; set; } = string.Empty;
    public LocalizedNameDto Names { get; set; } = new();
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public bool IsOvernight { get; set; }
    public bool IsArchived { get; set; }
}

public class TimeSlotCreateDto
{
    [Required, MaxLength(ReferenceDataConsts.MaxCodeLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    public LocalizedNameDto Names { get; set; } = new();

    [MaxLength(ReferenceDataConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>Local time at the stop; equal to the end is refused.</summary>
    public TimeOnly StartTime { get; set; }

    /// <summary>Local time at the stop; before the start means the window ends the next day.</summary>
    public TimeOnly EndTime { get; set; }

    public bool IsArchived { get; set; }
}

public class TimeSlotUpdateDto : TimeSlotCreateDto, IReferenceDataUpdateDto
{
    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public interface ITimeSlotAppService : IApplicationService
{
    Task<GridResponse<TimeSlotRowDto>> GetListAsync(ArchivableListRequestDto input);
    Task<TimeSlotDto> GetAsync(Guid id);
    Task<List<ReferenceDataLookupDto>> GetLookupAsync(string? term = null, int take = ReferenceDataConsts.MaxLookupTake);
    Task<TimeSlotDto> CreateAsync(TimeSlotCreateDto input);
    Task<TimeSlotDto> UpdateAsync(Guid id, TimeSlotUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<ReferenceDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input);
}
