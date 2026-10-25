using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.Orders.Localization;
using OpenTms.Orders.Permissions;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Localization;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;

namespace OpenTms.Orders.TransportOrders;

/// <summary>Design 58 (Q12): the order and line attribute definitions, a Master data list under Orders → Transport orders.</summary>
[RequiresFeature(AppFeatures.MasterData.Default)]
public class OrderAttributeDefinitionAppService
    : ReferenceDataCrudAppServiceBase<OrderAttributeDefinition, OrderAttributeDefinitionRowDto, OrderAttributeDefinitionDto, OrderAttributeDefinitionCreateDto, OrderAttributeDefinitionUpdateDto, OrderAttributeDefinitionListRequestDto>,
      IOrderAttributeDefinitionAppService
{
    public OrderAttributeDefinitionAppService(IRepository<OrderAttributeDefinition, Guid> repository)
        : base(repository)
    {
        LocalizationResource = typeof(OrdersResource);
    }

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.OrderAttributeDefinitions, RequiresAll = true)]
    [Authorize(OrdersPermissions.OrderAttributeDefinitions.Default)]
    public Task<GridResponse<OrderAttributeDefinitionRowDto>> GetListAsync(OrderAttributeDefinitionListRequestDto input) => GetListInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.OrderAttributeDefinitions, RequiresAll = true)]
    [Authorize(OrdersPermissions.OrderAttributeDefinitions.Default)]
    public Task<OrderAttributeDefinitionDto> GetAsync(Guid id) => GetInternalAsync(id);

    [Authorize(OrdersPermissions.OrderAttributeDefinitions.Default)]
    public Task<List<ReferenceDataLookupDto>> GetLookupAsync(string? term = null, int take = ReferenceDataConsts.MaxLookupTake) => GetLookupInternalAsync(term, take);

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.OrderAttributeDefinitions, RequiresAll = true)]
    [Authorize(OrdersPermissions.OrderAttributeDefinitions.Create)]
    public Task<OrderAttributeDefinitionDto> CreateAsync(OrderAttributeDefinitionCreateDto input) => CreateInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.OrderAttributeDefinitions, RequiresAll = true)]
    [Authorize(OrdersPermissions.OrderAttributeDefinitions.Update)]
    public Task<OrderAttributeDefinitionDto> UpdateAsync(Guid id, OrderAttributeDefinitionUpdateDto input) => UpdateInternalAsync(id, input);

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.OrderAttributeDefinitions, RequiresAll = true)]
    [Authorize(OrdersPermissions.OrderAttributeDefinitions.Delete)]
    public Task DeleteAsync(Guid id) => DeleteInternalAsync(id);

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.OrderAttributeDefinitions, RequiresAll = true)]
    [Authorize(OrdersPermissions.OrderAttributeDefinitions.Delete)]
    public Task<ReferenceDataBulkDeleteResultDto> BulkDeleteAsync(OrderAttributeDefinitionBulkDeleteRequestDto input)
        => BulkDeleteInternalAsync(input.Selection, q =>
        {
            q = input.IncludeArchived ? q : q.Where(d => !d.IsArchived);
            return input.Target == null ? q : q.Where(d => d.Target == input.Target.Value);
        });

    protected override IQueryable<OrderAttributeDefinition> ApplyFilters(IQueryable<OrderAttributeDefinition> query, OrderAttributeDefinitionListRequestDto request)
    {
        query = request.IncludeArchived ? query : query.Where(d => !d.IsArchived);
        return request.Target == null ? query : query.Where(d => d.Target == request.Target.Value);
    }

    protected override IQueryable<OrderAttributeDefinition> ApplySearch(IQueryable<OrderAttributeDefinition> query, string term)
    {
        var language = UiLanguage;
        return query.Where(d =>
            d.Key.ToLower().Contains(term) ||
            (LocalizedNameQuery.Text(d.Name, language) ?? "").ToLower().Contains(term) ||
            (LocalizedNameQuery.Text(d.Name, LocalizedName.DefaultCulture) ?? "").ToLower().Contains(term));
    }

    protected override IOrderedQueryable<OrderAttributeDefinition> ApplySort(IQueryable<OrderAttributeDefinition> query, string? sortBy, bool descending)
    {
        var language = UiLanguage;
        return (sortBy?.ToLowerInvariant()) switch
        {
            null or "" => query.OrderBy(d => d.Target).ThenBy(d => d.Position).ThenBy(d => d.Key),
            "target" => Order(query, d => d.Target, descending),
            "key" => Order(query, d => d.Key, descending),
            "name" => Order(query, d => LocalizedNameQuery.Text(d.Name, language) ?? LocalizedNameQuery.Text(d.Name, LocalizedName.DefaultCulture), descending),
            "position" => Order(query, d => d.Position, descending),
            "isarchived" => Order(query, d => d.IsArchived, descending),
            _ => throw UnknownSortColumn(sortBy)
        };
    }

    protected override IQueryable<OrderAttributeDefinition> ApplyLookupFilter(IQueryable<OrderAttributeDefinition> query) => query.Where(d => !d.IsArchived);

    protected override Task<List<OrderAttributeDefinitionRowDto>> ToRowsAsync(List<OrderAttributeDefinition> entities)
        => Task.FromResult(entities.Select(d => new OrderAttributeDefinitionRowDto
        {
            Id = d.Id, Target = d.Target, Key = d.Key, Name = Resolve(d.Name), Position = d.Position, IsArchived = d.IsArchived, Description = d.Description
        }).ToList());

    protected override Task<OrderAttributeDefinitionDto> ToDtoAsync(OrderAttributeDefinition d)
    {
        var dto = new OrderAttributeDefinitionDto { Target = d.Target, Key = d.Key, Name = Resolve(d.Name), Names = ToNamesDto(d.Name), Position = d.Position, IsArchived = d.IsArchived };
        MapAudit(d, dto);
        return Task.FromResult(dto);
    }

    protected override ReferenceDataLookupDto ToLookup(OrderAttributeDefinition d) => new() { Id = d.Id, Name = Resolve(d.Name), Code = d.Key };

    protected override string DisplayNameOf(OrderAttributeDefinition d) => Resolve(d.Name);

    protected override async Task<OrderAttributeDefinition> CreateEntityAsync(OrderAttributeDefinitionCreateDto input)
    {
        var key = OrderAttributeDefinition.NormalizeKey(input.Key);
        await EnsureUniqueAsync(d => d.Target == input.Target && d.Key == key, "key", key);
        var definition = new OrderAttributeDefinition(GuidGenerator.Create(), CurrentTenant.Id, input.Target, key, ToLocalizedName(input.Names), input.Position);
        definition.SetDescription(input.Description);
        definition.SetArchived(input.IsArchived);
        return definition;
    }

    protected override async Task ApplyUpdateAsync(OrderAttributeDefinition definition, OrderAttributeDefinitionUpdateDto input)
    {
        var key = OrderAttributeDefinition.NormalizeKey(input.Key);
        if (key != definition.Key || input.Target != definition.Target)
        {
            await EnsureUniqueAsync(d => d.Target == input.Target && d.Key == key && d.Id != definition.Id, "key", key);
        }

        definition.SetTarget(input.Target);
        definition.SetKey(key);
        definition.SetName(ToLocalizedName(input.Names));
        definition.SetPosition(input.Position);
        definition.SetDescription(input.Description);
        definition.SetArchived(input.IsArchived);
    }
}
