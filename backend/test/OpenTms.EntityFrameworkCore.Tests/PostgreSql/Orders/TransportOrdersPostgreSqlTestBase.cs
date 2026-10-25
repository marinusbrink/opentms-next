using System;
using System.Threading.Tasks;
using OpenTms.Orders.TransportOrders;
using OpenTms.PostgreSql.ReferenceData;
using OpenTms.TransportRun;

namespace OpenTms.PostgreSql.Orders;

/// <summary>
/// Shared plumbing for the transport order tests against real PostgreSQL (design 58): the world of
/// <see cref="TransportRunFixture"/> (Master Data, Orders, Planning and Financial rows created through the real services
/// inside the test's transaction, with suffixed codes so a committed row never clashes with a later run) and the create DTO.
/// </summary>
public abstract class TransportOrdersPostgreSqlTestBase : ReferenceDataPostgreSqlTestBase
{
    protected ITransportOrderAppService Orders => GetRequiredService<ITransportOrderAppService>();

    protected TransportRunFixture Run => GetRequiredService<TransportRunFixture>();

    protected DateOnly Today => Run.Today;

    /// <summary>The rows of the current tenant; call inside the tenant's transaction. Unique names for rows that commit.</summary>
    protected async Task<TransportRunWorld> FixtureAsync(bool uniqueNames = false)
    {
        var world = await Run.BuildAsync(new TransportRunWorldOptions { UniqueNames = uniqueNames });
        await FlushAsync();
        return world;
    }

    protected TransportOrderCreateDto CreateDto(TransportRunWorld f, bool confirm = false, DateOnly? orderDate = null, string? customerReference = "PO-1")
        => Run.CreateDto(f, confirm, 1, customerReference, orderDate);
}
