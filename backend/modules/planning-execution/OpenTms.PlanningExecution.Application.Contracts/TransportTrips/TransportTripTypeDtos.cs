using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Application.Services;

namespace OpenTms.PlanningExecution.TransportTrips;

public class TransportTripTypeRowDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public TripExecution DefaultExecution { get; set; }
    public bool SendToFleetManagementSystem { get; set; }
    /// <summary>The format applied to counter 1 today, e.g. "TR-2026-00001".</summary>
    public string NumberingExample { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsArchived { get; set; }
    public string? Description { get; set; }
}

public class TransportTripTypeDto : ReferenceDataDetailDtoBase
{
    public string Code { get; set; } = string.Empty;
    /// <summary>The name in the request culture.</summary>
    public string Name { get; set; } = string.Empty;
    public LocalizedNameDto Names { get; set; } = new();
    public TripExecution DefaultExecution { get; set; }
    public bool SendToFleetManagementSystem { get; set; }
    public NumberSequenceFormatDto Numbering { get; set; } = new();
    /// <summary>The counter the next trip of this type takes.</summary>
    public long NextNumber { get; set; }
    /// <summary>The number the next trip of this type gets, e.g. "TR-2026-00042".</summary>
    public string NextNumberPreview { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsArchived { get; set; }
}

public class TransportTripTypeCreateDto
{
    [Required, MaxLength(ReferenceDataConsts.MaxCodeLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    public LocalizedNameDto Names { get; set; } = new();

    [MaxLength(ReferenceDataConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    public TripExecution DefaultExecution { get; set; } = TripExecution.OwnFleet;

    public bool SendToFleetManagementSystem { get; set; } = true;

    [Required]
    public NumberSequenceFormatDto Numbering { get; set; } = new();

    /// <summary>Continue numbering at this counter (only upwards); null leaves it.</summary>
    [Range(1, long.MaxValue)]
    public long? NextNumber { get; set; }

    public bool IsArchived { get; set; }
}

public class TransportTripTypeUpdateDto : TransportTripTypeCreateDto, IReferenceDataUpdateDto
{
    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public interface ITransportTripTypeAppService : IApplicationService
{
    Task<GridResponse<TransportTripTypeRowDto>> GetListAsync(ArchivableListRequestDto input);
    Task<TransportTripTypeDto> GetAsync(Guid id);
    Task<List<ReferenceDataLookupDto>> GetLookupAsync(string? term = null, int take = ReferenceDataConsts.MaxLookupTake);
    Task<TransportTripTypeDto> CreateAsync(TransportTripTypeCreateDto input);
    Task<TransportTripTypeDto> UpdateAsync(Guid id, TransportTripTypeUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<ReferenceDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input);
    Task SetDefaultAsync(Guid id);
}
