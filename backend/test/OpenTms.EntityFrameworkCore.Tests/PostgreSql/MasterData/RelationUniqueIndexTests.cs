using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenTms.MasterData.Drivers;
using OpenTms.MasterData.EntityFrameworkCore;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Relations;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Guids;
using Xunit;

namespace OpenTms.PostgreSql.MasterData;

/// <summary>
/// The unique and partial unique indexes and the check constraint of run 2, exercised through the
/// repositories so the application service's friendly check is bypassed (design 43, test risk analysis
/// "Uniqueness"). Risk class: Critical — the index is the one enforced place for the relation and driver
/// keys, the single own company, the single default account and the one-address-per-type rule.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class RelationUniqueIndexTests : MasterDataPostgreSqlTestBase
{
    private readonly IRepository<Relation, Guid> _relations;
    private readonly IRepository<RelationAddress, Guid> _addresses;
    private readonly IRepository<RelationBankAccount, Guid> _bankAccounts;
    private readonly IRepository<RelationExternalIdentifier, Guid> _externalIds;
    private readonly IRepository<Driver, Guid> _drivers;
    private readonly IRepository<Country, Guid> _countries;
    private readonly IRepository<AddressType, Guid> _addressTypes;
    private readonly IDbContextProvider<MasterDataDbContext> _contextProvider;
    private readonly IGuidGenerator _guids;

    public RelationUniqueIndexTests()
    {
        _relations = GetRequiredService<IRepository<Relation, Guid>>();
        _addresses = GetRequiredService<IRepository<RelationAddress, Guid>>();
        _bankAccounts = GetRequiredService<IRepository<RelationBankAccount, Guid>>();
        _externalIds = GetRequiredService<IRepository<RelationExternalIdentifier, Guid>>();
        _drivers = GetRequiredService<IRepository<Driver, Guid>>();
        _countries = GetRequiredService<IRepository<Country, Guid>>();
        _addressTypes = GetRequiredService<IRepository<AddressType, Guid>>();
        _contextProvider = GetRequiredService<IDbContextProvider<MasterDataDbContext>>();
        _guids = GetRequiredService<IGuidGenerator>();
    }

    private Relation NewRelation(Guid? tenant, string code, string name, RelationRoles roles = RelationRoles.Customer, string? vat = null, string? coc = null)
    {
        var relation = new Relation(_guids.Create(), tenant, code, name, roles);
        relation.SetLegal(coc, vat);
        return relation;
    }

    private static async Task<PostgresException> ShouldViolateAsync(Func<Task> action, string constraint, string sqlState)
    {
        var ex = await Should.ThrowAsync<DbUpdateException>(action);
        var postgres = ex.InnerException.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(sqlState);
        postgres.ConstraintName.ShouldBe(constraint);
        return postgres;
    }

    private static Task<PostgresException> ShouldViolateUniqueIndexAsync(Func<Task> insert, string indexName)
        => ShouldViolateAsync(insert, indexName, PostgresErrorCodes.UniqueViolation);

    // ── Relation keys ────────────────────────────────────────────────────────

    [Fact]
    public async Task A_duplicate_relation_code_is_refused_by_the_index()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            await _relations.InsertAsync(NewRelation(TenantA, "QQ-CODE", "First"), autoSave: true);
            await ShouldViolateUniqueIndexAsync(() => _relations.InsertAsync(NewRelation(TenantA, "QQ-CODE", "Second"), autoSave: true), "IX_Relations_TenantId_Code");
        });
    }

    [Fact]
    public async Task A_duplicate_vat_number_is_refused_by_the_index_but_two_relations_without_one_are_fine()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            await _relations.InsertAsync(NewRelation(TenantA, "QQ-1", "First", vat: "NL801234567B01"), autoSave: true);
            await _relations.InsertAsync(NewRelation(TenantA, "QQ-2", "No VAT"), autoSave: true);
            await _relations.InsertAsync(NewRelation(TenantA, "QQ-3", "No VAT either"), autoSave: true);
            await ShouldViolateUniqueIndexAsync(() => _relations.InsertAsync(NewRelation(TenantA, "QQ-4", "Second", vat: "nl 8012.34.567.b01"), autoSave: true), "IX_Relations_TenantId_VatNumber");
        });
    }

    [Fact]
    public async Task A_duplicate_chamber_of_commerce_number_is_refused_by_the_index()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            await _relations.InsertAsync(NewRelation(TenantA, "QQ-1", "First", coc: "12345678"), autoSave: true);
            await _relations.InsertAsync(NewRelation(TenantA, "QQ-2", "No CoC"), autoSave: true);
            await ShouldViolateUniqueIndexAsync(() => _relations.InsertAsync(NewRelation(TenantA, "QQ-3", "Second", coc: "12-34-56-78"), autoSave: true), "IX_Relations_TenantId_ChamberOfCommerceNumber");
        });
    }

    [Fact]
    public async Task A_soft_deleted_relation_frees_its_code_vat_and_coc_for_the_index()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var first = NewRelation(TenantA, "QQ-FREE", "First", vat: "NL801234567B01", coc: "12345678");
            await _relations.InsertAsync(first, autoSave: true);
            await _relations.DeleteAsync(first, autoSave: true);

            var second = NewRelation(TenantA, "QQ-FREE", "Second", vat: "NL801234567B01", coc: "12345678");
            await _relations.InsertAsync(second, autoSave: true);

            (await _relations.GetAsync(r => r.Code == "QQ-FREE")).Id.ShouldBe(second.Id);
        });
    }

    // ── Own company ──────────────────────────────────────────────────────────

    [Fact]
    public async Task A_second_own_company_is_refused_by_the_partial_index_whatever_its_other_roles()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            await _relations.InsertAsync(NewRelation(TenantA, "QQ-OWN1", "Own", RelationRoles.OwnCompany), autoSave: true);
            await _relations.InsertAsync(NewRelation(TenantA, "QQ-CUS", "Customer and carrier", RelationRoles.Customer | RelationRoles.Carrier), autoSave: true);
            await ShouldViolateUniqueIndexAsync(
                () => _relations.InsertAsync(NewRelation(TenantA, "QQ-OWN2", "Second own", RelationRoles.OwnCompany | RelationRoles.Customer | RelationRoles.Carrier), autoSave: true),
                "IX_Relations_TenantId_OwnCompany");
        });
    }

    [Fact]
    public async Task Adding_the_own_company_role_to_a_second_relation_is_refused_on_update()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            await _relations.InsertAsync(NewRelation(TenantA, "QQ-OWN1", "Own", RelationRoles.OwnCompany), autoSave: true);
            var other = NewRelation(TenantA, "QQ-OTH", "Other", RelationRoles.Carrier);
            await _relations.InsertAsync(other, autoSave: true);

            other.SetRoles(RelationRoles.Carrier | RelationRoles.OwnCompany);
            await ShouldViolateUniqueIndexAsync(() => _relations.UpdateAsync(other, autoSave: true), "IX_Relations_TenantId_OwnCompany");
        });
    }

    [Fact]
    public async Task A_soft_deleted_own_company_frees_the_slot()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var first = NewRelation(TenantA, "QQ-OWN1", "Own", RelationRoles.OwnCompany);
            await _relations.InsertAsync(first, autoSave: true);
            await _relations.DeleteAsync(first, autoSave: true);

            await _relations.InsertAsync(NewRelation(TenantA, "QQ-OWN2", "Own again", RelationRoles.OwnCompany), autoSave: true);
            (await _relations.CountAsync(r => (r.Roles & RelationRoles.OwnCompany) != 0)).ShouldBe(1);
        });
    }

    [Fact]
    public async Task Two_tenants_of_the_same_database_each_have_their_own_own_company_and_key_space()
    {
        /* The shared-tier tenant and the host share the host database; the partial index is per tenant. */
        await InRolledBackTransactionAsync(SharedTenant, async () =>
        {
            await _relations.InsertAsync(NewRelation(SharedTenant, "QQ-OWN", "Own of shared", RelationRoles.OwnCompany, vat: "NL801234567B01"), autoSave: true);
            using (CurrentTenant.Change(null))
            {
                await _relations.InsertAsync(NewRelation(null, "QQ-OWN", "Own of host", RelationRoles.OwnCompany, vat: "NL801234567B01"), autoSave: true);
                (await _relations.CountAsync(r => r.Code == "QQ-OWN")).ShouldBe(1);
            }

            (await _relations.CountAsync(r => r.Code == "QQ-OWN")).ShouldBe(1);
        });
    }

    [Fact]
    public async Task A_host_row_without_tenant_is_unique_too_NULLS_NOT_DISTINCT()
    {
        await InRolledBackTransactionAsync(null, async () =>
        {
            await _relations.InsertAsync(NewRelation(null, "QQ-HOST", "Host one", RelationRoles.OwnCompany), autoSave: true);
            await ShouldViolateUniqueIndexAsync(() => _relations.InsertAsync(NewRelation(null, "QQ-HOST", "Host two"), autoSave: true), "IX_Relations_TenantId_Code");
        });

        await InRolledBackTransactionAsync(null, async () =>
        {
            await _relations.InsertAsync(NewRelation(null, "QQ-HOST1", "Host one", RelationRoles.OwnCompany), autoSave: true);
            await ShouldViolateUniqueIndexAsync(() => _relations.InsertAsync(NewRelation(null, "QQ-HOST2", "Host two", RelationRoles.OwnCompany), autoSave: true), "IX_Relations_TenantId_OwnCompany");
        });
    }

    // ── Check constraint ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    [InlineData(-1)]
    public async Task A_roles_value_outside_the_five_flags_is_refused_by_the_check_constraint(int roles)
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var relation = NewRelation(TenantA, "QQ-CHK", "Check");
            await _relations.InsertAsync(relation, autoSave: true);
            var context = await _contextProvider.GetDbContextAsync();

            var ex = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlAsync($"UPDATE masterdata.\"Relations\" SET \"Roles\" = {roles} WHERE \"Id\" = {relation.Id}"));
            ex.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
            ex.ConstraintName.ShouldBe("CK_Relations_Roles");
        });
    }

    // ── Children ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_second_address_of_the_same_type_is_refused_by_the_index()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var nl = await _countries.GetAsync(c => c.IsoAlpha2 == "NL");
            var visiting = await _addressTypes.GetAsync(t => t.Kind == AddressTypeKind.Visiting);
            var postal = await _addressTypes.GetAsync(t => t.Kind == AddressTypeKind.Postal);
            var relation = NewRelation(TenantA, "QQ-ADR", "Addresses");
            await _relations.InsertAsync(relation, autoSave: true);

            await _addresses.InsertAsync(NewAddress(relation.Id, visiting.Id, nl.Id), autoSave: true);
            await _addresses.InsertAsync(NewAddress(relation.Id, postal.Id, nl.Id), autoSave: true);
            await ShouldViolateUniqueIndexAsync(() => _addresses.InsertAsync(NewAddress(relation.Id, visiting.Id, nl.Id), autoSave: true), "IX_RelationAddresses_RelationId_AddressTypeId");
        });
    }

    [Fact]
    public async Task A_second_default_address_on_one_relation_is_refused_by_the_index_NULLS_NOT_DISTINCT_while_two_relations_each_have_one()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var nl = await _countries.GetAsync(c => c.IsoAlpha2 == "NL");
            var visiting = await _addressTypes.GetAsync(t => t.Kind == AddressTypeKind.Visiting);
            var one = NewRelation(TenantA, "QQ-DEF1", "Default one");
            var two = NewRelation(TenantA, "QQ-DEF2", "Default two");
            await _relations.InsertAsync(one, autoSave: true);
            await _relations.InsertAsync(two, autoSave: true);

            /* The optional FK lets the type be null; the default sits next to a typed address; each relation has its own. */
            await _addresses.InsertAsync(NewAddress(one.Id, null, nl.Id), autoSave: true);
            await _addresses.InsertAsync(NewAddress(one.Id, visiting.Id, nl.Id), autoSave: true);
            await _addresses.InsertAsync(NewAddress(two.Id, null, nl.Id), autoSave: true);
            (await _addresses.CountAsync(a => a.AddressTypeId == null && (a.RelationId == one.Id || a.RelationId == two.Id))).ShouldBe(2);

            await ShouldViolateUniqueIndexAsync(() => _addresses.InsertAsync(NewAddress(one.Id, null, nl.Id), autoSave: true), "IX_RelationAddresses_RelationId_AddressTypeId");
        });

        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            /* The same through an update: a typed address turned into a second default is refused as well. */
            var nl = await _countries.GetAsync(c => c.IsoAlpha2 == "NL");
            var postal = await _addressTypes.GetAsync(t => t.Kind == AddressTypeKind.Postal);
            var relation = NewRelation(TenantA, "QQ-DEF3", "Default three");
            await _relations.InsertAsync(relation, autoSave: true);
            await _addresses.InsertAsync(NewAddress(relation.Id, null, nl.Id), autoSave: true);
            var typed = NewAddress(relation.Id, postal.Id, nl.Id);
            await _addresses.InsertAsync(typed, autoSave: true);

            typed.Set(new RelationAddressInput(null, null, nl.Id, "Main", "1", null, null, "1234 AB", "Amsterdam", null, null, null));
            await ShouldViolateUniqueIndexAsync(() => _addresses.UpdateAsync(typed, autoSave: true), "IX_RelationAddresses_RelationId_AddressTypeId");
        });
    }

    [Fact]
    public async Task An_address_needs_an_existing_country_and_address_type_and_a_referenced_one_cannot_be_deleted()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var nl = await _countries.GetAsync(c => c.IsoAlpha2 == "NL");
            var visiting = await _addressTypes.GetAsync(t => t.Kind == AddressTypeKind.Visiting);
            var relation = NewRelation(TenantA, "QQ-FK", "Foreign keys");
            await _relations.InsertAsync(relation, autoSave: true);

            await ShouldViolateAsync(() => _addresses.InsertAsync(NewAddress(relation.Id, visiting.Id, Guid.NewGuid()), autoSave: true), "FK_RelationAddresses_Countries_CountryId", PostgresErrorCodes.ForeignKeyViolation);
        });

        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var nl = await _countries.GetAsync(c => c.IsoAlpha2 == "NL");
            var relation = NewRelation(TenantA, "QQ-FK", "Foreign keys");
            await _relations.InsertAsync(relation, autoSave: true);

            await ShouldViolateAsync(() => _addresses.InsertAsync(NewAddress(relation.Id, Guid.NewGuid(), nl.Id), autoSave: true), "FK_RelationAddresses_AddressTypes_AddressTypeId", PostgresErrorCodes.ForeignKeyViolation);
        });

        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            /* Restrict: a country in use cannot be hard-deleted; ABP soft-deletes, so delete through SQL. */
            var nl = await _countries.GetAsync(c => c.IsoAlpha2 == "NL");
            var visiting = await _addressTypes.GetAsync(t => t.Kind == AddressTypeKind.Visiting);
            var relation = NewRelation(TenantA, "QQ-FK", "Foreign keys");
            await _relations.InsertAsync(relation, autoSave: true);
            await _addresses.InsertAsync(NewAddress(relation.Id, visiting.Id, nl.Id), autoSave: true);
            var context = await _contextProvider.GetDbContextAsync();

            var ex = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlAsync($"DELETE FROM masterdata.\"Countries\" WHERE \"Id\" = {nl.Id}"));
            ex.SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
            ex.ConstraintName.ShouldBe("FK_RelationAddresses_Countries_CountryId");
        });
    }

    [Fact]
    public async Task A_second_default_bank_account_is_refused_by_the_partial_index()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var relation = NewRelation(TenantA, "QQ-BANK", "Bank");
            await _relations.InsertAsync(relation, autoSave: true);

            await _bankAccounts.InsertAsync(NewBankAccount(relation.Id, "NL91ABNA0417164300", isDefault: true), autoSave: true);
            await _bankAccounts.InsertAsync(NewBankAccount(relation.Id, "NL20INGB0001234567", isDefault: false), autoSave: true);
            await ShouldViolateUniqueIndexAsync(() => _bankAccounts.InsertAsync(NewBankAccount(relation.Id, "DE89370400440532013000", isDefault: true), autoSave: true), "IX_RelationBankAccounts_RelationId_Default");
        });
    }

    [Fact]
    public async Task A_duplicate_iban_on_one_relation_is_refused_but_the_same_iban_on_another_relation_is_fine()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var one = NewRelation(TenantA, "QQ-B1", "One");
            var two = NewRelation(TenantA, "QQ-B2", "Two");
            await _relations.InsertAsync(one, autoSave: true);
            await _relations.InsertAsync(two, autoSave: true);

            await _bankAccounts.InsertAsync(NewBankAccount(one.Id, "NL91ABNA0417164300", isDefault: true), autoSave: true);
            await _bankAccounts.InsertAsync(NewBankAccount(two.Id, "NL91ABNA0417164300", isDefault: true), autoSave: true);
            await ShouldViolateUniqueIndexAsync(() => _bankAccounts.InsertAsync(NewBankAccount(one.Id, "NL91ABNA0417164300", isDefault: false), autoSave: true), "IX_RelationBankAccounts_RelationId_Iban");
        });
    }

    // ── Administration keys (PO 2026-10-13) ──────────────────────────────────
    // Critical: the index is the one enforced place for the debtor / creditor numbers and the external ids.

    private Relation NewAccountRelation(Guid? tenant, string code, string? debtor = null, string? creditor = null)
    {
        var relation = NewRelation(tenant, code, code);
        relation.SetAccountNumbers(debtor, creditor);
        return relation;
    }

    private RelationExternalIdentifier NewExternal(Guid? tenant, Guid relationId, string source, string identifier)
        => new(_guids.Create(), tenant, relationId, source, identifier);

    [Fact]
    public async Task A_duplicate_debtor_number_is_refused_by_the_index_but_relations_without_one_are_fine()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            await _relations.InsertAsync(NewAccountRelation(TenantA, "QQ-1", debtor: "QQDEB01"), autoSave: true);
            await _relations.InsertAsync(NewAccountRelation(TenantA, "QQ-2"), autoSave: true);
            await _relations.InsertAsync(NewAccountRelation(TenantA, "QQ-3"), autoSave: true);
            await ShouldViolateUniqueIndexAsync(() => _relations.InsertAsync(NewAccountRelation(TenantA, "QQ-4", debtor: " qqdeb01 "), autoSave: true), "IX_Relations_TenantId_DebtorNumber");
        });
    }

    [Fact]
    public async Task A_duplicate_creditor_number_is_refused_by_the_index_and_the_columns_are_separate_key_spaces()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            await _relations.InsertAsync(NewAccountRelation(TenantA, "QQ-1", debtor: "QQ-X", creditor: "QQ-Y"), autoSave: true);
            /* One relation's debtor number may be another's creditor number. */
            await _relations.InsertAsync(NewAccountRelation(TenantA, "QQ-2", debtor: "QQ-Y", creditor: "QQ-X"), autoSave: true);
            await ShouldViolateUniqueIndexAsync(() => _relations.InsertAsync(NewAccountRelation(TenantA, "QQ-3", creditor: "QQ-Y"), autoSave: true), "IX_Relations_TenantId_CreditorNumber");
        });
    }

    [Fact]
    public async Task A_soft_deleted_relation_frees_its_debtor_and_creditor_numbers_for_the_index()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var first = NewAccountRelation(TenantA, "QQ-1", "QQDEB", "QQCRE");
            await _relations.InsertAsync(first, autoSave: true);
            await _relations.DeleteAsync(first, autoSave: true);

            var second = NewAccountRelation(TenantA, "QQ-2", "QQDEB", "QQCRE");
            await _relations.InsertAsync(second, autoSave: true);
            (await _relations.GetAsync(r => r.DebtorNumber == "QQDEB")).Id.ShouldBe(second.Id);
        });
    }

    [Fact]
    public async Task Two_tenants_of_the_same_database_may_share_debtor_creditor_and_external_ids_and_the_host_is_unique_too()
    {
        /* The shared-tier tenant and the host share the host database; every administration index is per tenant. */
        await InRolledBackTransactionAsync(SharedTenant, async () =>
        {
            var shared = NewAccountRelation(SharedTenant, "QQ-S", "QQDEB", "QQCRE");
            await _relations.InsertAsync(shared, autoSave: true);
            await _externalIds.InsertAsync(NewExternal(SharedTenant, shared.Id, "EDI", "QQ-871"), autoSave: true);

            using (CurrentTenant.Change(null))
            {
                var host = NewAccountRelation(null, "QQ-H", "QQDEB", "QQCRE");
                await _relations.InsertAsync(host, autoSave: true);
                await _externalIds.InsertAsync(NewExternal(null, host.Id, "EDI", "QQ-871"), autoSave: true);
                (await _relations.CountAsync(r => r.DebtorNumber == "QQDEB")).ShouldBe(1);
                (await _externalIds.CountAsync(e => e.Identifier == "QQ-871")).ShouldBe(1);

                /* NULLS NOT DISTINCT: the host's (TenantId null) keys are unique as well; the violation comes last. */
                var second = NewAccountRelation(null, "QQ-H2");
                await _relations.InsertAsync(second, autoSave: true);
                await ShouldViolateUniqueIndexAsync(() => _externalIds.InsertAsync(NewExternal(null, second.Id, "EDI", "QQ-871"), autoSave: true), "IX_RelationExternalIdentifiers_TenantId_Source_Identifier");
            }
        });

        await InRolledBackTransactionAsync(null, async () =>
        {
            await _relations.InsertAsync(NewAccountRelation(null, "QQ-H1", debtor: "QQDEB"), autoSave: true);
            await ShouldViolateUniqueIndexAsync(() => _relations.InsertAsync(NewAccountRelation(null, "QQ-H2", debtor: "QQDEB"), autoSave: true), "IX_Relations_TenantId_DebtorNumber");
        });
    }

    [Fact]
    public async Task The_same_source_and_identifier_on_two_relations_of_a_tenant_is_refused_by_the_index()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var one = NewRelation(TenantA, "QQ-1", "One");
            var two = NewRelation(TenantA, "QQ-2", "Two");
            await _relations.InsertAsync(one, autoSave: true);
            await _relations.InsertAsync(two, autoSave: true);

            await _externalIds.InsertAsync(NewExternal(TenantA, one.Id, "EDI", "8712345000001"), autoSave: true);
            /* Another source or another identifier is a different key. */
            await _externalIds.InsertAsync(NewExternal(TenantA, two.Id, "Exact", "8712345000001"), autoSave: true);
            await _externalIds.InsertAsync(NewExternal(TenantA, two.Id, "EDI", "8712345000002"), autoSave: true);
            await ShouldViolateUniqueIndexAsync(() => _externalIds.InsertAsync(NewExternal(TenantA, two.Id, "EDI", "8712345000001"), autoSave: true), "IX_RelationExternalIdentifiers_TenantId_Source_Identifier");
        });
    }

    [Fact]
    public async Task Replacing_an_external_id_in_one_save_deletes_before_it_writes_so_the_index_holds()
    {
        /* Through the aggregate: drop the row holding EDI/1 and rename another to EDI/1 in the same save. */
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var relation = NewRelation(TenantA, "QQ-1", "One");
            relation.SetExternalIdentifiers([new RelationExternalIdentifierInput(null, "EDI", "1"), new RelationExternalIdentifierInput(null, "EDI", "2")], _guids.Create);
            await _relations.InsertAsync(relation, autoSave: true);
            var keep = relation.ExternalIdentifiers.Single(e => e.Identifier == "2").Id;

            var loaded = await _relations.GetAsync(relation.Id);
            loaded.SetExternalIdentifiers([new RelationExternalIdentifierInput(keep, "EDI", "1")], _guids.Create);
            await _relations.UpdateAsync(loaded, autoSave: true);

            var context = await _contextProvider.GetDbContextAsync();
            context.ChangeTracker.Clear();
            var stored = await context.Set<RelationExternalIdentifier>().Where(e => e.RelationId == relation.Id).ToListAsync();
            stored.ShouldHaveSingleItem().Id.ShouldBe(keep);
            stored[0].Identifier.ShouldBe("1");
        });
    }

    [Fact]
    public async Task A_relation_needs_an_existing_currency_and_characteristic_and_a_hard_delete_cascades_its_children()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var relation = NewRelation(TenantA, "QQ-1", "One");
            relation.SetCurrency(Guid.NewGuid());
            await ShouldViolateAsync(() => _relations.InsertAsync(relation, autoSave: true), "FK_Relations_Currencies_CurrencyId", PostgresErrorCodes.ForeignKeyViolation);
        });

        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var relation = NewRelation(TenantA, "QQ-1", "One");
            relation.SetTransportCharacteristics([Guid.NewGuid()]);
            var ex = await Should.ThrowAsync<DbUpdateException>(() => _relations.InsertAsync(relation, autoSave: true));
            var postgres = ex.InnerException.ShouldBeOfType<PostgresException>();
            postgres.SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
            postgres.ConstraintName!.ShouldStartWith("FK_RelationTransportCharacteristics_TransportCharacteristics_");
        });

        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var context = await _contextProvider.GetDbContextAsync();
            /* A currency of its own: the seeded EUR is held by the countries' FK too. */
            var currency = new Currency(_guids.Create(), TenantA, "QQX", OpenTms.Platform.Localization.LocalizedName.Create("en", "Test currency"), true);
            var eur = currency.Id;
            context.Add(currency);
            var characteristic = new OpenTms.MasterData.Fleet.TransportCharacteristics(_guids.Create(), TenantA, "QQ-ADR", "Dangerous goods");
            context.Add(characteristic);
            await context.SaveChangesAsync();

            var relation = NewRelation(TenantA, "QQ-1", "One");
            relation.SetCurrency(eur);
            relation.SetExternalIdentifiers([new RelationExternalIdentifierInput(null, "EDI", "1")], _guids.Create);
            relation.SetTransportCharacteristics([characteristic.Id]);
            await _relations.InsertAsync(relation, autoSave: true);

            /* Restrict: the currency in use cannot be removed from under the relation. */
            var restrict = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlAsync($"DELETE FROM masterdata.\"Currencies\" WHERE \"Id\" = {eur}"));
            restrict.SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
            restrict.ConstraintName.ShouldBe("FK_Relations_Currencies_CurrencyId");
        });

        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var context = await _contextProvider.GetDbContextAsync();
            var characteristic = new OpenTms.MasterData.Fleet.TransportCharacteristics(_guids.Create(), TenantA, "QQ-ADR", "Dangerous goods");
            context.Add(characteristic);
            await context.SaveChangesAsync();
            var relation = NewRelation(TenantA, "QQ-1", "One");
            relation.SetExternalIdentifiers([new RelationExternalIdentifierInput(null, "EDI", "1")], _guids.Create);
            relation.SetTransportCharacteristics([characteristic.Id]);
            await _relations.InsertAsync(relation, autoSave: true);
            var other = NewRelation(TenantA, "QQ-2", "Two");
            other.SetTransportCharacteristics([characteristic.Id]);
            await _relations.InsertAsync(other, autoSave: true);

            /* Cascade: a hard delete of the relation takes its external ids and characteristics along; deleting the characteristic its joins. */
            await context.Database.ExecuteSqlAsync($"DELETE FROM masterdata.\"Relations\" WHERE \"Id\" = {relation.Id}");
            context.ChangeTracker.Clear();
            (await context.Set<RelationExternalIdentifier>().CountAsync(e => e.RelationId == relation.Id)).ShouldBe(0);
            (await context.Set<RelationTransportCharacteristic>().CountAsync(c => c.RelationId == relation.Id)).ShouldBe(0);

            await context.Database.ExecuteSqlAsync($"DELETE FROM masterdata.\"TransportCharacteristics\" WHERE \"Id\" = {characteristic.Id}");
            (await context.Set<RelationTransportCharacteristic>().CountAsync(c => c.RelationId == other.Id)).ShouldBe(0);
        });
    }

    // ── Driver keys ──────────────────────────────────────────────────────────

    [Fact]
    public async Task A_duplicate_driver_code_is_refused_by_the_index_and_freed_by_a_soft_delete()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var first = new Driver(_guids.Create(), TenantA, "QQ-D1", "Jan", "Jansen");
            await _drivers.InsertAsync(first, autoSave: true);
            await ShouldViolateUniqueIndexAsync(() => _drivers.InsertAsync(new Driver(_guids.Create(), TenantA, "QQ-D1", "Piet", "Pietersen"), autoSave: true), "IX_Drivers_TenantId_Code");
        });

        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var first = new Driver(_guids.Create(), TenantA, "QQ-D1", "Jan", "Jansen");
            await _drivers.InsertAsync(first, autoSave: true);
            await _drivers.DeleteAsync(first, autoSave: true);
            var second = new Driver(_guids.Create(), TenantA, "QQ-D1", "Piet", "Pietersen");
            await _drivers.InsertAsync(second, autoSave: true);
            (await _drivers.GetAsync(d => d.Code == "QQ-D1")).Id.ShouldBe(second.Id);
        });
    }

    [Fact]
    public async Task A_user_linked_to_two_drivers_is_refused_by_the_partial_index_but_many_drivers_without_user_are_fine()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var user = Guid.NewGuid();
            var first = new Driver(_guids.Create(), TenantA, "QQ-D1", "Jan", "Jansen");
            first.SetUser(user);
            await _drivers.InsertAsync(first, autoSave: true);
            await _drivers.InsertAsync(new Driver(_guids.Create(), TenantA, "QQ-D2", "No", "User"), autoSave: true);
            await _drivers.InsertAsync(new Driver(_guids.Create(), TenantA, "QQ-D3", "No", "User either"), autoSave: true);

            var second = new Driver(_guids.Create(), TenantA, "QQ-D4", "Piet", "Pietersen");
            second.SetUser(user);
            await ShouldViolateUniqueIndexAsync(() => _drivers.InsertAsync(second, autoSave: true), "IX_Drivers_TenantId_UserId");
        });
    }

    [Fact]
    public async Task An_anonymised_driver_frees_its_user_for_the_index()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var user = Guid.NewGuid();
            var first = new Driver(_guids.Create(), TenantA, "QQ-D1", "Jan", "Jansen");
            first.SetUser(user);
            await _drivers.InsertAsync(first, autoSave: true);
            first.Anonymise();
            await _drivers.UpdateAsync(first, autoSave: true);

            var second = new Driver(_guids.Create(), TenantA, "QQ-D2", "Piet", "Pietersen");
            second.SetUser(user);
            await _drivers.InsertAsync(second, autoSave: true);
            (await _drivers.CountAsync(d => d.UserId == user)).ShouldBe(1);
        });
    }

    [Fact]
    public async Task A_relation_with_a_driver_cannot_be_hard_deleted_and_a_driver_needs_an_existing_employer()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var driver = new Driver(_guids.Create(), TenantA, "QQ-D1", "Jan", "Jansen");
            driver.SetEmployer(Guid.NewGuid());
            await ShouldViolateAsync(() => _drivers.InsertAsync(driver, autoSave: true), "FK_Drivers_Relations_EmployerRelationId", PostgresErrorCodes.ForeignKeyViolation);
        });

        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var carrier = NewRelation(TenantA, "QQ-CAR", "Carrier", RelationRoles.Carrier);
            await _relations.InsertAsync(carrier, autoSave: true);
            var driver = new Driver(_guids.Create(), TenantA, "QQ-D1", "Jan", "Jansen");
            driver.SetEmployer(carrier.Id);
            await _drivers.InsertAsync(driver, autoSave: true);
            var context = await _contextProvider.GetDbContextAsync();

            var ex = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlAsync($"DELETE FROM masterdata.\"Relations\" WHERE \"Id\" = {carrier.Id}"));
            ex.SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
            ex.ConstraintName.ShouldBe("FK_Drivers_Relations_EmployerRelationId");
        });
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>A null <paramref name="addressTypeId"/> is the relation's default address ("all addresses equal", PO 2026-10-12).</summary>
    private RelationAddress NewAddress(Guid relationId, Guid? addressTypeId, Guid countryId)
    {
        var address = new RelationAddress(_guids.Create(), TenantA, relationId);
        address.Set(new RelationAddressInput(null, addressTypeId, countryId, "Main", "1", null, null, "1234 AB", "Amsterdam", null, null, null));
        return address;
    }

    private RelationBankAccount NewBankAccount(Guid relationId, string iban, bool isDefault)
    {
        var account = new RelationBankAccount(_guids.Create(), TenantA, relationId);
        account.Set(new RelationBankAccountInput(null, iban, null, null, null, isDefault));
        return account;
    }
}
