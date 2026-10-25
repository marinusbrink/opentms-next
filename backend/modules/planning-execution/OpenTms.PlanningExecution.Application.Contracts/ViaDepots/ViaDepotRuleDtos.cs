using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.PlanningExecution.Legs;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Application.Services;

namespace OpenTms.PlanningExecution.ViaDepots;

/// <summary>Design 59 "ViaDepotRule", listed in the Master data app under Planning &amp; Execution → Trips (design-50 shape).</summary>
public class ViaDepotRuleRowDto
{
    public Guid Id { get; set; }
    public ViaDepotScope Scope { get; set; }
    public Guid? RelationId { get; set; }
    public string? RelationName { get; set; }
    public Guid? GeographicalRegionId { get; set; }
    public string? GeographicalRegionName { get; set; }
    public Guid DepotRelationId { get; set; }
    public string? DepotRelationName { get; set; }
    public Guid DepotAddressId { get; set; }
    public string? DepotAddressCity { get; set; }
    public Guid ArrivalWaypointTypeId { get; set; }
    public Guid? DepartureWaypointTypeId { get; set; }
    public bool Overnight { get; set; }
    public int Position { get; set; }
    public bool IsArchived { get; set; }
    public string? Description { get; set; }
}

public class ViaDepotRuleDto : ReferenceDataDetailDtoBase
{
    public ViaDepotScope Scope { get; set; }
    public Guid? RelationId { get; set; }
    public string? RelationName { get; set; }
    public Guid? GeographicalRegionId { get; set; }
    public string? GeographicalRegionName { get; set; }
    public Guid DepotRelationId { get; set; }
    public string? DepotRelationName { get; set; }
    public Guid DepotAddressId { get; set; }
    public string? DepotAddressCity { get; set; }
    public Guid ArrivalWaypointTypeId { get; set; }
    public Guid? DepartureWaypointTypeId { get; set; }
    public bool Overnight { get; set; }
    public int Position { get; set; }
    public bool IsArchived { get; set; }
}

public class ViaDepotRuleCreateDto
{
    public ViaDepotScope Scope { get; set; } = ViaDepotScope.Relation;

    public Guid? RelationId { get; set; }

    public Guid? GeographicalRegionId { get; set; }

    [Required]
    public Guid DepotRelationId { get; set; }

    [Required]
    public Guid DepotAddressId { get; set; }

    [Required]
    public Guid ArrivalWaypointTypeId { get; set; }

    public Guid? DepartureWaypointTypeId { get; set; }

    public bool Overnight { get; set; }

    [Range(1, 10_000)]
    public int Position { get; set; } = 1;

    [MaxLength(ReferenceDataConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    public bool IsArchived { get; set; }
}

public class ViaDepotRuleUpdateDto : ViaDepotRuleCreateDto, IReferenceDataUpdateDto
{
    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public interface IViaDepotRuleAppService : IApplicationService
{
    Task<GridResponse<ViaDepotRuleRowDto>> GetListAsync(ArchivableListRequestDto input);
    Task<ViaDepotRuleDto> GetAsync(Guid id);
    Task<ViaDepotRuleDto> CreateAsync(ViaDepotRuleCreateDto input);
    Task<ViaDepotRuleDto> UpdateAsync(Guid id, ViaDepotRuleUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<ReferenceDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input);
}
