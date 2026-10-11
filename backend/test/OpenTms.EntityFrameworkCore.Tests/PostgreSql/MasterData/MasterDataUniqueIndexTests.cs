using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Relations;
using OpenTms.Platform.Localization;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Xunit;

namespace OpenTms.PostgreSql.MasterData;

/// <summary>
/// The unique and partial unique indexes of the masterdata schema, exercised through the repository
/// so the application service's friendly check is bypassed (design 41, "Uniqueness and the single
/// default"). Risk class: Critical — the index is the one enforced place for the natural keys, the
/// single default currency and the one-Visiting-one-Postal rule.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class MasterDataUniqueIndexTests : MasterDataPostgreSqlTestBase
{
    private readonly IRepository<Currency, Guid> _currencies;
    private readonly IRepository<Country, Guid> _countries;
    private readonly IRepository<AddressType, Guid> _addressTypes;
    private readonly IRepository<RelationCategory, Guid> _categories;
    private readonly IGuidGenerator _guids;

    public MasterDataUniqueIndexTests()
    {
        _currencies = GetRequiredService<IRepository<Currency, Guid>>();
        _countries = GetRequiredService<IRepository<Country, Guid>>();
        _addressTypes = GetRequiredService<IRepository<AddressType, Guid>>();
        _categories = GetRequiredService<IRepository<RelationCategory, Guid>>();
        _guids = GetRequiredService<IGuidGenerator>();
    }

    private static LocalizedName Name(string text) => LocalizedName.Create("en", text);

    private static async Task<PostgresException> ShouldViolateUniqueIndexAsync(Func<Task> insert, string indexName)
    {
        var ex = await Should.ThrowAsync<DbUpdateException>(insert);
        var postgres = ex.InnerException.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        postgres.ConstraintName.ShouldBe(indexName);
        return postgres;
    }

    [Fact]
    public async Task A_duplicate_currency_code_is_refused_by_the_index()
    {
        await InRolledBackTransactionAsync(TenantA, () => ShouldViolateUniqueIndexAsync(
            () => _currencies.InsertAsync(new Currency(_guids.Create(), TenantA, "EUR", Name("Euro again"), true), autoSave: true),
            "IX_Currencies_TenantId_IsoCode"));
    }

    [Fact]
    public async Task A_second_default_currency_is_refused_by_the_partial_index()
    {
        await InRolledBackTransactionAsync(TenantA, () => ShouldViolateUniqueIndexAsync(
            () =>
            {
                var second = new Currency(_guids.Create(), TenantA, "QQD", Name("Second default"), true);
                second.MarkDefault();
                return _currencies.InsertAsync(second, autoSave: true);
            },
            "IX_Currencies_TenantId_Default"));
    }

    [Fact]
    public async Task A_soft_deleted_currency_frees_its_code_for_the_index()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var first = new Currency(_guids.Create(), TenantA, "QQZ", Name("First"), true);
            await _currencies.InsertAsync(first, autoSave: true);
            await _currencies.DeleteAsync(first, autoSave: true);

            var second = new Currency(_guids.Create(), TenantA, "QQZ", Name("Second"), true);
            await _currencies.InsertAsync(second, autoSave: true);

            (await _currencies.GetAsync(c => c.IsoCode == "QQZ")).Id.ShouldBe(second.Id);
        });
    }

    [Fact]
    public async Task A_second_inactive_non_default_currency_with_a_new_code_is_fine()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            await _currencies.InsertAsync(new Currency(_guids.Create(), TenantA, "QQA", Name("A"), false), autoSave: true);
            await _currencies.InsertAsync(new Currency(_guids.Create(), TenantA, "QQB", Name("B"), false), autoSave: true);

            (await _currencies.GetCountAsync()).ShouldBe(180);
        });
    }

    [Fact]
    public async Task Duplicate_country_codes_are_refused_by_their_indexes()
    {
        await InRolledBackTransactionAsync(TenantA, () => ShouldViolateUniqueIndexAsync(
            () => _countries.InsertAsync(new Country(_guids.Create(), TenantA, "NL", "QQQ", Name("Clash")), autoSave: true),
            "IX_Countries_TenantId_IsoAlpha2"));

        await InRolledBackTransactionAsync(TenantA, () => ShouldViolateUniqueIndexAsync(
            () => _countries.InsertAsync(new Country(_guids.Create(), TenantA, "QQ", "NLD", Name("Clash")), autoSave: true),
            "IX_Countries_TenantId_IsoAlpha3"));
    }

    [Fact]
    public async Task A_second_visiting_address_type_is_refused_but_other_kinds_are_unbounded()
    {
        await InRolledBackTransactionAsync(TenantA, () => ShouldViolateUniqueIndexAsync(
            () => _addressTypes.InsertAsync(new AddressType(_guids.Create(), TenantA, AddressTypeKind.Visiting, Name("Second visiting")), autoSave: true),
            "IX_AddressTypes_TenantId_Kind"));

        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            await _addressTypes.InsertAsync(new AddressType(_guids.Create(), TenantA, AddressTypeKind.Other, Name("Warehouse")), autoSave: true);
            await _addressTypes.InsertAsync(new AddressType(_guids.Create(), TenantA, AddressTypeKind.Other, Name("Warehouse")), autoSave: true);
            (await _addressTypes.GetCountAsync()).ShouldBe(4);
        });
    }

    [Fact]
    public async Task A_duplicate_name_is_refused_by_the_index_in_the_shared_tenant_too()
    {
        await InRolledBackTransactionAsync(SharedTenant, async () =>
        {
            await _categories.InsertAsync(new RelationCategory(_guids.Create(), SharedTenant, "Customers"), autoSave: true);
            await ShouldViolateUniqueIndexAsync(
                () => _categories.InsertAsync(new RelationCategory(_guids.Create(), SharedTenant, "Customers"), autoSave: true),
                "IX_RelationCategories_TenantId_Name");
        });
    }

    [Fact]
    public async Task The_same_name_may_exist_in_two_tenants_of_the_same_database()
    {
        /* The shared tenant and the host share the host database; the index is per tenant. */
        await InRolledBackTransactionAsync(SharedTenant, async () =>
        {
            await _categories.InsertAsync(new RelationCategory(_guids.Create(), SharedTenant, "Customers"), autoSave: true);

            using (CurrentTenant.Change(null))
            {
                await _categories.InsertAsync(new RelationCategory(_guids.Create(), null, "Customers"), autoSave: true);
                (await _categories.GetCountAsync()).ShouldBe(1);
            }

            (await _categories.GetCountAsync()).ShouldBe(1);
        });
    }
}
