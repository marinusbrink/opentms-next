using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace OpenTms.Integrations.AiConnector;

/// <summary>
/// The nine read tools of the AI connector (design 74 "Tool contract"), in-process: <c>/mcp</c> in the host calls these and
/// has no HTTP route of its own here. Each result is an allowlist DTO that names its fields; a field added to a source DTO
/// stays out until a design adds it. Each call runs through the other module's published service, so its permission,
/// feature and tenant filter apply.
/// </summary>
public interface IAiConnectorToolService : IApplicationService
{
    Task<AiToolList<AiOrderRow>> SearchOrdersAsync(SearchOrdersInput input);

    Task<AiOrderDetail> GetOrderAsync(GetOrderInput input);

    Task<AiToolList<AiTripRow>> SearchTripsAsync(SearchTripsInput input);

    Task<AiTripDetail> GetTripAsync(GetTripInput input);

    Task<AiToolList<AiLegRow>> GetLegsAsync(GetLegsInput input);

    Task<AiToolList<AiRelationRow>> SearchRelationsAsync(SearchRelationsInput input);

    Task<AiToolList<AiVehicleRow>> SearchVehiclesAsync(SearchVehiclesInput input);

    Task<AiOrderRates> GetOrderRatesAsync(GetOrderRatesInput input);

    Task<AiCustomerRevenue> GetCustomerRevenueAsync(GetCustomerRevenueInput input);
}
