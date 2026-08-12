namespace OpenTms.Orders;

public static class OrdersDbProperties
{
    /// <summary>PostgreSQL schema owned by the Orders module. One schema per module; no other module may touch it.</summary>
    public const string DbSchema = "orders";

    /// <summary>Named connection string; falls back to "Default" (ABP resolution), so database-per-tenant works unchanged.</summary>
    public const string ConnectionStringName = "Orders";
}
