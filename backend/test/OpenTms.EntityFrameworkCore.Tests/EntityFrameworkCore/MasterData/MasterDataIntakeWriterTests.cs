using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.EntityFrameworkCore.Orders;
using OpenTms.MasterData.Published;
using OpenTms.MasterData.Relations;
using OpenTms.MasterData.Transport;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace OpenTms.EntityFrameworkCore.MasterData;

/// <summary>
/// Master Data's published <see cref="IMasterDataIntakeWriter"/> on SQLite (design 72 build notes 29–30, 32): a relation with
/// role, keys, marker description and the external identifier "OrderIntake" (idempotent on it), its code derived from the
/// name with "-2" when taken; optional keys Master Data refuses left out, required address parts and an unknown country
/// refused with a code (never an exception); an address only as the default of a relation without one; a transport unit,
/// product or temperature by name (an existing one, any case, returned; an archived one refused); and every write in the
/// current tenant only — tenant B's writer never returns, changes or reuses tenant A's rows. Risk class: High (master data
/// written without a person), Critical for tenant isolation.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class MasterDataIntakeWriterTests : TransportOrderTestBase<OpenTmsEntityFrameworkCoreTestModule>
{
    private const string MarkerEn = "Created by order intake";

    private IMasterDataIntakeWriter Writer => GetRequiredService<IMasterDataIntakeWriter>();

    private Task<Relation> RelationAsync(Guid id) => WithUnitOfWorkAsync(() => GetRequiredService<IRepository<Relation, Guid>>().GetAsync(id, includeDetails: true));

    private static IntakeAddressInputDto Address(string? country = "NL", string? street = "Teeltweg") => new()
    {
        Street = street, HouseNumber = "3", PostalCode = "2671 AA", City = "Naaldwijk", CountryCode = country
    };

    [Fact]
    public async Task A_relation_is_created_with_its_keys_the_marker_and_the_external_identifier_and_asked_again_it_is_returned()
    {
        using var tenant = InTenantA();
        using var culture = UiCulture("en");
        var f = await FixtureAsync();

        var created = await Writer.CreateRelationAsync(new IntakeRelationCreateDto
        {
            Name = "Nieuwe Klant B.V.", Roles = RelationRoles.Customer, EmailAddress = "orders@nieuweklant.nl", VatNumber = "NL123456789B01",
            ChamberOfCommerceNumber = "12345678", Address = Address(), SourceReference = "intake-1 order.customer"
        });

        created.Created.ShouldBeTrue();
        created.Succeeded.ShouldBeTrue();
        created.RefusedCode.ShouldBeNull();
        created.RelationAddressId.ShouldNotBeNull();
        created.Detail.ShouldBe("Teeltweg 3, 2671 AA Naaldwijk");
        var row = await RelationAsync(created.Id!.Value);
        (row.Name, row.Roles, row.TenantId).ShouldBe(("Nieuwe Klant B.V.", RelationRoles.Customer, (Guid?)TenantA));
        row.Code.ShouldStartWith("NIEUWEKLANT");
        row.Code.Length.ShouldBeLessThanOrEqualTo(MasterDataIntakeWriter.MaxDerivedCodeLength);
        (row.Email, row.VatNumber, row.ChamberOfCommerceNumber).ShouldBe(("orders@nieuweklant.nl", "NL123456789B01", "12345678"));
        row.Description!.ShouldStartWith(MarkerEn);
        var external = row.ExternalIdentifiers.ShouldHaveSingleItem();
        (external.Source, external.Identifier).ShouldBe((IMasterDataIntakeWriter.SourceName, "intake-1 order.customer"));
        var address = row.Addresses.ShouldHaveSingleItem();
        (address.AddressTypeId, address.CountryId, address.Street, address.City).ShouldBe(((Guid?)null, f.CountryId, "Teeltweg", "Naaldwijk"));

        var again = await Writer.CreateRelationAsync(new IntakeRelationCreateDto { Name = "Something else", SourceReference = "intake-1 order.customer" });
        again.Created.ShouldBeFalse("idempotent on the source reference (build note 32)");
        again.Id.ShouldBe(created.Id);
        again.RelationAddressId.ShouldBe(created.RelationAddressId);

        var second = await Writer.CreateRelationAsync(new IntakeRelationCreateDto { Name = "Nieuwe Klant B.V.", SourceReference = "intake-2 order.customer" });
        second.Created.ShouldBeTrue("another reference is another row");
        (await RelationAsync(second.Id!.Value)).Code.ShouldBe(row.Code + "-2", "a taken code gets -2");
    }

    [Fact]
    public async Task Optional_keys_master_data_refuses_are_left_out_never_fatal()
    {
        using var tenant = InTenantA();
        var f = await FixtureAsync();
        await Writer.CreateRelationAsync(new IntakeRelationCreateDto { Name = "Eerste", VatNumber = "NL123456789B01", ChamberOfCommerceNumber = "87654321", SourceReference = "a" });

        var created = await Writer.CreateRelationAsync(new IntakeRelationCreateDto
        {
            Name = "Tweede", VatNumber = "NL123456789B01", ChamberOfCommerceNumber = "87654321", EmailAddress = "geen adres", SourceReference = "b"
        });

        created.Succeeded.ShouldBeTrue();
        var row = await RelationAsync(created.Id!.Value);
        (row.VatNumber, row.ChamberOfCommerceNumber, row.Email).ShouldBe(((string?)null, (string?)null, (string?)null),
            "VAT and chamber of commerce number on another relation, an e-mail address of the wrong shape: left out");
        (await Writer.CreateRelationAsync(new IntakeRelationCreateDto { Name = "Derde", VatNumber = "not-a-vat", SourceReference = "c" })).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Required_values_and_an_unknown_country_are_refused_with_a_code()
    {
        using var tenant = InTenantA();
        await FixtureAsync();

        /* An empty name or reference is refused by the contract's [Required] (validation), before the writer runs. */
        await Should.ThrowAsync<Volo.Abp.Validation.AbpValidationException>(() => Writer.CreateRelationAsync(new IntakeRelationCreateDto { Name = " ", SourceReference = "x" }));

        (await Writer.CreateRelationAsync(new IntakeRelationCreateDto { Name = "X", Roles = RelationRoles.None, SourceReference = "x" })).RefusedCode.ShouldBe(IMasterDataIntakeWriter.RefusedInvalid);
        var noStreet = await Writer.CreateRelationAsync(new IntakeRelationCreateDto { Name = "Zonder straat", Address = Address(street: null), SourceReference = "y" });
        (noStreet.Succeeded, noStreet.RefusedCode).ShouldBe((false, IMasterDataIntakeWriter.RefusedInvalid));
        var unknownCountry = await Writer.CreateRelationAsync(new IntakeRelationCreateDto { Name = "Atlantis", Address = Address(country: "XX"), SourceReference = "z" });
        (unknownCountry.Succeeded, unknownCountry.RefusedCode).ShouldBe((false, IMasterDataIntakeWriter.RefusedCountryUnknown));
        (await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<Relation, Guid>>().CountAsync(r => r.Name == "Atlantis" || r.Name == "Zonder straat"))).ShouldBe(0);
    }

    [Fact]
    public async Task An_address_is_added_only_as_the_default_of_a_relation_without_one()
    {
        using var tenant = InTenantA();
        var f = await FixtureAsync();
        var bare = await Writer.CreateRelationAsync(new IntakeRelationCreateDto { Name = "Bakker Logistiek Holding", SourceReference = "bare" });

        var added = await Writer.AddRelationAddressAsync(new IntakeRelationAddressAddDto { RelationId = bare.Id!.Value, Address = Address(country: null) });

        added.Created.ShouldBeTrue();
        added.Id.ShouldBe(bare.Id);
        var row = await RelationAsync(bare.Id!.Value);
        var address = row.Addresses.ShouldHaveSingleItem();
        (address.Id, address.AddressTypeId, address.CountryId).ShouldBe((added.RelationAddressId!.Value, (Guid?)null, f.CountryId), "no country: the default country, else NL");

        (await Writer.AddRelationAddressAsync(new IntakeRelationAddressAddDto { RelationId = bare.Id!.Value, Address = Address() })).RefusedCode
            .ShouldBe(IMasterDataIntakeWriter.RefusedDefaultAddressTaken);
        (await Writer.AddRelationAddressAsync(new IntakeRelationAddressAddDto { RelationId = f.DepotId, Address = Address() })).RefusedCode
            .ShouldBe(IMasterDataIntakeWriter.RefusedDefaultAddressTaken, "DC Venlo has its default address");
        (await Writer.AddRelationAddressAsync(new IntakeRelationAddressAddDto { RelationId = Guid.NewGuid(), Address = Address() })).RefusedCode
            .ShouldBe(IMasterDataIntakeWriter.RefusedNotFound);

        var archived = await Writer.CreateRelationAsync(new IntakeRelationCreateDto { Name = "Gearchiveerd", SourceReference = "archived" });
        await WithUnitOfWorkAsync(async () =>
        {
            var relations = GetRequiredService<IRepository<Relation, Guid>>();
            var relation = await relations.GetAsync(archived.Id!.Value);
            relation.Archive();
            await relations.UpdateAsync(relation, autoSave: true);
        });
        (await Writer.AddRelationAddressAsync(new IntakeRelationAddressAddDto { RelationId = archived.Id!.Value, Address = Address() })).RefusedCode
            .ShouldBe(IMasterDataIntakeWriter.RefusedArchived);
    }

    [Theory]
    [InlineData(MasterDataReferenceKind.TransportUnit)]
    [InlineData(MasterDataReferenceKind.Product)]
    [InlineData(MasterDataReferenceKind.Temperature)]
    public async Task A_reference_is_created_by_its_name_with_the_marker_and_an_existing_name_in_any_case_is_returned(MasterDataReferenceKind kind)
    {
        using var tenant = InTenantA();
        using var culture = UiCulture("en");
        await FixtureAsync();

        /* An empty name never reaches the writer: [Required] on the contract (the resolver plans no row without a name). */
        var created = await Writer.CreateReferenceAsync(new IntakeReferenceCreateDto { Kind = kind, Name = "  Gitterbox  " });
        var again = await Writer.CreateReferenceAsync(new IntakeReferenceCreateDto { Kind = kind, Name = "GITTERBOX" });

        (created.Created, created.Name).ShouldBe((true, "Gitterbox"));
        (again.Created, again.Id).ShouldBe((false, created.Id), "the name is the key (ignoring case): one row");
        var (description, tenantId) = await WithUnitOfWorkAsync(async () => kind switch
        {
            MasterDataReferenceKind.TransportUnit => await GetRequiredService<IRepository<TransportUnit, Guid>>().GetAsync(created.Id!.Value) is var u ? (u.Description, u.TenantId) : default,
            MasterDataReferenceKind.Product => await GetRequiredService<IRepository<Product, Guid>>().GetAsync(created.Id!.Value) is var p ? (p.Description, p.TenantId) : default,
            _ => await GetRequiredService<IRepository<Temperature, Guid>>().GetAsync(created.Id!.Value) is var t ? (t.Description, t.TenantId) : default
        });
        description!.ShouldStartWith(MarkerEn);
        tenantId.ShouldBe(TenantA);
    }

    [Fact]
    public async Task An_existing_transport_unit_is_returned_and_an_archived_one_refuses()
    {
        using var tenant = InTenantA();
        var f = await FixtureAsync();

        var existing = await Writer.CreateReferenceAsync(new IntakeReferenceCreateDto { Kind = MasterDataReferenceKind.TransportUnit, Name = "europallet" });
        (existing.Created, existing.Id).ShouldBe((false, (Guid?)f.TransportUnitId));

        await WithUnitOfWorkAsync(async () =>
        {
            var units = GetRequiredService<IRepository<TransportUnit, Guid>>();
            var unit = await units.GetAsync(f.TransportUnitId);
            unit.IsArchived = true;
            await units.UpdateAsync(unit, autoSave: true);
        });
        var archived = await Writer.CreateReferenceAsync(new IntakeReferenceCreateDto { Kind = MasterDataReferenceKind.TransportUnit, Name = "Europallet" });
        (archived.Succeeded, archived.RefusedCode).ShouldBe((false, IMasterDataIntakeWriter.RefusedArchived));
    }

    /* ---------- Tenant isolation ---------- */

    [Fact]
    public async Task The_writer_creates_in_the_current_tenant_only_and_never_returns_or_changes_tenant_As_rows()
    {
        Guid unitOfA, relationOfA, bareOfA;
        using (InTenantA())
        {
            var fa = await FixtureAsync();
            unitOfA = fa.TransportUnitId;
            relationOfA = (await Writer.CreateRelationAsync(new IntakeRelationCreateDto { Name = "Nieuwe Klant", SourceReference = "shared-ref" })).Id!.Value;
            bareOfA = (await Writer.CreateRelationAsync(new IntakeRelationCreateDto { Name = "Kale Relatie", SourceReference = "bare-ref" })).Id!.Value;
        }

        using (InTenantB())
        {
            await FixtureAsync();

            var unit = await Writer.CreateReferenceAsync(new IntakeReferenceCreateDto { Kind = MasterDataReferenceKind.TransportUnit, Name = "Europallet" });
            unit.Id.ShouldNotBe(unitOfA, "B's own Europallet, never A's");
            var gitterbox = await Writer.CreateReferenceAsync(new IntakeReferenceCreateDto { Kind = MasterDataReferenceKind.Product, Name = "Gitterbox" });
            gitterbox.Created.ShouldBeTrue();

            var relation = await Writer.CreateRelationAsync(new IntakeRelationCreateDto { Name = "Nieuwe Klant", SourceReference = "shared-ref" });
            relation.Created.ShouldBeTrue("A's relation with the same reference is not B's");
            relation.Id.ShouldNotBe(relationOfA);
            (await RelationAsync(relation.Id!.Value)).TenantId.ShouldBe(TenantB);
            (await RelationAsync(relation.Id!.Value)).Code.ShouldBe("NIEUWEKLANT", "A's codes do not count in B");

            (await Writer.AddRelationAddressAsync(new IntakeRelationAddressAddDto { RelationId = bareOfA, Address = Address() })).RefusedCode
                .ShouldBe(IMasterDataIntakeWriter.RefusedNotFound, "A's relation does not exist for B");
        }

        using (InTenantA())
        {
            (await RelationAsync(bareOfA)).Addresses.ShouldBeEmpty("B's writer changed nothing of A's");
            (await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<Relation, Guid>>().CountAsync(r => r.Name == "Nieuwe Klant"))).ShouldBe(1);
            (await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<Product, Guid>>().CountAsync(p => p.Name == "Gitterbox"))).ShouldBe(0);
        }
    }
}
