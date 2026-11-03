using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Features;
using OpenTms.MasterData.Published;
using OpenTms.MasterData.Relations;
using OpenTms.MasterData.Transport;
using OpenTms.Orders.Intake;
using OpenTms.Orders.Published;
using OpenTms.Orders.TransportOrders;
using OpenTms.TransportRun;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Orders;

/// <summary>
/// Straight-through order intake on SQLite (design 72 build notes 28–32, PO decisions 2026-11-03), with the master data
/// of <see cref="TransportRunFixture"/>:
/// <list type="bullet">
/// <item>the fuzzy rule in the resolver: a typo above the threshold is method Fuzzy with its score and learned once the
/// order exists; below it the row is created; a tie within the margin is Ambiguous; the threshold is the intake's (kept
/// for the review); a customer whose VAT number or e-mail domain conflicts is ruled out (the sender's domain never rules
/// out); an address needs the same postal code or city;</item>
/// <item>creation per entity (customer, stop place as a new relation or as the default address of a relation without one,
/// transport unit, product, temperature) with the marker, method Created and the counts for the trace; one row per new
/// place within an intake; nothing created again for the same intake or the next intake of the same message;</item>
/// <item>the straight-through matrix: every rule but a clerk's pick may confirm when the mapping may confirm and the
/// channel auto-confirms; otherwise a Draft, never a review;</item>
/// <item>tenant isolation: tenant B never fuzzy-matches or reuses tenant A's rows and creates its own.</item>
/// </list>
/// Risk class: High (order chain), Critical for tenant isolation.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class OrderIntakeStraightThroughTests : TransportOrderTestBase<OpenTmsEntityFrameworkCoreTestModule>
{
    private const string CreatedMarkerEn = "Created by order intake";

    private IOrderIntakeService Intake => GetRequiredService<IOrderIntakeService>();

    private IOrderIntakeAppService Review => GetRequiredService<IOrderIntakeAppService>();

    private static IDisposable IntakeOn() => TestFeatureValueProvider.Override(IntakeConsts.OrderIntakeFeature, "true");

    private OrderIntakeDto Payload(TransportRunWorld f, Action<OrderIntakeDto>? change = null)
    {
        var date = Today.AddDays(1).ToString("yyyy-MM-dd");
        var payload = new OrderIntakeDto
        {
            CustomerReference = "PO-" + f.Suffix,
            Customer = new OrderIntakeCustomerDto { Code = "KL" + f.Suffix },
            Lines =
            [
                new OrderIntakeLineDto
                {
                    TransportUnitName = "Europallet", Quantity = 2, WeightKg = 100,
                    Stops =
                    [
                        new OrderIntakeStopDto
                        {
                            Role = IntakeStopRole.Pickup, Date = date,
                            Address = new OrderIntakeAddressDto { Name = "DC Venlo", PostalCode = "5928 PR", HouseNumber = "12", City = "Venlo" }
                        },
                        new OrderIntakeStopDto
                        {
                            Role = IntakeStopRole.Delivery, Date = date,
                            Address = new OrderIntakeAddressDto { Name = "Supermarkt Utrecht", PostalCode = "3511BD", HouseNumber = "40", City = "Utrecht" }
                        }
                    ]
                }
            ]
        };
        change?.Invoke(payload);
        return payload;
    }

    private static OrderIntakeAddressDto Kwekerij() => new()
    {
        Name = "Kwekerij De Bloem", Street = "Teeltweg", HouseNumber = "3", PostalCode = "2671 AA", City = "Naaldwijk", CountryCode = "NL"
    };

    private static OrderIntakeAddressDto Bloemenhal() => new()
    {
        Name = "Bloemenhal Aalsmeer", Street = "Legmeerdijk", HouseNumber = "313", PostalCode = "1431 GB", City = "Aalsmeer"
    };

    /// <summary>Everything unknown: a new customer, two new places, a new transport unit, product and temperature.</summary>
    private OrderIntakeDto Unknown(TransportRunWorld f) => Payload(f, p =>
    {
        p.Customer = new OrderIntakeCustomerDto
        {
            Name = "Nieuwe Klant B.V.", VatNumber = "NL123456789B01", ChamberOfCommerceNumber = "12345678", EmailAddress = "orders@nieuweklant.nl"
        };
        p.Lines[0].TransportUnitName = "Gitterbox";
        p.Lines[0].ProductName = "Bloemen";
        p.Lines[0].TemperatureName = "Diepvries";
        p.Lines[0].Stops[0].Address = Kwekerij();
        p.Lines[0].Stops[1].Address = Bloemenhal();
    });

    private static OrderIntakeSubmissionDto Submission(OrderIntakeDto payload, bool trusted = true, bool autoConfirm = true, decimal? threshold = null,
        Guid? document = null, int index = 0, Guid? defaultCustomer = null, string? sender = null, Guid? defaultOrderType = null) => new()
    {
        SourceDocumentId = document ?? Guid.NewGuid(),
        IntakeIndex = index,
        Channel = "Gmail",
        SourceKind = OrderSource.Import,
        SenderAddress = sender,
        Subject = "Order",
        FileName = "order.pdf",
        Payload = payload,
        MappingConfidence = 0.9m,
        ProfileTrusted = trusted,
        AutoConfirm = autoConfirm,
        DefaultCustomerRelationId = defaultCustomer,
        DefaultTransportOrderTypeId = defaultOrderType,
        NameSimilarityThreshold = threshold
    };

    private async Task<OrderIntakeFieldResolutionDto> FieldAsync(Guid intakeId, string path)
        => (await Review.GetAsync(intakeId)).Resolution.Single(r => r.FieldPath == path);

    private Task<List<IntakeAlias>> AliasesAsync() => WithUnitOfWorkAsync(() => GetRequiredService<IRepository<IntakeAlias, Guid>>().GetListAsync());

    private Task<Relation> RelationRowAsync(Guid id) => WithUnitOfWorkAsync(() => GetRequiredService<IRepository<Relation, Guid>>().GetAsync(id, includeDetails: true));

    private Task<Relation> RelationOfAddressAsync(Guid addressId) => WithUnitOfWorkAsync(async () =>
        (await GetRequiredService<IRepository<Relation, Guid>>().GetListAsync(r => r.Addresses.Any(a => a.Id == addressId), includeDetails: true)).Single());

    private Task<List<Relation>> RelationsNamedAsync(string name) => WithUnitOfWorkAsync(() =>
        GetRequiredService<IRepository<Relation, Guid>>().GetListAsync(r => r.Name == name, includeDetails: true));

    private sealed record Counts(long Relations, long Units, long Products, long Temperatures);

    private Task<Counts> CountsAsync() => WithUnitOfWorkAsync(async () => new Counts(
        await GetRequiredService<IRepository<Relation, Guid>>().GetCountAsync(),
        await GetRequiredService<IRepository<TransportUnit, Guid>>().GetCountAsync(),
        await GetRequiredService<IRepository<Product, Guid>>().GetCountAsync(),
        await GetRequiredService<IRepository<Temperature, Guid>>().GetCountAsync()));

    private async Task<Guid> RelationWithKeysAsync(TransportRunWorld f, string code, string name, string? vat = null, string? coc = null, string? email = null,
        string street = "Kade", string number = "1", string postalCode = "3011 AA", string city = "Rotterdam")
    {
        var relation = await GetRequiredService<IRelationAppService>().CreateAsync(new RelationCreateDto
        {
            Code = code, Name = name, Roles = RelationRoles.Customer, VatNumber = vat, ChamberOfCommerceNumber = coc, Email = email,
            Addresses =
            [
                new RelationAddressDto
                {
                    CountryId = f.CountryId, Street = street, HouseNumber = number, PostalCode = postalCode, City = city,
                    Latitude = 51.9, Longitude = 4.5, TimeZoneId = "Europe/Amsterdam"
                }
            ]
        });
        return relation.Id;
    }

    /* ---------- The fuzzy rule ---------- */

    [Fact]
    public async Task A_typo_in_the_customer_name_is_a_fuzzy_match_with_its_score_confirms_and_is_learned_as_an_alias()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();
        var jansen = await RelationWithKeysAsync(f, "JT" + f.Suffix, "Jansen Transport B.V.");
        var before = await CountsAsync();
        var typo = Payload(f, p => p.Customer = new OrderIntakeCustomerDto { Name = "Jansen Transprot BV" });

        var result = await Intake.SubmitAsync(Submission(typo));

        result.Status.ShouldBe(OrderIntakeStatus.OrderCreated, string.Join(" | ", (await Review.GetAsync(result.IntakeId)).Resolution.Select(r => r.FieldPath + "=" + r.Status + "/" + r.Method + "/" + r.TargetName + "/" + string.Join(",", r.Candidates.Select(c => c.Name)))));
        result.Confirmed.ShouldBeTrue("build note 31: Fuzzy may confirm");
        (result.FuzzyMatchCount, result.CreatedCount).ShouldBe((1, 0));
        var customer = await FieldAsync(result.IntakeId, IntakeFieldPaths.Customer);
        (customer.Method, customer.TargetId, customer.Score).ShouldBe(((IntakeResolutionMethod?)IntakeResolutionMethod.Fuzzy, (Guid?)jansen, (decimal?)0.94m));
        (await Orders.GetAsync(result.TransportOrderId!.Value)).CustomerRelationId.ShouldBe(jansen);
        (await CountsAsync()).ShouldBe(before, "a fuzzy match creates nothing");
        var alias = (await AliasesAsync()).ShouldHaveSingleItem("the fuzzy resolution is learned once the order exists");
        (alias.Kind, alias.TargetId).ShouldBe((IntakeAliasKind.Customer, jansen));

        var next = await Intake.SubmitAsync(Submission(typo));
        (await FieldAsync(next.IntakeId, IntakeFieldPaths.Customer)).Method.ShouldBe(IntakeResolutionMethod.Alias);
        next.FuzzyMatchCount.ShouldBe(0);
    }

    [Fact]
    public async Task Another_place_below_the_threshold_is_created_not_matched()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        using var culture = UiCulture("en");
        var f = await FixtureAsync();
        var venray = await RelationWithKeysAsync(f, "JV" + f.Suffix, "Jansen Transport Venray");

        var result = await Intake.SubmitAsync(Submission(Payload(f, p => p.Customer = new OrderIntakeCustomerDto { Name = "Jansen Transport Venlo" })));

        result.Status.ShouldBe(OrderIntakeStatus.OrderCreated);
        var customer = await FieldAsync(result.IntakeId, IntakeFieldPaths.Customer);
        customer.Method.ShouldBe(IntakeResolutionMethod.Created, "0.73 is below the default 0.75 (build note 28)");
        customer.TargetId.ShouldNotBe(venray);
        var created = await RelationRowAsync(customer.TargetId!.Value);
        created.Name.ShouldBe("Jansen Transport Venlo");
        result.CreatedCount.ShouldBe(1);
    }

    [Fact]
    public async Task Two_similar_candidates_within_the_margin_are_ambiguous_and_nothing_is_created()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();
        var jansen = await RelationWithKeysAsync(f, "JT" + f.Suffix, "Jansen Transport B.V.");
        var janssen = await RelationWithKeysAsync(f, "JS" + f.Suffix, "Janssen Transport B.V.", number: "2");
        var before = await CountsAsync();

        var result = await Intake.SubmitAsync(Submission(Payload(f, p => p.Customer = new OrderIntakeCustomerDto { Name = "Jansen Transprot" })));

        result.Status.ShouldBe(OrderIntakeStatus.NeedsReview);
        result.Unresolved.ShouldBe([IntakeFieldPaths.Customer]);
        var customer = await FieldAsync(result.IntakeId, IntakeFieldPaths.Customer);
        customer.Status.ShouldBe(IntakeResolutionStatus.Ambiguous);
        customer.Creatable.ShouldBeFalse();
        customer.Candidates.Select(c => c.Id).ShouldBe([jansen, janssen], ignoreOrder: true, string.Join(" | ", customer.Candidates.Select(c => c.Name + " " + c.Detail + " " + c.Id)) + " jansen=" + jansen);
        (await CountsAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task The_threshold_is_the_one_the_intake_was_submitted_with()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();
        var jansen = await RelationWithKeysAsync(f, "JT" + f.Suffix, "Jansen Transport B.V.");
        OrderIntakeDto Typo(bool withDates = true) => Payload(f, p =>
        {
            p.Customer = new OrderIntakeCustomerDto { Name = "Jansen Transprot BV" };
            if (!withDates)
            {
                p.Lines[0].Stops.ForEach(s => s.Date = null);
            }
        });

        var lenient = await Intake.SubmitAsync(Submission(Typo(), threshold: 0.90m));
        (await FieldAsync(lenient.IntakeId, IntakeFieldPaths.Customer)).Method.ShouldBe(IntakeResolutionMethod.Fuzzy, "0.94 ≥ 0.90");
        foreach (var alias in await AliasesAsync())
        {
            await Review.DeleteAliasesAsync(alias.Id); /* the next intakes must resolve by the rule again */
        }

        var strict = await Intake.SubmitAsync(Submission(Typo(), threshold: 0.95m));
        var strictCustomer = await FieldAsync(strict.IntakeId, IntakeFieldPaths.Customer);
        strictCustomer.Method.ShouldBe(IntakeResolutionMethod.Created, "0.94 < 0.95: nothing close enough");
        strictCustomer.TargetId.ShouldNotBe(jansen);
        (await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<OrderIntake, Guid>>().GetAsync(strict.IntakeId))).NameSimilarityThreshold.ShouldBe(0.95m);
    }

    [Fact]
    public async Task The_review_resolves_with_the_threshold_kept_on_the_intake()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();
        var jansen = await RelationWithKeysAsync(f, "JT" + f.Suffix, "Jansen Transport B.V.");
        OrderIntakeDto Typo(bool withDates = true) => Payload(f, p =>
        {
            p.Customer = new OrderIntakeCustomerDto { Name = "Jansen Transprot BV" };
            if (!withDates)
            {
                p.Lines[0].Stops.ForEach(s => s.Date = null);
            }
        });

        /* Kept on the intake (migration OrdersIntakeNameSimilarityThreshold): a review later uses the same 0.95, not the default. */
        var waiting = await Intake.SubmitAsync(Submission(Typo(withDates: false), threshold: 0.95m));
        waiting.Status.ShouldBe(OrderIntakeStatus.NeedsReview, "no stop date and no order date");
        (await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<OrderIntake, Guid>>().GetAsync(waiting.IntakeId))).NameSimilarityThreshold.ShouldBe(0.95m);
        var reviewed = await Review.ResolveAsync(waiting.IntakeId, new OrderIntakeResolveDto { ReviewedPayload = Typo() });
        var reviewedCustomer = reviewed.Resolution.Single(r => r.FieldPath == IntakeFieldPaths.Customer);
        reviewedCustomer.Method.ShouldBe(IntakeResolutionMethod.Created, "the review resolves with the intake's 0.95; the default 0.75 would have matched Jansen");
        reviewedCustomer.TargetId.ShouldNotBe(jansen);
        reviewed.Status.ShouldBe(OrderIntakeStatus.OrderCreated);
    }

    [Fact]
    public async Task A_customer_whose_VAT_number_conflicts_with_the_customer_block_is_ruled_out()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();
        var jansen = await RelationWithKeysAsync(f, "JT" + f.Suffix, "Jansen Transport B.V.", vat: "NL123456789B01", email: "info@jansen.nl");

        var otherVat = await Intake.SubmitAsync(Submission(Payload(f, p => p.Customer = new OrderIntakeCustomerDto { Name = "Jansen Transprot", VatNumber = "NL999999999B01" })));
        var vatField = await FieldAsync(otherVat.IntakeId, IntakeFieldPaths.Customer);
        vatField.Method.ShouldBe(IntakeResolutionMethod.Created, "another VAT number: another company");
        vatField.TargetId.ShouldNotBe(jansen);
    }

    [Fact]
    public async Task A_customer_whose_e_mail_domain_conflicts_with_the_customer_block_is_ruled_out()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();
        var jansen = await RelationWithKeysAsync(f, "JT" + f.Suffix, "Jansen Transport B.V.", vat: "NL123456789B01", email: "info@jansen.nl");

        var otherDomain = await Intake.SubmitAsync(Submission(Payload(f, p => p.Customer = new OrderIntakeCustomerDto { Name = "Jansen Transprot", EmailAddress = "planning@elders.nl" })));
        var domainField = await FieldAsync(otherDomain.IntakeId, IntakeFieldPaths.Customer);
        domainField.Method.ShouldBe(IntakeResolutionMethod.Created, "another e-mail domain in the customer block rules the candidate out");
        domainField.TargetId.ShouldNotBe(jansen);
    }

    [Fact]
    public async Task The_senders_domain_never_rules_a_similar_customer_out()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();
        var jansen = await RelationWithKeysAsync(f, "JT" + f.Suffix, "Jansen Transport B.V.", vat: "NL123456789B01", email: "info@jansen.nl");

        /* A forwarder sends it: only the document's own customer block counts (build note 28). */
        var forwarded = await Intake.SubmitAsync(Submission(Payload(f, p => p.Customer = new OrderIntakeCustomerDto { Name = "Jansen Transprot" }), sender: "planning@forwarder.nl"));
        var forwardedField = await FieldAsync(forwarded.IntakeId, IntakeFieldPaths.Customer);
        (forwardedField.Method, forwardedField.TargetId).ShouldBe(((IntakeResolutionMethod?)IntakeResolutionMethod.Fuzzy, (Guid?)jansen));

        /* The same VAT number is no conflict: it is an exact key match before any name rule. */
        var sameVat = await Intake.SubmitAsync(Submission(Payload(f, p => p.Customer = new OrderIntakeCustomerDto { Name = "Jansen Trasnport", VatNumber = "NL123456789B01" })));
        var sameVatField = await FieldAsync(sameVat.IntakeId, IntakeFieldPaths.Customer);
        (sameVatField.Method, sameVatField.TargetId).ShouldBe(((IntakeResolutionMethod?)IntakeResolutionMethod.Exact, (Guid?)jansen));
    }

    [Fact]
    public async Task A_stop_place_with_a_similar_name_at_the_same_postal_code_is_a_fuzzy_match_but_elsewhere_it_is_not()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();

        var near = await Intake.SubmitAsync(Submission(Payload(f, p => p.Lines[0].Stops[0].Address = new OrderIntakeAddressDto
        {
            Name = "DC Venloo", Street = "Celsiusweg", HouseNumber = "99", PostalCode = "5928 PR", City = "Venlo"
        })));
        var nearField = await FieldAsync(near.IntakeId, "lines[0].stops[0].address");
        (nearField.Method, nearField.TargetId).ShouldBe(((IntakeResolutionMethod?)IntakeResolutionMethod.Fuzzy, (Guid?)f.DepotAddressId));
        nearField.Score.ShouldNotBeNull();
        near.FuzzyMatchCount.ShouldBe(1);

        var inCity = await Intake.SubmitAsync(Submission(Payload(f, p => p.Lines[0].Stops[0].Address = new OrderIntakeAddressDto
        {
            Name = "DC Venlow", Street = "Celsiusweg", HouseNumber = "99", PostalCode = "5900 AA", City = "Venlo"
        })));
        var cityField = await FieldAsync(inCity.IntakeId, "lines[0].stops[0].address");
        (cityField.Method, cityField.TargetId).ShouldBe(((IntakeResolutionMethod?)IntakeResolutionMethod.Fuzzy, (Guid?)f.DepotAddressId), "the same city is enough");

        var elsewhere = await Intake.SubmitAsync(Submission(Payload(f, p => p.Lines[0].Stops[0].Address = new OrderIntakeAddressDto
        {
            Name = "DC Venlou", Street = "Damrak", HouseNumber = "1", PostalCode = "1012 LG", City = "Amsterdam"
        })));
        var elsewhereField = await FieldAsync(elsewhere.IntakeId, "lines[0].stops[0].address");
        elsewhereField.Method.ShouldBe(IntakeResolutionMethod.Created, "no address at that postal code or in that city: a new place");
        elsewhereField.TargetId.ShouldNotBe(f.DepotAddressId);
        (await RelationOfAddressAsync(elsewhereField.TargetId!.Value)).Id.ShouldNotBe(f.DepotId, "DC Venlo has its default address already: a relation of its own");
    }

    [Fact]
    public async Task Transport_units_and_products_with_a_typo_are_fuzzy_matches()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();
        var before = await CountsAsync();

        var result = await Intake.SubmitAsync(Submission(Payload(f, p =>
        {
            p.Lines[0].TransportUnitName = "Europalet";
            p.Lines[0].ProductName = "Zuivell";
        })));

        result.Status.ShouldBe(OrderIntakeStatus.OrderCreated);
        var unit = await FieldAsync(result.IntakeId, "lines[0].transportUnit");
        (unit.Method, unit.TargetId).ShouldBe(((IntakeResolutionMethod?)IntakeResolutionMethod.Fuzzy, (Guid?)f.TransportUnitId));
        var product = await FieldAsync(result.IntakeId, "lines[0].product");
        (product.Method, product.TargetId).ShouldBe(((IntakeResolutionMethod?)IntakeResolutionMethod.Fuzzy, (Guid?)f.ProductId));
        result.FuzzyMatchCount.ShouldBe(2);
        (await CountsAsync()).ShouldBe(before);
    }

    /* ---------- Creation (build note 29) ---------- */

    [Fact]
    public async Task Everything_unknown_is_created_with_the_marker_and_the_order_goes_straight_through()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        using var culture = UiCulture("en");
        var f = await FixtureAsync();
        var before = await CountsAsync();

        var result = await Intake.SubmitAsync(Submission(Unknown(f)));

        result.Status.ShouldBe(OrderIntakeStatus.OrderCreated);
        result.Unresolved.ShouldBeEmpty();
        (result.FuzzyMatchCount, result.CreatedCount).ShouldBe((0, 6), string.Join(" | ", (await Review.GetAsync(result.IntakeId)).Resolution.Select(r => r.FieldPath + "=" + r.Status + "/" + r.Method + "/" + r.TargetName + "/" + r.Score)));
        (await CountsAsync()).ShouldBe(before with { Relations = before.Relations + 3, Units = before.Units + 1, Products = before.Products + 1, Temperatures = before.Temperatures + 1 });

        var detail = await Review.GetAsync(result.IntakeId);
        foreach (var path in new[] { IntakeFieldPaths.Customer, "lines[0].stops[0].address", "lines[0].stops[1].address", "lines[0].transportUnit", "lines[0].product", "lines[0].temperature" })
        {
            var field = detail.Resolution.Single(r => r.FieldPath == path);
            (field.Status, field.Method, field.Creatable).ShouldBe((IntakeResolutionStatus.Resolved, (IntakeResolutionMethod?)IntakeResolutionMethod.Created, false), path);
            field.TargetId.ShouldNotBeNull(path);
        }

        /* The customer: role Customer, the block's keys, the marker and the external identifier "{intakeId} {field path}". */
        var customer = await RelationRowAsync(detail.Resolution.Single(r => r.FieldPath == IntakeFieldPaths.Customer).TargetId!.Value);
        customer.Name.ShouldBe("Nieuwe Klant B.V.");
        customer.Roles.ShouldBe(RelationRoles.Customer);
        customer.TenantId.ShouldBe(TenantA);
        (customer.VatNumber, customer.ChamberOfCommerceNumber, customer.Email).ShouldBe(("NL123456789B01", "12345678", "orders@nieuweklant.nl"));
        customer.Description.ShouldNotBeNull();
        customer.Description!.ShouldStartWith(CreatedMarkerEn);
        var external = customer.ExternalIdentifiers.ShouldHaveSingleItem();
        (external.Source, external.Identifier).ShouldBe((IMasterDataIntakeWriter.SourceName, result.IntakeId.ToString("N") + " " + IntakeFieldPaths.Customer));
        customer.Addresses.ShouldBeEmpty("the canonical model has no customer address");

        /* The places: new relations with role Customer (PO 2026-11-03), the received address as their default address. */
        var order = await Orders.GetAsync(result.TransportOrderId!.Value);
        order.CustomerRelationId.ShouldBe(customer.Id);
        var pickup = await RelationOfAddressAsync(order.Lines[0].Stops[0].RelationAddressId);
        pickup.Name.ShouldBe("Kwekerij De Bloem");
        pickup.Roles.ShouldBe(RelationRoles.Customer);
        pickup.Description!.ShouldStartWith(CreatedMarkerEn);
        pickup.ExternalIdentifiers.ShouldHaveSingleItem().Source.ShouldBe(IMasterDataIntakeWriter.SourceName);
        var pickupAddress = pickup.Addresses.ShouldHaveSingleItem();
        (pickupAddress.AddressTypeId, pickupAddress.Street, pickupAddress.HouseNumber, pickupAddress.PostalCode, pickupAddress.City, pickupAddress.CountryId)
            .ShouldBe(((Guid?)null, "Teeltweg", (string?)"3", "2671 AA", "Naaldwijk", f.CountryId));
        var delivery = await RelationOfAddressAsync(order.Lines[0].Stops[1].RelationAddressId);
        delivery.Name.ShouldBe("Bloemenhal Aalsmeer");
        delivery.Addresses.Single().CountryId.ShouldBe(f.CountryId, "no country on the document: the default country, else NL");

        var unit = await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<TransportUnit, Guid>>().GetAsync(order.Lines[0].TransportUnitId));
        (unit.Name, unit.TenantId).ShouldBe(("Gitterbox", (Guid?)TenantA));
        unit.Description!.ShouldStartWith(CreatedMarkerEn);
        var product = await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<Product, Guid>>().GetAsync(order.Lines[0].ProductId!.Value));
        product.Name.ShouldBe("Bloemen");
        product.Description!.ShouldStartWith(CreatedMarkerEn);
        var temperature = await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<Temperature, Guid>>().GetAsync(order.Lines[0].TemperatureId!.Value));
        temperature.Name.ShouldBe("Diepvries");
        temperature.Description!.ShouldStartWith(CreatedMarkerEn);

        /* Learned, so the next intake of the same texts finds them by alias (build note 32). */
        (await AliasesAsync()).Select(a => a.Kind).ShouldBe(
            [IntakeAliasKind.Customer, IntakeAliasKind.Address, IntakeAliasKind.Address, IntakeAliasKind.TransportUnit, IntakeAliasKind.Product, IntakeAliasKind.Temperature],
            ignoreOrder: true);
    }

    [Fact]
    public async Task An_intake_that_created_its_master_data_may_be_confirmed_at_once()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();

        /* A known customer (its confirm rules pass), only the rest created. */
        var result = await Intake.SubmitAsync(Submission(Payload(f, p =>
        {
            p.Lines[0].TransportUnitName = "Gitterbox";
            p.Lines[0].Stops[1].Address = Bloemenhal();
        })));

        result.CreatedCount.ShouldBe(2);
        result.Confirmed.ShouldBeTrue("build note 31: Created may lead to a confirmed order");
    }

    [Fact]
    public async Task A_relation_without_a_default_address_takes_the_new_place_as_its_default_address()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();
        var bakker = await GetRequiredService<IMasterDataIntakeWriter>().CreateRelationAsync(new IntakeRelationCreateDto
        {
            Name = "Bakker Logistiek Holding", SourceReference = "setup " + f.Suffix
        });
        bakker.Succeeded.ShouldBeTrue();

        var result = await Intake.SubmitAsync(Submission(Payload(f, p => p.Lines[0].Stops[1].Address = new OrderIntakeAddressDto
        {
            Name = "Bakker Logistiek Holding BV", Street = "Industrieweg", HouseNumber = "5", PostalCode = "4004 KA", City = "Tiel"
        })));

        var field = await FieldAsync(result.IntakeId, "lines[0].stops[1].address");
        field.Method.ShouldBe(IntakeResolutionMethod.Created);
        var owner = await RelationOfAddressAsync(field.TargetId!.Value);
        owner.Id.ShouldBe(bakker.Id!.Value, "the name matches a relation that has no default address yet");
        owner.Addresses.ShouldHaveSingleItem().City.ShouldBe("Tiel");
        (await RelationsNamedAsync("Bakker Logistiek Holding BV")).ShouldBeEmpty("no second relation");
    }

    [Fact]
    public async Task A_new_place_named_twice_in_one_intake_is_created_once()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();
        var payload = Unknown(f);
        payload.Lines.Add(new OrderIntakeLineDto
        {
            TransportUnitName = "Gitterbox", Quantity = 1,
            Stops =
            [
                new OrderIntakeStopDto { Role = IntakeStopRole.Pickup, Date = payload.Lines[0].Stops[0].Date, Address = Kwekerij() },
                new OrderIntakeStopDto { Role = IntakeStopRole.Delivery, Date = payload.Lines[0].Stops[1].Date, Address = Bloemenhal() }
            ]
        });
        var before = await CountsAsync();

        var result = await Intake.SubmitAsync(Submission(payload));

        result.Status.ShouldBe(OrderIntakeStatus.OrderCreated);
        (await CountsAsync()).ShouldBe(before with { Relations = before.Relations + 3, Units = before.Units + 1, Products = before.Products + 1, Temperatures = before.Temperatures + 1 },
            "two stops at one new place create one relation; two lines of one new unit create one unit");
        var order = await Orders.GetAsync(result.TransportOrderId!.Value);
        order.Lines[1].Stops[0].RelationAddressId.ShouldBe(order.Lines[0].Stops[0].RelationAddressId);
        order.Lines[1].TransportUnitId.ShouldBe(order.Lines[0].TransportUnitId);
        (await RelationsNamedAsync("Kwekerij De Bloem")).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_same_intake_again_and_the_next_intake_of_the_same_message_create_nothing_new()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();
        var document = Guid.NewGuid();
        var first = await Intake.SubmitAsync(Submission(Unknown(f), document: document));
        var afterFirst = await CountsAsync();

        /* A reprocess or a retried job answers the intake it holds. */
        var again = await Intake.SubmitAsync(Submission(Unknown(f), document: document));
        again.IntakeId.ShouldBe(first.IntakeId);
        again.TransportOrderId.ShouldBe(first.TransportOrderId);
        (await CountsAsync()).ShouldBe(afterFirst);

        /* The same message read again as a new document (another mailbox sync, a second attachment): resolved by the learned aliases. */
        var next = await Intake.SubmitAsync(Submission(Unknown(f)));
        next.Status.ShouldBe(OrderIntakeStatus.OrderCreated);
        next.CreatedCount.ShouldBe(0);
        (await CountsAsync()).ShouldBe(afterFirst, "build note 32: nothing is created twice");
        var detail = await Review.GetAsync(next.IntakeId);
        foreach (var path in new[] { IntakeFieldPaths.Customer, "lines[0].stops[0].address", "lines[0].stops[1].address", "lines[0].transportUnit", "lines[0].product", "lines[0].temperature" })
        {
            detail.Resolution.Single(r => r.FieldPath == path).Method.ShouldBe(IntakeResolutionMethod.Alias, path);
        }

        var firstOrder = await Orders.GetAsync(first.TransportOrderId!.Value);
        var nextOrder = await Orders.GetAsync(next.TransportOrderId!.Value);
        nextOrder.CustomerRelationId.ShouldBe(firstOrder.CustomerRelationId);
        nextOrder.Lines[0].Stops.Select(s => s.RelationAddressId).ShouldBe(firstOrder.Lines[0].Stops.Select(s => s.RelationAddressId));
    }

    [Fact]
    public async Task A_place_master_data_refuses_keeps_the_intake_in_review()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();

        var result = await Intake.SubmitAsync(Submission(Payload(f, p => p.Lines[0].Stops[1].Address = new OrderIntakeAddressDto
        {
            Name = "Entrepot Atlantis", Street = "Nergensweg", HouseNumber = "1", PostalCode = "99999", City = "Atlantis", CountryCode = "XX"
        })));

        result.Status.ShouldBe(OrderIntakeStatus.NeedsReview, "a country Master Data does not have");
        result.TransportOrderId.ShouldBeNull();
        result.Unresolved.ShouldContain("lines[0].stops[1].address");
        var field = await FieldAsync(result.IntakeId, "lines[0].stops[1].address");
        (field.Status, field.Creatable).ShouldBe((IntakeResolutionStatus.Unresolved, false));
        (await RelationsNamedAsync("Entrepot Atlantis")).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_customer_without_a_name_is_named_after_the_senders_domain()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();

        var result = await Intake.SubmitAsync(Submission(Payload(f, p => p.Customer = null), sender: "planning@nieuwe-verlader.nl"));

        result.Status.ShouldBe(OrderIntakeStatus.OrderCreated);
        var customer = await FieldAsync(result.IntakeId, IntakeFieldPaths.Customer);
        customer.Method.ShouldBe(IntakeResolutionMethod.Created);
        var relation = await RelationRowAsync(customer.TargetId!.Value);
        relation.Name.ShouldBe("nieuwe-verlader.nl");
        relation.Email.ShouldBe("planning@nieuwe-verlader.nl", "the sender's address when the block has none");
    }

    /* ---------- The straight-through matrix (build note 31) ---------- */

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public async Task Every_rule_may_confirm_when_the_mapping_may_and_the_channel_auto_confirms_otherwise_a_draft(bool mayConfirm, bool autoConfirm, bool confirmed)
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();
        /* Exact (code), Match (name + city), Fuzzy (unit), Created (product), Pattern (waypoint types), Default (the only order type). */
        var payload = Payload(f, p =>
        {
            p.Lines[0].Stops[0].Address = new OrderIntakeAddressDto { Name = "DC Venlo", City = "Venlo" };
            p.Lines[0].TransportUnitName = "Europalet";
            p.Lines[0].ProductName = "Bloemen " + f.Suffix;
        });

        var result = await Intake.SubmitAsync(Submission(payload, trusted: mayConfirm, autoConfirm: autoConfirm));

        result.Status.ShouldBe(OrderIntakeStatus.OrderCreated, "an unverified AI extraction or a channel without auto-confirm is a Draft, never a review");
        result.Confirmed.ShouldBe(confirmed);
        (await Orders.GetAsync(result.TransportOrderId!.Value)).Status.ShouldBe(confirmed ? TransportOrderStatus.Open : TransportOrderStatus.Draft);
        var methods = (await Review.GetAsync(result.IntakeId)).Resolution.Select(r => r.Method).ToList();
        methods.ShouldContain(IntakeResolutionMethod.Match);
        methods.ShouldContain(IntakeResolutionMethod.Fuzzy);
        methods.ShouldContain(IntakeResolutionMethod.Created);
    }

    [Fact]
    public async Task A_clerks_pick_never_confirms_by_itself()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();
        await RelationWithKeysAsync(f, "JT" + f.Suffix, "Jansen Transport B.V.");
        await RelationWithKeysAsync(f, "JS" + f.Suffix, "Janssen Transport B.V.", number: "2");
        var open = await Intake.SubmitAsync(Submission(Payload(f, p => p.Customer = new OrderIntakeCustomerDto { Name = "Jansen Transprot" })));
        open.Status.ShouldBe(OrderIntakeStatus.NeedsReview);

        var resolved = await Review.ResolveAsync(open.IntakeId, new OrderIntakeResolveDto { Picks = new Dictionary<string, Guid> { [IntakeFieldPaths.Customer] = f.CustomerId } });

        resolved.Status.ShouldBe(OrderIntakeStatus.OrderCreated);
        (await Orders.GetAsync(resolved.TransportOrderId!.Value)).Status.ShouldBe(TransportOrderStatus.Draft, "Create order without confirm: the clerk confirms (User)");
    }

    /* ---------- Tenant isolation (constitution rule 1) ---------- */

    [Fact]
    public async Task Tenant_Bs_intake_never_fuzzy_matches_or_reuses_tenant_As_rows_and_creates_its_own()
    {
        using var features = IntakeOn();
        TransportRunWorld fa;
        Guid jansenOfA;
        OrderIntakeSubmissionResultDto inA;
        Counts countsOfA;
        using (InTenantA())
        {
            fa = await FixtureAsync();
            jansenOfA = await RelationWithKeysAsync(fa, "JT" + fa.Suffix, "Jansen Transport B.V.");
            inA = await Intake.SubmitAsync(Submission(Unknown(fa)));
            inA.Status.ShouldBe(OrderIntakeStatus.OrderCreated);
            countsOfA = await CountsAsync();
        }

        var aRows = new HashSet<Guid> { jansenOfA, fa.CustomerId, fa.DepotAddressId, fa.ShopAddressId, fa.TransportUnitId, fa.ProductId };
        using (InTenantA())
        {
            foreach (var field in (await Review.GetAsync(inA.IntakeId)).Resolution.Where(r => r.TargetId != null))
            {
                aRows.Add(field.TargetId!.Value);
            }
        }

        using (InTenantB())
        {
            var fb = await FixtureAsync();
            /* The texts A has rows and aliases for: a typo of A's relation, A's created customer, places, unit, product, temperature. */
            var payload = Unknown(fb);
            var typo = Payload(fb, p =>
            {
                p.Customer = new OrderIntakeCustomerDto { Name = "Jansen Transprot BV" };
                p.Lines[0].TransportUnitName = "Gitterbox";
            });

            var created = await Intake.SubmitAsync(Submission(payload));
            var fuzzy = await Intake.SubmitAsync(Submission(typo));

            created.Status.ShouldBe(OrderIntakeStatus.OrderCreated);
            created.CreatedCount.ShouldBe(6, "B creates its own rows: A's aliases and rows are invisible");
            fuzzy.Status.ShouldBe(OrderIntakeStatus.OrderCreated);
            fuzzy.FuzzyMatchCount.ShouldBe(0, "A's Jansen Transport B.V. is no candidate in B");
            foreach (var id in new[] { created.IntakeId, fuzzy.IntakeId })
            {
                var resolution = (await Review.GetAsync(id)).Resolution;
                resolution.Where(r => r.TargetId != null).Select(r => r.TargetId!.Value).ShouldNotContain(t => aRows.Contains(t), "no field of B resolves to A's rows");
                resolution.SelectMany(r => r.Candidates).Select(c => c.Id).ShouldNotContain(c => aRows.Contains(c), "no candidate of B is A's row");
            }

            (await Review.GetAsync(created.IntakeId)).Resolution.Select(r => r.Method).ShouldNotContain(IntakeResolutionMethod.Alias, "A's aliases for the same texts are not B's");

            var fuzzyCustomer = (await Review.GetAsync(fuzzy.IntakeId)).Resolution.Single(r => r.FieldPath == IntakeFieldPaths.Customer);
            fuzzyCustomer.Method.ShouldBe(IntakeResolutionMethod.Created);
            (await RelationRowAsync(fuzzyCustomer.TargetId!.Value)).TenantId.ShouldBe(TenantB, "the writer creates in the current tenant");
            (await RelationsNamedAsync("Nieuwe Klant B.V.")).ShouldHaveSingleItem().TenantId.ShouldBe(TenantB);
            (await AliasesAsync()).ShouldAllBe(a => a.TenantId == TenantB);
        }

        using (InTenantA())
        {
            (await CountsAsync()).ShouldBe(countsOfA, "nothing of B's intakes landed in A");
            (await RelationsNamedAsync("Jansen Transprot BV")).ShouldBeEmpty();
            (await RelationsNamedAsync("Nieuwe Klant B.V.")).ShouldHaveSingleItem().TenantId.ShouldBe(TenantA);
        }
    }

    /* ---------- The order type: the tenant's default (coordinator follow-up, 2026-11-03) ---------- */

    /// <summary>A second active order type ("Express") with the same waypoint pattern, and which type is the tenant's IsDefault (null: none).</summary>
    private async Task<Guid> ExpressTypeAsync(TransportRunWorld f, bool? expressIsDefault)
    {
        var express = await GetRequiredService<ITransportOrderTypeAppService>().CreateAsync(new TransportOrderTypeCreateDto
        {
            Code = "EXP" + f.Suffix, Names = OrdersNames("Express", "Spoed"),
            Numbering = new() { Prefix = "EX", IncludeYear = true, Separator = "-", Digits = 5 },
            DefaultWaypoints = [new() { WaypointTypeId = f.LoadTypeId, Position = 1 }, new() { WaypointTypeId = f.UnloadTypeId, Position = 2 }]
        });
        await WithUnitOfWorkAsync(async () =>
        {
            var types = GetRequiredService<IRepository<TransportOrderType, Guid>>();
            foreach (var type in await types.GetListAsync())
            {
                type.SetDefault(expressIsDefault switch { true => type.Id == express.Id, false => type.Id == f.OrderTypeId, null => false });
                await types.UpdateAsync(type, autoSave: true);
            }
        });
        return express.Id;
    }

    [Fact]
    public async Task Several_active_types_without_a_name_or_channel_default_take_the_tenants_default_and_go_straight_through()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();
        var express = await ExpressTypeAsync(f, expressIsDefault: true);

        var result = await Intake.SubmitAsync(Submission(Payload(f)));

        result.Status.ShouldBe(OrderIntakeStatus.OrderCreated);
        result.Confirmed.ShouldBeTrue("Default is a rule: the order goes straight through");
        var orderType = await FieldAsync(result.IntakeId, IntakeFieldPaths.OrderType);
        (orderType.Method, orderType.TargetId).ShouldBe(((IntakeResolutionMethod?)IntakeResolutionMethod.Default, (Guid?)express));
        (await Orders.GetAsync(result.TransportOrderId!.Value)).TransportOrderTypeId.ShouldBe(express);
    }

    [Fact]
    public async Task A_given_order_type_name_wins_over_the_tenants_default()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();
        await ExpressTypeAsync(f, expressIsDefault: true);

        var result = await Intake.SubmitAsync(Submission(Payload(f, p => p.OrderTypeName = "Standaard")));

        var orderType = await FieldAsync(result.IntakeId, IntakeFieldPaths.OrderType);
        (orderType.Method, orderType.TargetId).ShouldBe(((IntakeResolutionMethod?)IntakeResolutionMethod.Exact, (Guid?)f.OrderTypeId));
        result.Status.ShouldBe(OrderIntakeStatus.OrderCreated);
    }

    [Fact]
    public async Task The_channel_default_wins_over_the_tenants_default()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();
        await ExpressTypeAsync(f, expressIsDefault: true);

        var result = await Intake.SubmitAsync(Submission(Payload(f), defaultOrderType: f.OrderTypeId));

        var orderType = await FieldAsync(result.IntakeId, IntakeFieldPaths.OrderType);
        (orderType.Method, orderType.TargetId).ShouldBe(((IntakeResolutionMethod?)IntakeResolutionMethod.Default, (Guid?)f.OrderTypeId));
        result.Status.ShouldBe(OrderIntakeStatus.OrderCreated);
    }

    [Fact]
    public async Task Several_active_types_and_no_default_among_them_keep_the_order_type_in_review()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();
        var express = await ExpressTypeAsync(f, expressIsDefault: null);

        var result = await Intake.SubmitAsync(Submission(Payload(f)));

        result.Status.ShouldBe(OrderIntakeStatus.NeedsReview);
        result.Unresolved.ShouldBe([IntakeFieldPaths.OrderType]);
        var orderType = await FieldAsync(result.IntakeId, IntakeFieldPaths.OrderType);
        orderType.Status.ShouldBe(IntakeResolutionStatus.Ambiguous);
        orderType.Candidates.Select(c => c.Id).ShouldBe([f.OrderTypeId, express], ignoreOrder: true);
    }
}
