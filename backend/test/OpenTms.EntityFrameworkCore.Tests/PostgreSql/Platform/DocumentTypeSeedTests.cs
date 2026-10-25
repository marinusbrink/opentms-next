using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Platform.DocumentTypes;
using OpenTms.Platform.Localization;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace OpenTms.PostgreSql.Platform;

/// <summary>
/// The document-type seed against real PostgreSQL (docs/designs/54-platform-master-data.md, "Seed": FREIGHT and GENERAL in
/// every tenant, upsert on Code, never overwriting tenant edits, the code never matched on the name; test risk analysis
/// "Seed", Medium: idempotent re-run, tenant edits kept). The DbMigrator that created the test databases ran the
/// contributor for tenant A (own database) and the shared tenant (host database). Also: a soft-deleted code stays deleted,
/// the host gets nothing, a brand-new tenant gets both once.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class DocumentTypeSeedTests : PlatformMasterDataPostgreSqlTestBase
{
    private IRepository<DocumentType, Guid> Types => GetRequiredService<IRepository<DocumentType, Guid>>();
    private DocumentTypeDataSeedContributor Contributor => GetRequiredService<DocumentTypeDataSeedContributor>();
    private IDataFilter DataFilter => GetRequiredService<IDataFilter>();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Every_tenant_has_FREIGHT_and_GENERAL_once_with_their_names_and_flags(bool ownDatabase)
    {
        var tenantId = ownDatabase ? TenantA : SharedTenant;
        await InRolledBackTransactionAsync(tenantId, async () =>
        {
            var types = await Types.GetListAsync(includeDetails: true);
            types.Select(t => t.Code).ShouldBe([DocumentTypeDataSeedContributor.FreightCode, DocumentTypeDataSeedContributor.GeneralCode], ignoreOrder: true);
            types.ShouldAllBe(t => t.TenantId == tenantId && !t.IsArchived && t.Transformations.Count == 0);

            var freight = types.Single(t => t.Code == "FREIGHT");
            (freight.Name.Resolve("nl"), freight.Name.Resolve("en")).ShouldBe(("Vrachtdocument", "Freight document"));
            (freight.IsFreightDocument, freight.IncludeWithInvoice, freight.ShowInListIndicator).ShouldBe((true, true, true));

            var general = types.Single(t => t.Code == "GENERAL");
            (general.Name.Resolve("nl"), general.Name.Resolve("en")).ShouldBe(("Algemeen document", "General document"));
            (general.IsFreightDocument, general.IncludeWithInvoice, general.ShowInListIndicator).ShouldBe((false, false, false));
        });
    }

    [Fact]
    public async Task The_host_gets_no_document_types()
    {
        await InRolledBackTransactionAsync(null, async () =>
        {
            (await Types.GetCountAsync()).ShouldBe(0);
            await Contributor.SeedAsync(new DataSeedContext(null));
            await FlushAsync();
            (await Types.GetCountAsync()).ShouldBe(0);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_second_run_inserts_nothing(bool ownDatabase)
    {
        var tenantId = ownDatabase ? TenantA : SharedTenant;
        await InRolledBackTransactionAsync(tenantId, async () =>
        {
            var before = (await Types.GetListAsync()).Select(t => (t.Id, t.ConcurrencyStamp)).OrderBy(t => t.Id).ToList();

            await Contributor.SeedAsync(new DataSeedContext(tenantId));
            await Contributor.SeedAsync(new DataSeedContext(tenantId));
            await FlushAsync();

            (await Types.GetListAsync()).Select(t => (t.Id, t.ConcurrencyStamp)).OrderBy(t => t.Id).ToList().ShouldBe(before, "same rows, untouched");
        });
    }

    [Fact]
    public async Task Tenant_edits_are_kept_and_a_soft_deleted_code_is_not_revived()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var freight = await Types.GetAsync(t => t.Code == "FREIGHT");
            freight.SetName(LocalizedName.Create([new("nl", "Vrachtbrief"), new("en", "Consignment note")]));
            freight.SetFlags(false, false, true);
            freight.Archive();
            freight.SetDescription("our own");
            await Types.UpdateAsync(freight, autoSave: true);
            var general = await Types.GetAsync(t => t.Code == "GENERAL");
            await Types.DeleteAsync(general, autoSave: true);

            await Contributor.SeedAsync(new DataSeedContext(TenantA));
            await FlushAsync();

            var kept = await Types.GetAsync(t => t.Code == "FREIGHT");
            (kept.Name.Resolve("nl"), kept.Name.Resolve("en"), kept.IsFreightDocument, kept.IncludeWithInvoice, kept.ShowInListIndicator, kept.IsArchived, kept.Description)
                .ShouldBe(("Vrachtbrief", "Consignment note", false, false, true, true, "our own"));

            (await Types.AnyAsync(t => t.Code == "GENERAL")).ShouldBeFalse("a soft-deleted code stays deleted");
            using (DataFilter.Disable<ISoftDelete>())
            {
                (await Types.GetListAsync(t => t.Code == "GENERAL")).ShouldHaveSingleItem().IsDeleted.ShouldBeTrue();
            }
        });
    }

    [Fact]
    public async Task The_seed_matches_on_the_code_never_on_the_name()
    {
        await InRolledBackTransactionAsync(SharedTenant, async () =>
        {
            /* A tenant type named like the seed's under another code does not stop the seed; a renamed code is a new type. */
            var freight = await Types.GetAsync(t => t.Code == "FREIGHT");
            freight.SetCode("VRACHT");
            await Types.UpdateAsync(freight, autoSave: true);

            await Contributor.SeedAsync(new DataSeedContext(SharedTenant));
            await FlushAsync();

            var codes = (await Types.GetListAsync()).Select(t => t.Code).ToList();
            codes.ShouldBe(["VRACHT", "FREIGHT", "GENERAL"], ignoreOrder: true);
            (await Types.GetAsync(t => t.Code == "VRACHT")).Name.Resolve("en").ShouldBe("Freight document", "the tenant's row is untouched");
        });
    }

    [Fact]
    public async Task A_new_tenant_gets_both_types_once()
    {
        var newTenant = Guid.NewGuid();
        await InRolledBackTransactionAsync(newTenant, async () =>
        {
            (await Types.GetCountAsync()).ShouldBe(0);
            await Contributor.SeedAsync(new DataSeedContext(newTenant));
            await FlushAsync();

            /* A later seed run (the DbMigrator's, after the provisioning job's) finds the rows and adds nothing. */
            await Contributor.SeedAsync(new DataSeedContext(newTenant));
            await FlushAsync();

            var types = await Types.GetListAsync();
            types.Select(t => t.Code).ShouldBe(["FREIGHT", "GENERAL"], ignoreOrder: true);
            types.ShouldAllBe(t => t.TenantId == newTenant);

            /* The neighbours in the host database are untouched. */
            using (CurrentTenant.Change(SharedTenant))
            {
                (await Types.GetCountAsync()).ShouldBe(2);
            }

            using (CurrentTenant.Change(null))
            {
                (await Types.GetCountAsync()).ShouldBe(0);
            }
        });
    }
}
