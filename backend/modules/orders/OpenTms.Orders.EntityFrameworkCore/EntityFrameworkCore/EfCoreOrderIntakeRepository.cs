using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OpenTms.Orders.Intake;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace OpenTms.Orders.EntityFrameworkCore;

/// <summary>
/// See <see cref="IOrderIntakeRepository"/>. PostgreSQL searches with <c>ILIKE</c> on the columns that carry the trigram
/// indexes (<c>IX_OrderIntakes_*_Trigram</c>); a <c>lower(column) LIKE</c> could not use them. Any other provider (the
/// SQLite unit tests) keeps the portable lower-case contains.
/// </summary>
public class EfCoreOrderIntakeRepository : EfCoreRepository<OrdersDbContext, OrderIntake, Guid>, IOrderIntakeRepository
{
    public EfCoreOrderIntakeRepository(IDbContextProvider<OrdersDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public virtual async Task<IQueryable<OrderIntake>> WhereSearchMatchesAsync(IQueryable<OrderIntake> query, string term)
    {
        var text = term.Trim();
        if (text.Length == 0)
        {
            return query;
        }

        if ((await GetDbContextAsync()).Database.IsNpgsql())
        {
            var pattern = OrdersSearchPattern.Contains(text);
            return query.Where(i =>
                EF.Functions.ILike(i.SenderAddress!, pattern, OrdersSearchPattern.Escape) ||
                EF.Functions.ILike(i.Subject!, pattern, OrdersSearchPattern.Escape) ||
                EF.Functions.ILike(i.FileName!, pattern, OrdersSearchPattern.Escape) ||
                EF.Functions.ILike(i.CustomerReference!, pattern, OrdersSearchPattern.Escape));
        }

        var lowered = text.ToLowerInvariant();
        return query.Where(i =>
            (i.SenderAddress != null && i.SenderAddress.ToLower().Contains(lowered)) ||
            (i.Subject != null && i.Subject.ToLower().Contains(lowered)) ||
            (i.FileName != null && i.FileName.ToLower().Contains(lowered)) ||
            (i.CustomerReference != null && i.CustomerReference.ToLower().Contains(lowered)));
    }
}

/// <summary>A <c>LIKE</c>/<c>ILIKE</c> "contains" pattern with the term's own wildcards matched literally.</summary>
internal static class OrdersSearchPattern
{
    public const string Escape = "\\";

    public static string Contains(string term)
        => "%" + term.Replace(Escape, Escape + Escape, StringComparison.Ordinal)
                     .Replace("%", Escape + "%", StringComparison.Ordinal)
                     .Replace("_", Escape + "_", StringComparison.Ordinal) + "%";
}
