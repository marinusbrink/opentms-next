using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OpenTms.MasterData.Transport;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace OpenTms.MasterData.EntityFrameworkCore;

/// <summary>
/// See <see cref="IProductRelationCodeRepository"/>. The performance gate measured the joined sort (every code joined to its
/// product and sorted by the product's name, for one page of 100) at 513 to 530 ms on production's one-vCPU database (budget
/// 300 ms). PostgreSQL now reads the products in name order and, per product, its codes in code order through a
/// <c>LATERAL</c> subquery, so the <c>LIMIT</c> stops the walk once the page is full: 6 ms instead of 160 ms for the first page
/// of 250,000 codes on one core. The codes without a product form their own segment. Any other provider (the SQLite unit
/// tests, which have no <c>LATERAL</c>) keeps the portable joined sort.
/// </summary>
public class EfCoreProductRelationCodeRepository
    : EfCoreRepository<MasterDataDbContext, ProductRelationCode, Guid>, IProductRelationCodeRepository
{
    public EfCoreProductRelationCodeRepository(IDbContextProvider<MasterDataDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public virtual async Task<List<ProductRelationCode>> GetPageByProductNameAsync(
        IQueryable<ProductRelationCode> codes, IQueryable<Product> products, int skip, int take, bool descending)
    {
        skip = Math.Max(0, skip);
        if (take <= 0)
        {
            return [];
        }

        if (!(await GetDbContextAsync()).Database.IsNpgsql())
        {
            return await PortableAsync(codes, products, skip, take, descending);
        }

        var withProduct = codes.Where(c => c.ProductId != null && products.Any(p => p.Id == c.ProductId));
        var withoutProduct = codes.Where(c => c.ProductId == null || !products.Any(p => p.Id == c.ProductId));

        /* Ascending: the codes with a product, then those without. Descending: the other way round (PostgreSQL puts a
         * missing name first when descending, as the joined sort did). */
        var firstIsProducts = !descending;
        var page = new List<ProductRelationCode>();
        page.AddRange(firstIsProducts
            ? await ByProductAsync(products, codes, skip, take, descending)
            : await ByCodeAsync(withoutProduct, skip, take, descending));
        if (page.Count == take)
        {
            return page;
        }

        /* The first segment ran out: the rest of the page comes from the second. Only a page that starts past the first
         * segment's end needs its size to know where in the second segment to begin. */
        var firstSegment = firstIsProducts ? withProduct : withoutProduct;
        var skipInSecond = page.Count > 0 ? 0 : (int)Math.Max(0, skip - await firstSegment.LongCountAsync());
        page.AddRange(firstIsProducts
            ? await ByCodeAsync(withoutProduct, skipInSecond, take - page.Count, descending)
            : await ByProductAsync(products, codes, skipInSecond, take - page.Count, descending));
        return page;
    }

    /// <summary>The codes of the products in name order, each product's codes in code order (PostgreSQL <c>LATERAL</c>).</summary>
    private static async Task<List<ProductRelationCode>> ByProductAsync(
        IQueryable<Product> products, IQueryable<ProductRelationCode> codes, int skip, int take, bool descending)
    {
        /* Each product contributes at most skip + take codes: no page reaches further into one product. */
        var perProduct = skip + take;
        /* The inner projection refers to the outer product, so EF Core keeps the subquery correlated (CROSS JOIN LATERAL)
         * instead of rewriting it as ROW_NUMBER() over every code, which reads them all (measured: 398 to 482 ms). */
        var rows = descending
            ? products.SelectMany(p => codes.Where(c => c.ProductId == p.Id).OrderByDescending(c => c.NormalizedCode).Take(perProduct)
                    .Select(c => new { p.Name, Code = c }))
                .OrderByDescending(r => r.Name).ThenByDescending(r => r.Code.NormalizedCode)
            : products.SelectMany(p => codes.Where(c => c.ProductId == p.Id).OrderBy(c => c.NormalizedCode).Take(perProduct)
                    .Select(c => new { p.Name, Code = c }))
                .OrderBy(r => r.Name).ThenBy(r => r.Code.NormalizedCode);
        return await rows.Skip(skip).Take(take).Select(r => r.Code).ToListAsync();
    }

    private static Task<List<ProductRelationCode>> ByCodeAsync(IQueryable<ProductRelationCode> codes, int skip, int take, bool descending)
        => (descending ? codes.OrderByDescending(c => c.NormalizedCode) : codes.OrderBy(c => c.NormalizedCode))
            .Skip(skip).Take(take).ToListAsync();

    /// <summary>The joined sort, on providers without <c>LATERAL</c>: the codes left-joined to their product, by its name, then code.</summary>
    private static Task<List<ProductRelationCode>> PortableAsync(
        IQueryable<ProductRelationCode> codes, IQueryable<Product> products, int skip, int take, bool descending)
    {
        var joined = codes
            .GroupJoin(products, c => c.ProductId, p => (Guid?)p.Id, (c, matches) => new { c, matches })
            .SelectMany(x => x.matches.DefaultIfEmpty(), (x, p) => new { Code = x.c, Name = p == null ? null : p.Name });
        /* Missing names last ascending and first descending, as on PostgreSQL. */
        var ordered = descending
            ? joined.OrderBy(r => r.Name != null).ThenByDescending(r => r.Name).ThenByDescending(r => r.Code.NormalizedCode)
            : joined.OrderBy(r => r.Name == null).ThenBy(r => r.Name).ThenBy(r => r.Code.NormalizedCode);
        return ordered.Skip(skip).Take(take).Select(r => r.Code).ToListAsync();
    }
}
