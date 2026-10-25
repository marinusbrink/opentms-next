using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Application.Services;

namespace OpenTms.Orders.TransportOrders;

public class TransportOrderTypeRowDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>The format applied to counter 1 today, e.g. "TO-2026-00001".</summary>
    public string NumberingExample { get; set; } = string.Empty;
    /// <summary>The names of the default stops in their order, joined by ", ".</summary>
    public string DefaultWaypoints { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsArchived { get; set; }
    public string? Description { get; set; }
}

public class TransportOrderTypeDefaultWaypointDto
{
    public Guid WaypointTypeId { get; set; }

    /// <summary>The waypoint type's name (read only; ignored on save).</summary>
    public string? Name { get; set; }

    /// <summary>1-based; the stops are saved in this order and renumbered 1, 2, … (more than 10 stops is TooManyChildren).</summary>
    [Range(1, 1000)]
    public int Position { get; set; }
}

public class TransportOrderTypeDto : ReferenceDataDetailDtoBase
{
    public string Code { get; set; } = string.Empty;
    /// <summary>The name in the request culture.</summary>
    public string Name { get; set; } = string.Empty;
    public LocalizedNameDto Names { get; set; } = new();
    public TransportOrderRequiredFields RequiredFields { get; set; }
    public List<TransportOrderTypeDefaultWaypointDto> DefaultWaypoints { get; set; } = [];
    public NumberSequenceFormatDto Numbering { get; set; } = new();
    /// <summary>The counter the next order of this type takes.</summary>
    public long NextNumber { get; set; }
    /// <summary>The number the next order of this type gets, e.g. "TO-2026-00042".</summary>
    public string NextNumberPreview { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsArchived { get; set; }
}

public class TransportOrderTypeCreateDto
{
    [Required, MaxLength(ReferenceDataConsts.MaxCodeLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    public LocalizedNameDto Names { get; set; } = new();

    [MaxLength(ReferenceDataConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    public TransportOrderRequiredFields RequiredFields { get; set; }

    /// <summary>At most 10 (design 51).</summary>
    public List<TransportOrderTypeDefaultWaypointDto> DefaultWaypoints { get; set; } = [];

    [Required]
    public NumberSequenceFormatDto Numbering { get; set; } = new();

    /// <summary>Continue numbering at this counter (only upwards); null leaves it.</summary>
    [Range(1, long.MaxValue)]
    public long? NextNumber { get; set; }

    public bool IsArchived { get; set; }
}

public class TransportOrderTypeUpdateDto : TransportOrderTypeCreateDto, IReferenceDataUpdateDto
{
    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public interface ITransportOrderTypeAppService : IApplicationService
{
    Task<GridResponse<TransportOrderTypeRowDto>> GetListAsync(ArchivableListRequestDto input);
    Task<TransportOrderTypeDto> GetAsync(Guid id);
    Task<List<ReferenceDataLookupDto>> GetLookupAsync(string? term = null, int take = ReferenceDataConsts.MaxLookupTake);
    Task<TransportOrderTypeDto> CreateAsync(TransportOrderTypeCreateDto input);
    Task<TransportOrderTypeDto> UpdateAsync(Guid id, TransportOrderTypeUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<ReferenceDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input);
    Task SetDefaultAsync(Guid id);
}
