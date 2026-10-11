using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.MasterData.Features;
using OpenTms.MasterData.Permissions;
using OpenTms.Platform.Grid;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;

namespace OpenTms.MasterData.Fleet;

[RequiresFeature(MasterDataFeatures.FirstRun)]
public class TransportCharacteristicsAppService
    : MasterDataCrudAppServiceBase<TransportCharacteristics, TransportCharacteristicsRowDto, TransportCharacteristicsDto, TransportCharacteristicsCreateDto, TransportCharacteristicsUpdateDto, MasterDataListRequestDto>,
      ITransportCharacteristicsAppService
{
    private readonly IRepository<VehicleTypeTransportCharacteristics> _joins;

    public TransportCharacteristicsAppService(IRepository<TransportCharacteristics, Guid> repository, IRepository<VehicleTypeTransportCharacteristics> joins)
        : base(repository)
    {
        _joins = joins;
    }

    [Authorize(MasterDataPermissions.TransportCharacteristics.Default)]
    public Task<GridResponse<TransportCharacteristicsRowDto>> GetListAsync(MasterDataListRequestDto input) => GetListInternalAsync(input);

    [Authorize(MasterDataPermissions.TransportCharacteristics.Default)]
    public Task<TransportCharacteristicsDto> GetAsync(Guid id) => GetInternalAsync(id);

    [Authorize(MasterDataPermissions.TransportCharacteristics.Default)]
    public Task<List<MasterDataLookupDto>> GetLookupAsync(string? term = null, int take = MasterDataConsts.MaxLookupTake) => GetLookupInternalAsync(term, take);

    [Authorize(MasterDataPermissions.TransportCharacteristics.Create)]
    public Task<TransportCharacteristicsDto> CreateAsync(TransportCharacteristicsCreateDto input) => CreateInternalAsync(input);

    [Authorize(MasterDataPermissions.TransportCharacteristics.Update)]
    public Task<TransportCharacteristicsDto> UpdateAsync(Guid id, TransportCharacteristicsUpdateDto input) => UpdateInternalAsync(id, input);

    [Authorize(MasterDataPermissions.TransportCharacteristics.Delete)]
    public Task DeleteAsync(Guid id) => DeleteInternalAsync(id);

    [Authorize(MasterDataPermissions.TransportCharacteristics.Delete)]
    public Task<MasterDataBulkDeleteResultDto> BulkDeleteAsync(MasterDataBulkDeleteRequestDto input)
        => BulkDeleteInternalAsync(input.Selection, q => q);

    protected override IQueryable<TransportCharacteristics> ApplySearch(IQueryable<TransportCharacteristics> query, string term)
        => query.Where(c => c.Code.ToLower().Contains(term) || c.Name.ToLower().Contains(term) || (c.Description != null && c.Description.ToLower().Contains(term)));

    protected override IOrderedQueryable<TransportCharacteristics> ApplySort(IQueryable<TransportCharacteristics> query, string? sortBy, bool descending)
        => (sortBy?.ToLowerInvariant()) switch
        {
            null or "" or "name" => Order(query, c => c.Name, descending),
            "code" => Order(query, c => c.Code, descending),
            "description" => Order(query, c => c.Description, descending),
            _ => throw UnknownSortColumn(sortBy)
        };

    protected override Task<List<TransportCharacteristicsRowDto>> ToRowsAsync(List<TransportCharacteristics> entities)
        => Task.FromResult(entities.Select(c => new TransportCharacteristicsRowDto { Id = c.Id, Code = c.Code, Name = c.Name, Description = c.Description }).ToList());

    protected override Task<TransportCharacteristicsDto> ToDtoAsync(TransportCharacteristics c)
    {
        var dto = new TransportCharacteristicsDto { Code = c.Code, Name = c.Name };
        MapAudit(c, dto);
        return Task.FromResult(dto);
    }

    protected override MasterDataLookupDto ToLookup(TransportCharacteristics c) => new() { Id = c.Id, Name = c.Name, Code = c.Code };

    protected override string DisplayNameOf(TransportCharacteristics c) => c.Code;

    protected override async Task<TransportCharacteristics> CreateEntityAsync(TransportCharacteristicsCreateDto input)
    {
        var code = MasterDataCodes.NormalizeRequired(input.Code, MasterDataConsts.MaxCodeLength, "code");
        await EnsureUniqueAsync(c => c.Code == code, "code", code);
        var entity = new TransportCharacteristics(GuidGenerator.Create(), CurrentTenant.Id, code, input.Name);
        entity.SetDescription(input.Description);
        return entity;
    }

    protected override async Task ApplyUpdateAsync(TransportCharacteristics entity, TransportCharacteristicsUpdateDto input)
    {
        var code = MasterDataCodes.NormalizeRequired(input.Code, MasterDataConsts.MaxCodeLength, "code");
        if (code != entity.Code)
        {
            await EnsureUniqueAsync(c => c.Code == code && c.Id != entity.Id, "code", code);
            entity.SetCode(code);
        }

        entity.SetName(input.Name);
        entity.SetDescription(input.Description);
    }

    /// <summary>The join rows cascade in the database but not on a soft delete, so they go explicitly.</summary>
    protected override Task BeforeDeleteAsync(TransportCharacteristics entity)
        => _joins.DeleteAsync(j => j.TransportCharacteristicsId == entity.Id);
}
