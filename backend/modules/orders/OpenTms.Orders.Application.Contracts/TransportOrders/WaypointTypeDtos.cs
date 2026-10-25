using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Application.Services;

namespace OpenTms.Orders.TransportOrders;

public class WaypointTypeRowDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public WaypointActivity Activity { get; set; }
    public string? SuccessiveWaypointTypeName { get; set; }
    /// <summary>The Master Data relation category's name; "(not found)" when it was deleted there.</summary>
    public string? RelationCategoryName { get; set; }
    /// <summary>The Master Data relation's name; "(not found)" when it was deleted there.</summary>
    public string? DefaultRelationName { get; set; }
    public int? DefaultDurationMinutes { get; set; }
    public bool IsArchived { get; set; }
    public string? Description { get; set; }
}

public class WaypointTypeFleetManagementCodeDto
{
    [Required, MaxLength(OrdersConsts.MaxFleetManagementVendorLength)]
    public string Vendor { get; set; } = string.Empty;

    [Required, MaxLength(OrdersConsts.MaxFleetManagementCodeLength)]
    public string Code { get; set; } = string.Empty;
}

public class WaypointTypeDto : ReferenceDataDetailDtoBase
{
    public string Code { get; set; } = string.Empty;
    /// <summary>The name in the request culture.</summary>
    public string Name { get; set; } = string.Empty;
    public LocalizedNameDto Names { get; set; } = new();
    public WaypointActivity Activity { get; set; }
    public Guid? SuccessiveWaypointTypeId { get; set; }
    public string? SuccessiveWaypointTypeName { get; set; }
    public Guid? RelationCategoryId { get; set; }
    public string? RelationCategoryName { get; set; }
    public Guid? DefaultRelationId { get; set; }
    public string? DefaultRelationName { get; set; }
    public int? DefaultDurationMinutes { get; set; }
    public List<WaypointTypeFleetManagementCodeDto> FleetManagementCodes { get; set; } = [];
    public bool IsArchived { get; set; }
}

public class WaypointTypeCreateDto
{
    [Required, MaxLength(ReferenceDataConsts.MaxCodeLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    public LocalizedNameDto Names { get; set; } = new();

    [MaxLength(ReferenceDataConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    public WaypointActivity Activity { get; set; } = WaypointActivity.Other;

    public Guid? SuccessiveWaypointTypeId { get; set; }

    public Guid? RelationCategoryId { get; set; }

    public Guid? DefaultRelationId { get; set; }

    [Range(0, OrdersConsts.MaxDefaultDurationMinutes)]
    public int? DefaultDurationMinutes { get; set; }

    /// <summary>At most 20, one per vendor (design 51).</summary>
    public List<WaypointTypeFleetManagementCodeDto> FleetManagementCodes { get; set; } = [];

    public bool IsArchived { get; set; }
}

public class WaypointTypeUpdateDto : WaypointTypeCreateDto, IReferenceDataUpdateDto
{
    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public interface IWaypointTypeAppService : IApplicationService
{
    Task<GridResponse<WaypointTypeRowDto>> GetListAsync(ArchivableListRequestDto input);
    Task<WaypointTypeDto> GetAsync(Guid id);
    Task<List<ReferenceDataLookupDto>> GetLookupAsync(string? term = null, int take = ReferenceDataConsts.MaxLookupTake);
    Task<WaypointTypeDto> CreateAsync(WaypointTypeCreateDto input);
    Task<WaypointTypeDto> UpdateAsync(Guid id, WaypointTypeUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<ReferenceDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input);
}
