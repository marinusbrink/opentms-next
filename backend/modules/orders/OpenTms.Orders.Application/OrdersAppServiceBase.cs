using OpenTms.Orders.Localization;
using Volo.Abp.Application.Services;

namespace OpenTms.Orders;

/* Inherit the Orders module's application services from this class. */
public abstract class OrdersAppServiceBase : ApplicationService
{
    protected OrdersAppServiceBase()
    {
        LocalizationResource = typeof(OrdersResource);
    }
}
