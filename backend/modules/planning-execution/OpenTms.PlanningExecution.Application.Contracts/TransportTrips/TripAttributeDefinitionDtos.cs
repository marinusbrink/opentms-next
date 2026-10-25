using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Application.Services;

namespace OpenTms.PlanningExecution.TransportTrips;

/// <summary>Design 56 "Attributes": the tenant's custom trip attributes (design-50 shape, Master data app → Planning &amp; Execution → Trips).</summary>
public class TripAttributeDefinitionRowDto
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Position { get; set; }
    public bool IsArchived { get; set; }
    public string? Description { get; set; }
}

public class TripAttributeDefinitionDto : ReferenceDataDetailDtoBase
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public LocalizedNameDto Names { get; set; } = new();
    public int Position { get; set; }
    public bool IsArchived { get; set; }
}

public class TripAttributeDefinitionCreateDto
{
    [Required, MaxLength(PlanningExecutionConsts.MaxAttributeDefinitionKeyLength)]
    public string Key { get; set; } = string.Empty;

    [Required]
    public LocalizedNameDto Names { get; set; } = new();

    [Range(1, 10_000)]
    public int Position { get; set; } = 1;

    [MaxLength(ReferenceDataConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    public bool IsArchived { get; set; }
}

public class TripAttributeDefinitionUpdateDto : TripAttributeDefinitionCreateDto, IReferenceDataUpdateDto
{
    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public interface ITripAttributeDefinitionAppService : IApplicationService
{
    Task<GridResponse<TripAttributeDefinitionRowDto>> GetListAsync(ArchivableListRequestDto input);
    Task<TripAttributeDefinitionDto> GetAsync(Guid id);
    Task<List<ReferenceDataLookupDto>> GetLookupAsync(string? term = null, int take = ReferenceDataConsts.MaxLookupTake);
    Task<TripAttributeDefinitionDto> CreateAsync(TripAttributeDefinitionCreateDto input);
    Task<TripAttributeDefinitionDto> UpdateAsync(Guid id, TripAttributeDefinitionUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<ReferenceDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input);
}
