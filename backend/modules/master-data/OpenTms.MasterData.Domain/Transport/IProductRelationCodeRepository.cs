using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace OpenTms.MasterData.Transport;

public interface IProductRelationCodeRepository : IRepository<ProductRelationCode, Guid>
{
    /// <summary>
    /// One page of <paramref name="codes"/> sorted by the name of their product (then by code), the codes without a product
    /// (or whose product <paramref name="products"/> does not hold) after the others ascending and before them descending.
    /// On PostgreSQL the page walks the products in name order and reads each product's codes through its index, so a page
    /// costs what it returns rather than a sort of every code (the code list's "sorted by product name" read).
    /// </summary>
    Task<List<ProductRelationCode>> GetPageByProductNameAsync(
        IQueryable<ProductRelationCode> codes, IQueryable<Product> products, int skip, int take, bool descending);
}
