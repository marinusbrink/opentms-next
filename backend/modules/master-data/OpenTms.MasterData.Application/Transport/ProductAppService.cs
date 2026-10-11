using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.MasterData.Features;
using OpenTms.MasterData.Incompatibilities;
using OpenTms.MasterData.Permissions;
using OpenTms.Platform.Grid;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;

namespace OpenTms.MasterData.Transport;

[RequiresFeature(MasterDataFeatures.FirstRun)]
public class ProductAppService
    : MasterDataCrudAppServiceBase<Product, ProductRowDto, ProductDto, ProductCreateDto, ProductUpdateDto, ArchivableListRequestDto>,
      IProductAppService
{
    private readonly IRepository<ProductIncompatibility> _pairs;

    public ProductAppService(IRepository<Product, Guid> repository, IRepository<ProductIncompatibility> pairs)
        : base(repository)
    {
        _pairs = pairs;
    }

    [Authorize(MasterDataPermissions.Products.Default)]
    public Task<GridResponse<ProductRowDto>> GetListAsync(ArchivableListRequestDto input) => GetListInternalAsync(input);

    [Authorize(MasterDataPermissions.Products.Default)]
    public Task<ProductDto> GetAsync(Guid id) => GetInternalAsync(id);

    [Authorize(MasterDataPermissions.Products.Default)]
    public Task<List<MasterDataLookupDto>> GetLookupAsync(string? term = null, int take = MasterDataConsts.MaxLookupTake) => GetLookupInternalAsync(term, take);

    [Authorize(MasterDataPermissions.Products.Create)]
    public Task<ProductDto> CreateAsync(ProductCreateDto input) => CreateInternalAsync(input);

    [Authorize(MasterDataPermissions.Products.Update)]
    public Task<ProductDto> UpdateAsync(Guid id, ProductUpdateDto input) => UpdateInternalAsync(id, input);

    [Authorize(MasterDataPermissions.Products.Delete)]
    public Task DeleteAsync(Guid id) => DeleteInternalAsync(id);

    [Authorize(MasterDataPermissions.Products.Delete)]
    public Task<MasterDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input)
        => BulkDeleteInternalAsync(input.Selection, q => input.IncludeArchived ? q : q.Where(p => !p.IsArchived));

    protected override IQueryable<Product> ApplyFilters(IQueryable<Product> query, ArchivableListRequestDto request)
        => request.IncludeArchived ? query : query.Where(p => !p.IsArchived);

    protected override IQueryable<Product> ApplySearch(IQueryable<Product> query, string term)
        => query.Where(p =>
            p.Name.ToLower().Contains(term) ||
            (p.Description != null && p.Description.ToLower().Contains(term)) ||
            (p.AdrUnNumber != null && p.AdrUnNumber.Contains(term)));

    protected override IOrderedQueryable<Product> ApplySort(IQueryable<Product> query, string? sortBy, bool descending)
        => (sortBy?.ToLowerInvariant()) switch
        {
            null or "" or "name" => Order(query, p => p.Name, descending),
            "description" => Order(query, p => p.Description, descending),
            "isarchived" => Order(query, p => p.IsArchived, descending),
            "isadr" => Order(query, p => p.IsAdr, descending),
            "adrunnumber" => Order(query, p => p.AdrUnNumber, descending),
            "adrclass" => Order(query, p => p.AdrClass, descending),
            "adrpackinggroup" => Order(query, p => p.AdrPackingGroup, descending),
            "adrhazardlabels" => Order(query, p => p.AdrHazardLabels, descending),
            "adrtunnelrestrictioncode" => Order(query, p => p.AdrTunnelRestrictionCode, descending),
            "adrtransportcategory" => Order(query, p => p.AdrTransportCategory, descending),
            "adrislimitedquantity" => Order(query, p => p.AdrIsLimitedQuantity, descending),
            "defaultquantity" => Order(query, p => p.DefaultQuantity, descending),
            "defaultweightkg" => Order(query, p => p.DefaultWeightKg, descending),
            "defaultcolli" => Order(query, p => p.DefaultColli, descending),
            "defaultvolumem3" => Order(query, p => p.DefaultVolumeM3, descending),
            "defaultloadingmeters" => Order(query, p => p.DefaultLoadingMeters, descending),
            "defaultepq" => Order(query, p => p.DefaultEpq, descending),
            "defaultlengthcm" => Order(query, p => p.DefaultLengthCm, descending),
            "defaultwidthcm" => Order(query, p => p.DefaultWidthCm, descending),
            "defaultheightcm" => Order(query, p => p.DefaultHeightCm, descending),
            _ => throw UnknownSortColumn(sortBy)
        };

    protected override IQueryable<Product> ApplyLookupFilter(IQueryable<Product> query) => query.Where(p => !p.IsArchived);

    protected override Task<List<ProductRowDto>> ToRowsAsync(List<Product> entities)
        => Task.FromResult(entities.Select(p => new ProductRowDto
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            IsArchived = p.IsArchived,
            IsAdr = p.IsAdr,
            AdrUnNumber = p.AdrUnNumber,
            AdrClass = p.AdrClass,
            AdrPackingGroup = p.AdrPackingGroup,
            AdrHazardLabels = p.AdrHazardLabels,
            AdrTunnelRestrictionCode = p.AdrTunnelRestrictionCode,
            AdrTransportCategory = p.AdrTransportCategory,
            AdrIsLimitedQuantity = p.AdrIsLimitedQuantity,
            DefaultQuantity = p.DefaultQuantity,
            DefaultWeightKg = p.DefaultWeightKg,
            DefaultColli = p.DefaultColli,
            DefaultVolumeM3 = p.DefaultVolumeM3,
            DefaultLoadingMeters = p.DefaultLoadingMeters,
            DefaultEpq = p.DefaultEpq,
            DefaultLengthCm = p.DefaultLengthCm,
            DefaultWidthCm = p.DefaultWidthCm,
            DefaultHeightCm = p.DefaultHeightCm
        }).ToList());

    protected override async Task<ProductDto> ToDtoAsync(Product p)
    {
        var dto = new ProductDto
        {
            Name = p.Name,
            IsArchived = p.IsArchived,
            IsAdr = p.IsAdr,
            AdrUnNumber = p.AdrUnNumber,
            AdrClass = p.AdrClass,
            AdrPackingGroup = p.AdrPackingGroup,
            AdrDescription = p.AdrDescription,
            AdrTechnicalName = p.AdrTechnicalName,
            AdrClassificationCode = p.AdrClassificationCode,
            AdrHazardLabels = p.AdrHazardLabels,
            AdrHazardIdentificationNumber = p.AdrHazardIdentificationNumber,
            AdrTunnelRestrictionCode = p.AdrTunnelRestrictionCode,
            AdrTransportCategory = p.AdrTransportCategory,
            AdrIsEnvironmentallyHazardous = p.AdrIsEnvironmentallyHazardous,
            AdrIsLimitedQuantity = p.AdrIsLimitedQuantity,
            AdrIsWaste = p.AdrIsWaste,
            AdrFlashpointCelsius = p.AdrFlashpointCelsius,
            AdrSpecialProvisions = p.AdrSpecialProvisions,
            DefaultQuantity = p.DefaultQuantity,
            DefaultWeightKg = p.DefaultWeightKg,
            DefaultColli = p.DefaultColli,
            DefaultVolumeM3 = p.DefaultVolumeM3,
            DefaultLoadingMeters = p.DefaultLoadingMeters,
            DefaultEpq = p.DefaultEpq,
            DefaultLengthCm = p.DefaultLengthCm,
            DefaultWidthCm = p.DefaultWidthCm,
            DefaultHeightCm = p.DefaultHeightCm,
            IncompatibleProducts = await GetIncompatiblesAsync(_pairs, p.Id)
        };
        MapAudit(p, dto);
        return dto;
    }

    protected override MasterDataLookupDto ToLookup(Product p) => new() { Id = p.Id, Name = p.Name, Code = p.IsAdr ? p.AdrUnNumber : null };

    protected override string DisplayNameOf(Product p) => p.Name;

    protected override async Task<Product> CreateEntityAsync(ProductCreateDto input)
    {
        var name = input.Name.Trim();
        await EnsureUniqueAsync(p => p.Name == name, "name", name);
        var product = new Product(GuidGenerator.Create(), CurrentTenant.Id, name);
        Apply(product, input);
        return product;
    }

    protected override async Task ApplyUpdateAsync(Product product, ProductUpdateDto input)
    {
        var name = input.Name.Trim();
        if (name != product.Name)
        {
            await EnsureUniqueAsync(p => p.Name == name && p.Id != product.Id, "name", name);
            product.SetName(name);
        }

        Apply(product, input);
    }

    private static void Apply(Product product, ProductCreateDto input)
    {
        product.SetDescription(input.Description);
        product.IsArchived = input.IsArchived;
        product.SetAdr(input.IsAdr, new ProductAdr(
            input.AdrUnNumber,
            input.AdrClass,
            input.AdrPackingGroup,
            input.AdrDescription,
            input.AdrTechnicalName,
            input.AdrClassificationCode,
            input.AdrHazardLabels,
            input.AdrHazardIdentificationNumber,
            input.AdrTunnelRestrictionCode,
            input.AdrTransportCategory,
            input.AdrIsEnvironmentallyHazardous,
            input.AdrIsLimitedQuantity,
            input.AdrIsWaste,
            input.AdrFlashpointCelsius,
            input.AdrSpecialProvisions));
        product.DefaultQuantity = input.DefaultQuantity;
        product.DefaultWeightKg = input.DefaultWeightKg;
        product.DefaultColli = input.DefaultColli;
        product.DefaultVolumeM3 = input.DefaultVolumeM3;
        product.DefaultLoadingMeters = input.DefaultLoadingMeters;
        product.DefaultEpq = input.DefaultEpq;
        product.DefaultLengthCm = input.DefaultLengthCm;
        product.DefaultWidthCm = input.DefaultWidthCm;
        product.DefaultHeightCm = input.DefaultHeightCm;
    }

    protected override Task AfterWriteAsync(Product product, ProductCreateDto input)
        => SyncIncompatibilitiesAsync(_pairs, product.Id, input.IncompatibleProductIds, (tenantId, a, b) => new ProductIncompatibility(tenantId, a, b));

    protected override Task BeforeDeleteAsync(Product product) => RemoveIncompatibilitiesAsync(_pairs, product.Id);
}
