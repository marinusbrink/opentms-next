using System;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace OpenTms.Orders.Intake;

/// <summary>The intake repository with the review list's search (design 72 "Orders API", review M2).</summary>
public interface IOrderIntakeRepository : IRepository<OrderIntake, Guid>
{
    /// <summary>
    /// <paramref name="query"/> narrowed to the intakes whose sender, subject, file name or customer reference contains
    /// <paramref name="term"/>, case-insensitively. On PostgreSQL an <c>ILIKE '%term%'</c> on the raw columns, so their
    /// trigram indexes serve it; the term's wildcard characters are matched literally.
    /// </summary>
    Task<IQueryable<OrderIntake>> WhereSearchMatchesAsync(IQueryable<OrderIntake> query, string term);
}
