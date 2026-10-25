using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Fakes;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Relations;
using OpenTms.MasterData.Transport;
using OpenTms.Orders.TransportOrders;
using OpenTms.PostgreSql.ReferenceData;
using Volo.Abp.Domain.Repositories;

namespace OpenTms.PostgreSql.Orders;

/// <summary>
/// Shared plumbing for the transport order tests against real PostgreSQL (design 58): the Master Data rows an order needs
/// (created through the Master Data services inside the test's transaction, with random codes so a committed row never
/// clashes with a later run), the seeded Orders master data of the tenant (LOAD/UNLOAD, MORNING, STD) and the create DTO.
/// </summary>
public abstract class TransportOrdersPostgreSqlTestBase : ReferenceDataPostgreSqlTestBase
{
    protected ITransportOrderAppService Orders => GetRequiredService<ITransportOrderAppService>();

    protected FakeFinancialReferences Financial => GetRequiredService<FakeFinancialReferences>();

    protected DateOnly Today => GetRequiredService<OrdersClock>().Today();

    protected sealed class Fixture
    {
        public Guid CustomerId { get; init; }
        public Guid DepotId { get; init; }
        public Guid DepotAddressId { get; init; }
        public Guid ShopId { get; init; }
        public Guid ShopAddressId { get; init; }
        public Guid TransportUnitId { get; init; }
        public Guid LoadTypeId { get; init; }
        public Guid UnloadTypeId { get; init; }
        public Guid OrderTypeId { get; init; }
        public Guid PriceListId { get; init; }
    }

    /// <summary>The rows of the current tenant; call inside the tenant's transaction.</summary>
    protected async Task<Fixture> FixtureAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var country = await GetRequiredService<IRepository<Country, Guid>>().GetAsync(c => c.IsoAlpha2 == "NL");
        var relations = GetRequiredService<IRelationAppService>();
        var customer = await relations.CreateAsync(Relation("KL" + suffix, "Klant Foods " + suffix, RelationRoles.Customer, country.Id, "Dierensteinweg", "2991 XJ", "Barendrecht"));
        var depot = await relations.CreateAsync(Relation("DP" + suffix, "DC Venlo " + suffix, RelationRoles.Customer, country.Id, "Celsiusweg", "5928 PR", "Venlo"));
        var shop = await relations.CreateAsync(Relation("SH" + suffix, "Supermarkt Utrecht " + suffix, RelationRoles.Customer, country.Id, "Vredenburg", "3511 BD", "Utrecht"));
        var unit = await GetRequiredService<ITransportUnitAppService>().CreateAsync(new TransportUnitCreateDto { Name = "Europallet " + suffix });
        var waypointTypes = await GetRequiredService<IRepository<WaypointType, Guid>>().GetListAsync(w => w.Code == "LOAD" || w.Code == "UNLOAD");
        var type = await GetRequiredService<IRepository<TransportOrderType, Guid>>().GetAsync(t => t.Code == "STD");
        await FlushAsync();

        return new Fixture
        {
            CustomerId = customer.Id,
            DepotId = depot.Id, DepotAddressId = depot.Addresses.Single().Id!.Value,
            ShopId = shop.Id, ShopAddressId = shop.Addresses.Single().Id!.Value,
            TransportUnitId = unit.Id,
            LoadTypeId = waypointTypes.Single(w => w.Code == "LOAD").Id,
            UnloadTypeId = waypointTypes.Single(w => w.Code == "UNLOAD").Id,
            OrderTypeId = type.Id,
            PriceListId = Financial.AddPriceList("Tarieven " + suffix)
        };
    }

    private static RelationCreateDto Relation(string code, string name, RelationRoles roles, Guid countryId, string street, string postalCode, string city) => new()
    {
        Code = code, Name = name, Roles = roles,
        Addresses = [new RelationAddressDto { CountryId = countryId, Street = street, HouseNumber = "1", PostalCode = postalCode, City = city, Latitude = 51.9, Longitude = 4.5 }]
    };

    protected TransportOrderCreateDto CreateDto(Fixture f, bool confirm = false, DateOnly? orderDate = null, string? customerReference = "PO-1") => new()
    {
        TransportOrderTypeId = f.OrderTypeId,
        CustomerRelationId = f.CustomerId,
        OrderDate = orderDate ?? Today,
        CustomerReference = customerReference,
        FinancialTerms = new TransportOrderFinancialTermsInputDto { CurrencyCode = "EUR", PriceListIds = [f.PriceListId] },
        Lines =
        [
            new TransportOrderLineInputDto
            {
                TransportUnitId = f.TransportUnitId, Quantity = 2, WeightKg = 100,
                Stops =
                [
                    new() { WaypointTypeId = f.LoadTypeId, RelationId = f.DepotId, RelationAddressId = f.DepotAddressId, Date = orderDate ?? Today, Reference = "LAAD-1" },
                    new() { WaypointTypeId = f.UnloadTypeId, RelationId = f.ShopId, RelationAddressId = f.ShopAddressId, Date = (orderDate ?? Today).AddDays(1), Reference = "LOS-1" }
                ]
            }
        ],
        Confirm = confirm
    };
}
