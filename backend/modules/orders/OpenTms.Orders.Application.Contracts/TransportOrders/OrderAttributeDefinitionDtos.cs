using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Application.Services;

namespace OpenTms.Orders.TransportOrders;

public class OrderAttributeDefinitionListRequestDto : ArchivableListRequestDto
{
    public OrderAttributeTarget? Target { get; set; }
}

public class OrderAttributeDefinitionRowDto
{
    public Guid Id { get; set; }
    public OrderAttributeTarget Target { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Position { get; set; }
    public bool IsArchived { get; set; }
    public string? Description { get; set; }
}

public class OrderAttributeDefinitionCreateDto
{
    public OrderAttributeTarget Target { get; set; } = OrderAttributeTarget.Order;

    [Required, MaxLength(OrdersConsts.MaxAttributeKeyLength)]
    public string Key { get; set; } = string.Empty;

    [Required]
    public LocalizedNameDto Names { get; set; } = new();

    [Range(1, 1000)]
    public int Position { get; set; } = 1;

    [MaxLength(ReferenceDataConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    public bool IsArchived { get; set; }
}

public class OrderAttributeDefinitionUpdateDto : OrderAttributeDefinitionCreateDto, IReferenceDataUpdateDto
{
    [Required] public string ConcurrencyStamp { get; set; } = string.Empty;
}

public class OrderAttributeDefinitionDto : ReferenceDataDetailDtoBase
{
    public OrderAttributeTarget Target { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public LocalizedNameDto Names { get; set; } = new();
    public int Position { get; set; }
    public bool IsArchived { get; set; }
}

public class OrderAttributeDefinitionBulkDeleteRequestDto : ArchivableBulkDeleteRequestDto
{
    public OrderAttributeTarget? Target { get; set; }
}

public interface IOrderAttributeDefinitionAppService : IApplicationService
{
    Task<GridResponse<OrderAttributeDefinitionRowDto>> GetListAsync(OrderAttributeDefinitionListRequestDto input);
    Task<OrderAttributeDefinitionDto> GetAsync(Guid id);
    Task<List<ReferenceDataLookupDto>> GetLookupAsync(string? term = null, int take = ReferenceDataConsts.MaxLookupTake);
    Task<OrderAttributeDefinitionDto> CreateAsync(OrderAttributeDefinitionCreateDto input);
    Task<OrderAttributeDefinitionDto> UpdateAsync(Guid id, OrderAttributeDefinitionUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<ReferenceDataBulkDeleteResultDto> BulkDeleteAsync(OrderAttributeDefinitionBulkDeleteRequestDto input);
}
