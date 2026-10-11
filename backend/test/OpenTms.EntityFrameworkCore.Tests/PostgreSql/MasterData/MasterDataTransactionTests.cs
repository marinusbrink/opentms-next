using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Transport;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace OpenTms.PostgreSql.MasterData;

/// <summary>
/// What the service's own transactional unit of work guarantees on PostgreSQL (the SQLite module
/// runs without transactions): a failed command leaves nothing behind. Risk class: Critical for the
/// single default (design 41, "SetDefault swaps atomically"), High for the create path.
/// The services run in their own units of work here, so the rows they commit are removed afterwards.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class MasterDataTransactionTests : MasterDataPostgreSqlTestBase
{
    private readonly ICurrencyAppService _currencies;
    private readonly IProductAppService _products;
    private readonly IRepository<Currency, Guid> _currencyRepository;
    private readonly IRepository<Product, Guid> _productRepository;

    public MasterDataTransactionTests()
    {
        _currencies = GetRequiredService<ICurrencyAppService>();
        _products = GetRequiredService<IProductAppService>();
        _currencyRepository = GetRequiredService<IRepository<Currency, Guid>>();
        _productRepository = GetRequiredService<IRepository<Product, Guid>>();
    }

    [Fact]
    public async Task A_failed_SetDefault_leaves_the_previous_default_in_place()
    {
        using var _ = CurrentTenant.Change(TenantA);
        CurrencyDto inactive = null!;
        try
        {
            inactive = await _currencies.CreateAsync(new CurrencyCreateDto { IsoCode = "QQT", Names = Names("Transaction probe"), IsActive = false });

            var ex = await Should.ThrowAsync<BusinessException>(() => _currencies.SetDefaultAsync(inactive.Id));
            ex.Code.ShouldBe(MasterDataErrorCodes.DefaultMustBeActive);

            var rows = (await _currencies.GetListAsync(List<CurrencyListRequestDto>(end: 200))).Rows;
            rows.Count(r => r.IsDefault).ShouldBe(1);
            rows.Single(r => r.IsDefault).IsoCode.ShouldBe("EUR");
            (await _currencies.GetAsync(inactive.Id)).IsDefault.ShouldBeFalse();
        }
        finally
        {
            if (inactive != null)
            {
                await WithUnitOfWorkAsync(async () =>
                {
                    var row = await _currencyRepository.FindAsync(inactive.Id);
                    if (row != null)
                    {
                        await _currencyRepository.HardDeleteAsync(row);
                    }
                });
            }
        }
    }

    [Fact]
    public async Task A_create_that_fails_on_a_reference_leaves_no_row_behind()
    {
        using var _ = CurrentTenant.Change(TenantA);
        try
        {
            var ex = await Should.ThrowAsync<BusinessException>(() => _products.CreateAsync(new ProductCreateDto
            {
                Name = "Transaction probe product",
                IncompatibleProductIds = [Guid.NewGuid()]
            }));
            ex.Code.ShouldBe(MasterDataErrorCodes.ReferenceNotFound);

            (await _products.GetListAsync(List<ArchivableListRequestDto>(search: "transaction probe"))).Rows.ShouldBeEmpty();
            await WithUnitOfWorkAsync(async () => (await _productRepository.AnyAsync(p => p.Name == "Transaction probe product")).ShouldBeFalse());
        }
        finally
        {
            await WithUnitOfWorkAsync(async () =>
            {
                foreach (var row in await _productRepository.GetListAsync(p => p.Name == "Transaction probe product"))
                {
                    await _productRepository.HardDeleteAsync(row);
                }
            });
        }
    }
}
