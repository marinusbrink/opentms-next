using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Features;
using OpenTms.MasterData.Published;
using OpenTms.Orders.Intake;
using OpenTms.Orders.Published;
using OpenTms.Orders.TransportOrders;
using OpenTms.TransportRun;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Orders;

using OrdersErrorCodes = OpenTms.Orders.OrdersErrorCodes;

/// <summary>
/// Orders' side of design 72 on SQLite with the master data of <see cref="TransportRunFixture"/>: the resolver's rules in
/// their order (fixed customer, given id, code, name as a conservative Match, default; postcode + house number Exact, name +
/// city Match; the order type's waypoint pattern; transport unit by name), the order created as Draft or — only when the
/// profile is trusted, the channel auto-confirms and nothing was a Match — Confirmed, NeedsReview with the open fields,
/// idempotency on (document, index), the review (picks learned as aliases, reject), learning Match fields on an unedited
/// confirmation (build note 2), the stop contact in the remarks (build note 4), the feature gate and tenant isolation of
/// intakes, aliases and the Master Data match query. Risk class: High (order chain), Critical for tenant isolation.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class OrderIntakeResolutionTests : TransportOrderTestBase<OpenTmsEntityFrameworkCoreTestModule>
{
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
                            Role = IntakeStopRole.Delivery, Date = date, ContactName = "Jan Jansen", ContactPhone = "06-12345678",
                            Address = new OrderIntakeAddressDto { Name = "Supermarkt Utrecht", PostalCode = "3511BD", HouseNumber = "40", City = "Utrecht" }
                        }
                    ]
                }
            ]
        };
        change?.Invoke(payload);
        return payload;
    }

    private static OrderIntakeSubmissionDto Submission(OrderIntakeDto payload, bool trusted = false, bool autoConfirm = false, Guid? fixedCustomer = null,
        Guid? defaultCustomer = null, Guid? document = null, int index = 0, string? sender = null) => new()
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
        FixedCustomerRelationId = fixedCustomer,
        DefaultCustomerRelationId = defaultCustomer
    };

    private async Task<OrderIntakeFieldResolutionDto> FieldAsync(Guid intakeId, string path)
        => (await Review.GetAsync(intakeId)).Resolution.Single(r => r.FieldPath == path);

    private Task<List<IntakeAlias>> AliasesAsync() => WithUnitOfWorkAsync(() => GetRequiredService<IRepository<IntakeAlias, Guid>>().GetListAsync());

    /* ---------- Resolution and the order ---------- */

    [Fact]
    public async Task A_complete_intake_becomes_a_draft_when_the_profile_is_not_trusted()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();

        var result = await Intake.SubmitAsync(Submission(Payload(f), trusted: false, autoConfirm: true));

        result.Status.ShouldBe(OrderIntakeStatus.OrderCreated);
        result.Confirmed.ShouldBeFalse();
        result.Unresolved.ShouldBeEmpty();
        var order = await Orders.GetAsync(result.TransportOrderId!.Value);
        order.Status.ShouldBe(TransportOrderStatus.Draft);
        order.Source.ShouldBe(OrderSource.Import);
        order.SourceIntakeId.ShouldBe(result.IntakeId);
        order.DraftConfidence.ShouldBe(0.9m);
        order.CustomerRelationId.ShouldBe(f.CustomerId);
        order.CustomerReference.ShouldBe("PO-" + f.Suffix);
        var line = order.Lines.ShouldHaveSingleItem();
        line.TransportUnitId.ShouldBe(f.TransportUnitId);
        line.Stops.Select(s => s.RelationAddressId).ShouldBe([f.DepotAddressId, f.ShopAddressId]);
        line.Stops.Select(s => s.WaypointTypeId).ShouldBe([f.LoadTypeId, f.UnloadTypeId], "the order type's pattern: Pickup = first Load, Delivery = first Unload");
        line.Stops[1].Remarks.ShouldNotBeNull();
        line.Stops[1].Remarks!.ShouldContain("Jan Jansen", Case.Sensitive, "build note 4: the stop contact goes into the remarks");
        line.Stops[1].Remarks!.ShouldContain("06-12345678");

        (await FieldAsync(result.IntakeId, "order.customer")).Method.ShouldBe(IntakeResolutionMethod.Exact);
        (await FieldAsync(result.IntakeId, "lines[0].stops[0].address")).Method.ShouldBe(IntakeResolutionMethod.Exact);
        (await FieldAsync(result.IntakeId, "lines[0].stops[0].waypointType")).Method.ShouldBe(IntakeResolutionMethod.Pattern);
        (await FieldAsync(result.IntakeId, "lines[0].transportUnit")).Method.ShouldBe(IntakeResolutionMethod.Exact);
    }

    [Fact]
    public async Task A_trusted_auto_confirming_intake_resolved_by_keys_only_is_confirmed_at_once()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();

        var result = await Intake.SubmitAsync(Submission(Payload(f), trusted: true, autoConfirm: true));

        result.Confirmed.ShouldBeTrue();
        (await Orders.GetAsync(result.TransportOrderId!.Value)).Status.ShouldNotBe(TransportOrderStatus.Draft);
    }

    [Fact]
    public async Task Trusted_without_auto_confirm_stays_a_draft()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();

        (await Intake.SubmitAsync(Submission(Payload(f), trusted: true, autoConfirm: false))).Confirmed.ShouldBeFalse();
    }

    [Fact]
    public async Task A_customer_found_by_its_name_is_a_match_and_never_confirmed_automatically()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();

        var result = await Intake.SubmitAsync(Submission(Payload(f, p => p.Customer = new OrderIntakeCustomerDto { Name = "klant foods b.v." }), trusted: true, autoConfirm: true));

        result.Status.ShouldBe(OrderIntakeStatus.OrderCreated);
        result.Confirmed.ShouldBeFalse("build note 2: a name match is conservative");
        var customer = await FieldAsync(result.IntakeId, "order.customer");
        (customer.Method, customer.TargetId).ShouldBe(((IntakeResolutionMethod?)IntakeResolutionMethod.Match, (Guid?)f.CustomerId));
    }

    [Fact]
    public async Task An_address_found_by_name_and_city_is_a_match_and_never_confirmed_automatically()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();

        var result = await Intake.SubmitAsync(Submission(Payload(f, p => p.Lines[0].Stops[0].Address = new OrderIntakeAddressDto { Name = "DC Venlo", City = "Venlo" }),
            trusted: true, autoConfirm: true));

        result.Confirmed.ShouldBeFalse();
        var address = await FieldAsync(result.IntakeId, "lines[0].stops[0].address");
        (address.Method, address.TargetId).ShouldBe(((IntakeResolutionMethod?)IntakeResolutionMethod.Match, (Guid?)f.DepotAddressId));
    }

    [Fact]
    public async Task The_api_clients_fixed_customer_wins_over_whatever_the_payload_names()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();

        var result = await Intake.SubmitAsync(Submission(Payload(f, p => p.Customer = new OrderIntakeCustomerDto { Code = "SH" + f.Suffix, RelationId = f.ShopId }),
            fixedCustomer: f.CustomerId));

        (await Orders.GetAsync(result.TransportOrderId!.Value)).CustomerRelationId.ShouldBe(f.CustomerId);
        (await FieldAsync(result.IntakeId, "order.customer")).Method.ShouldBe(IntakeResolutionMethod.Given);
    }

    [Fact]
    public async Task An_unknown_customer_takes_the_channel_default()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();

        var result = await Intake.SubmitAsync(Submission(Payload(f, p => p.Customer = new OrderIntakeCustomerDto { Name = "Onbekend BV" }), defaultCustomer: f.CustomerId));

        result.Status.ShouldBe(OrderIntakeStatus.OrderCreated);
        (await FieldAsync(result.IntakeId, "order.customer")).Method.ShouldBe(IntakeResolutionMethod.Default);
    }

    [Fact]
    public async Task Open_fields_leave_the_intake_in_review_without_an_order_and_the_same_intake_twice_is_one_intake()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();
        var document = Guid.NewGuid();
        var payload = Payload(f, p =>
        {
            p.Customer = new OrderIntakeCustomerDto { Name = "Onbekend BV" };
            p.Lines[0].TransportUnitName = "Gitterbox";
        });

        var first = await Intake.SubmitAsync(Submission(payload, document: document));
        var again = await Intake.SubmitAsync(Submission(payload, document: document));

        first.Status.ShouldBe(OrderIntakeStatus.NeedsReview);
        first.TransportOrderId.ShouldBeNull();
        first.Unresolved.ShouldBe(["order.customer", "lines[0].transportUnit"], ignoreOrder: true);
        again.IntakeId.ShouldBe(first.IntakeId);
        (await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<OrderIntake, Guid>>().CountAsync(i => i.SourceDocumentId == document))).ShouldBe(1);
        (await Intake.SubmitAsync(Submission(payload, document: document, index: 1))).IntakeId.ShouldNotBe(first.IntakeId, "the key is (document, index)");
    }

    [Fact]
    public async Task Submitting_is_gated_by_the_intake_feature()
    {
        using var off = TestFeatureValueProvider.Override(IntakeConsts.OrderIntakeFeature, "false");
        using var tenant = InTenantA();
        var f = await FixtureAsync();

        await Should.ThrowAsync<AbpAuthorizationException>(() => Intake.SubmitAsync(Submission(Payload(f))));
    }

    /* ---------- Review and aliases ---------- */

    [Fact]
    public async Task A_review_pick_creates_the_order_and_is_learned_so_the_next_intake_resolves_by_alias_and_may_auto_confirm()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();
        var unknown = Payload(f, p => p.Customer = new OrderIntakeCustomerDto { Name = "Klant Foods Barendrecht Inkoop" });
        var open = await Intake.SubmitAsync(Submission(unknown));
        open.Status.ShouldBe(OrderIntakeStatus.NeedsReview);

        var resolved = await Review.ResolveAsync(open.IntakeId, new OrderIntakeResolveDto { Picks = new Dictionary<string, Guid> { ["order.customer"] = f.CustomerId } });

        resolved.Status.ShouldBe(OrderIntakeStatus.OrderCreated);
        resolved.TransportOrderId.ShouldNotBeNull();
        resolved.Resolution.Single(r => r.FieldPath == "order.customer").Method.ShouldBe(IntakeResolutionMethod.User);
        var alias = (await AliasesAsync()).ShouldHaveSingleItem();
        (alias.Kind, alias.TargetId).ShouldBe((IntakeAliasKind.Customer, f.CustomerId));

        var next = await Intake.SubmitAsync(Submission(unknown, trusted: true, autoConfirm: true));
        next.Confirmed.ShouldBeTrue("an alias is trusted (design 72: Given/Exact/Alias/Pattern/Default)");
        (await FieldAsync(next.IntakeId, "order.customer")).Method.ShouldBe(IntakeResolutionMethod.Alias);
        (await AliasesAsync()).Single().UseCount.ShouldBeGreaterThan(alias.UseCount);
    }

    [Fact]
    public async Task A_review_with_open_fields_left_creates_nothing_and_an_invalid_pick_is_refused()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();
        var open = await Intake.SubmitAsync(Submission(Payload(f, p =>
        {
            p.Customer = new OrderIntakeCustomerDto { Name = "Onbekend" };
            p.Lines[0].TransportUnitName = "Gitterbox";
        })));

        (await Should.ThrowAsync<BusinessException>(() => Review.ResolveAsync(open.IntakeId,
            new OrderIntakeResolveDto { Picks = new Dictionary<string, Guid> { ["order.customer"] = f.CustomerId } }))).Code.ShouldBe(OrdersErrorCodes.IntakeUnresolved);
        (await Should.ThrowAsync<BusinessException>(() => Review.ResolveAsync(open.IntakeId,
            new OrderIntakeResolveDto { Picks = new Dictionary<string, Guid> { ["order.customer"] = Guid.NewGuid(), ["lines[0].transportUnit"] = f.TransportUnitId } }))).Code
            .ShouldBe(OrdersErrorCodes.IntakePickInvalid, "a relation that does not exist (or is another tenant's) is refused");
        (await Should.ThrowAsync<BusinessException>(() => Review.ResolveAsync(open.IntakeId,
            new OrderIntakeResolveDto { Picks = new Dictionary<string, Guid> { ["lines[7].transportUnit"] = f.TransportUnitId } }))).Code.ShouldBe(OrdersErrorCodes.IntakePickInvalid);

        (await Review.GetAsync(open.IntakeId)).Status.ShouldBe(OrderIntakeStatus.NeedsReview);
        (await AliasesAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_corrected_value_is_kept_as_the_reviewed_payload_and_marks_the_intake_corrected()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();
        var open = await Intake.SubmitAsync(Submission(Payload(f, p => p.Lines[0].TransportUnitName = "Gitterbox")));

        var reviewed = Payload(f);
        var resolved = await Review.ResolveAsync(open.IntakeId, new OrderIntakeResolveDto { ReviewedPayload = reviewed });

        resolved.Corrected.ShouldBeTrue();
        resolved.ReviewedPayload!.Lines[0].TransportUnitName.ShouldBe("Europallet");
        (await Intake.GetReviewedPayloadAsync(open.IntakeId))!.Lines[0].TransportUnitName.ShouldBe("Europallet");
    }

    [Fact]
    public async Task A_rejected_intake_is_closed()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();
        var open = await Intake.SubmitAsync(Submission(Payload(f, p => p.Customer = new OrderIntakeCustomerDto { Name = "Onbekend" })));

        var rejected = await Review.RejectAsync(open.IntakeId, new OrderIntakeRejectDto { Reason = "Geen order" });

        rejected.Status.ShouldBe(OrderIntakeStatus.Rejected);
        rejected.RejectReason.ShouldBe("Geen order");
        await Should.ThrowAsync<BusinessException>(() => Review.ResolveAsync(open.IntakeId, new OrderIntakeResolveDto { Picks = new Dictionary<string, Guid> { ["order.customer"] = f.CustomerId } }));
        await Should.ThrowAsync<BusinessException>(() => Review.RejectAsync(open.IntakeId, new OrderIntakeRejectDto { Reason = "nogmaals" }));
    }

    [Fact]
    public async Task An_unedited_confirmation_learns_the_matched_fields_and_the_next_intake_may_auto_confirm()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();
        var byName = Payload(f, p => p.Customer = new OrderIntakeCustomerDto { Name = "Klant Foods B.V." });
        var draft = await Intake.SubmitAsync(Submission(byName, trusted: true, autoConfirm: true));
        draft.Confirmed.ShouldBeFalse();

        await Orders.ConfirmAsync(draft.TransportOrderId!.Value);

        var alias = (await AliasesAsync()).ShouldHaveSingleItem("the Match on the customer became an alias");
        (alias.Kind, alias.TargetId).ShouldBe((IntakeAliasKind.Customer, f.CustomerId));
        (await Orders.GetAsync(draft.TransportOrderId!.Value)).EditedAfterIntake.ShouldBeFalse();

        (await Intake.SubmitAsync(Submission(byName, trusted: true, autoConfirm: true))).Confirmed.ShouldBeTrue("now resolved by Alias");
    }

    [Fact]
    public async Task An_edited_draft_is_marked_and_teaches_no_alias()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();
        var draft = await Intake.SubmitAsync(Submission(Payload(f, p => p.Customer = new OrderIntakeCustomerDto { Name = "Klant Foods B.V." })));
        var order = await Orders.GetAsync(draft.TransportOrderId!.Value);

        await Orders.UpdateAsync(order.Id, HeaderUpdate(order, u => u.CustomerReference = "PO-EDITED"));
        await Orders.ConfirmAsync(order.Id);

        (await Orders.GetAsync(order.Id)).EditedAfterIntake.ShouldBeTrue();
        (await AliasesAsync()).ShouldBeEmpty("build note 9: an edited Draft is a correction, not a confirmation");
    }

    /* ---------- Tenant isolation ---------- */

    [Fact]
    public async Task Tenant_B_reaches_none_of_tenant_As_intakes_aliases_or_master_data_matches()
    {
        using var features = IntakeOn();
        TransportRunWorld f;
        OrderIntakeSubmissionResultDto open;
        Guid aliasId;
        var document = Guid.NewGuid();
        using (InTenantA())
        {
            f = await FixtureAsync();
            var learned = await Intake.SubmitAsync(Submission(Payload(f, p => p.Customer = new OrderIntakeCustomerDto { Name = "Klant Foods Inkoop" })));
            await Review.ResolveAsync(learned.IntakeId, new OrderIntakeResolveDto { Picks = new Dictionary<string, Guid> { ["order.customer"] = f.CustomerId } });
            aliasId = (await AliasesAsync()).Single().Id;
            open = await Intake.SubmitAsync(Submission(Payload(f, p => p.Customer = new OrderIntakeCustomerDto { Name = "Onbekend" }), document: document));
        }

        using (InTenantB())
        {
            (await Review.GetListAsync(new OrderIntakeListRequestDto())).TotalCount.ShouldBe(0);
            (await Review.GetListAsync(new OrderIntakeListRequestDto { WildcardSearch = "order" })).Rows.ShouldBeEmpty();
            await Should.ThrowAsync<EntityNotFoundException>(() => Review.GetAsync(open.IntakeId));
            await Should.ThrowAsync<EntityNotFoundException>(() => Review.RejectAsync(open.IntakeId, new OrderIntakeRejectDto { Reason = "x" }));
            await Should.ThrowAsync<EntityNotFoundException>(() => Review.ResolveAsync(open.IntakeId, new OrderIntakeResolveDto()));
            (await Review.GetAliasesAsync(new IntakeAliasListRequestDto())).TotalCount.ShouldBe(0);
            await Should.ThrowAsync<EntityNotFoundException>(() => Review.DeleteAliasesAsync(aliasId));
            (await Intake.GetReviewedPayloadAsync(open.IntakeId)).ShouldBeNull();

            /* A's alias does not resolve B's intake; the same (document, index) in B is B's own intake. */
            var inB = await Intake.SubmitAsync(Submission(Payload(f, p => p.Customer = new OrderIntakeCustomerDto { Name = "Klant Foods Inkoop" }), document: document));
            inB.IntakeId.ShouldNotBe(open.IntakeId);
            inB.Status.ShouldBe(OrderIntakeStatus.NeedsReview, "B has none of A's relations, addresses, units or aliases");

            var match = GetRequiredService<IMasterDataMatchQuery>();
            (await match.FindRelationsAsync(new RelationMatchQuery { Code = "KL" + f.Suffix, Name = "Klant Foods B.V." })).ShouldBeEmpty();
            (await match.FindRelationAddressesAsync(new AddressMatchQuery { PostalCode = "5928 PR", HouseNumber = "12", Name = "DC Venlo", City = "Venlo" })).ShouldBeEmpty();
            (await match.FindReferencesByNameAsync(new ReferenceNameMatchQuery { Kind = MasterDataReferenceKind.TransportUnit, Names = ["Europallet"] })).ShouldBeEmpty();
            (await match.GetReferenceNamesAsync(MasterDataReferenceKind.TransportUnit)).ShouldNotContain("Europallet");
        }

        using (InTenantA())
        {
            (await Review.GetAsync(open.IntakeId)).Status.ShouldBe(OrderIntakeStatus.NeedsReview);
            (await AliasesAsync()).ShouldHaveSingleItem().Id.ShouldBe(aliasId);
            var match = GetRequiredService<IMasterDataMatchQuery>();
            (await match.FindRelationsAsync(new RelationMatchQuery { Code = "KL" + f.Suffix })).ShouldHaveSingleItem().Id.ShouldBe(f.CustomerId);
        }
    }
}
