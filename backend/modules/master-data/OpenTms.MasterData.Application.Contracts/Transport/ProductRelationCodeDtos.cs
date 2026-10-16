using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using Volo.Abp.Application.Services;

namespace OpenTms.MasterData.Transport;

public class ProductRelationCodeRowDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public Guid? RelationId { get; set; }
    public string? RelationName { get; set; }
    public string? RelationCity { get; set; }
    public string? ProductName { get; set; }
    public string? TransportUnitName { get; set; }
    public bool IsArchived { get; set; }
}

public class ProductRelationCodeDto : MasterDataDetailDtoBase
{
    public string Code { get; set; } = string.Empty;
    public Guid? RelationId { get; set; }
    public string? RelationCode { get; set; }
    public string? RelationName { get; set; }
    public string? RelationCity { get; set; }
    public Guid? ProductId { get; set; }
    public string? ProductName { get; set; }
    public Guid? TransportUnitId { get; set; }
    public string? TransportUnitName { get; set; }
    public bool IsArchived { get; set; }
}

public class ProductRelationCodeCreateDto
{
    [Required, MaxLength(MasterDataConsts.MaxProductRelationCodeLength)]
    public string Code { get; set; } = string.Empty;

    /// <summary>The destination relation (design 48, assumption 4).</summary>
    public Guid? RelationId { get; set; }

    public Guid? ProductId { get; set; }

    public Guid? TransportUnitId { get; set; }

    public bool IsArchived { get; set; }

    [MaxLength(MasterDataConsts.MaxDescriptionLength)]
    public string? Description { get; set; }
}

public class ProductRelationCodeUpdateDto : ProductRelationCodeCreateDto, IMasterDataUpdateDto
{
    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public interface IProductRelationCodeAppService : IApplicationService
{
    Task<GridResponse<ProductRelationCodeRowDto>> GetListAsync(ArchivableListRequestDto input);
    Task<ProductRelationCodeDto> GetAsync(Guid id);
    Task<List<MasterDataLookupDto>> GetLookupAsync(string? term = null, int take = MasterDataConsts.MaxLookupTake);
    Task<ProductRelationCodeDto> CreateAsync(ProductRelationCodeCreateDto input);
    Task<ProductRelationCodeDto> UpdateAsync(Guid id, ProductRelationCodeUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<MasterDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input);
    Task ArchiveAsync(Guid id);
    Task UnarchiveAsync(Guid id);
}

/// <summary>A code that matched: the stored code and what it maps to.</summary>
public class ProductRelationCodeMatchDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NormalizedCode { get; set; } = string.Empty;
    public Guid? RelationId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? TransportUnitId { get; set; }
}

/// <summary>
/// The published resolver the Orders import calls (design 48, §3.15 CHANGE): returns the non-archived codes that
/// match the given codes by normalised code (trimmed, upper case). It never creates anything; an unknown code is
/// simply absent from the result. At most <see cref="MasterDataConsts.MaxResolverCodes"/> codes per call. A
/// synchronous call into Master Data is a design decision of the calling module (module rule 3).
/// </summary>
public interface IProductRelationCodeResolver
{
    Task<List<ProductRelationCodeMatchDto>> ResolveAsync(IReadOnlyCollection<string> codes);
}
