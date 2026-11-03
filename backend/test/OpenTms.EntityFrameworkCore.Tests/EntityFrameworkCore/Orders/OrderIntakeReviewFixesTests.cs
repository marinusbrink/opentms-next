using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Features;
using OpenTms.MasterData.Relations;
using OpenTms.Orders.Intake;
using OpenTms.Orders.Published;
using OpenTms.Orders.TransportOrders;
using OpenTms.TransportRun;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Orders;

using OrdersErrorCodes = OpenTms.Orders.OrdersErrorCodes;

/// <summary>
/// Orders' fixes of the design 72 review on SQLite with the master data of <see cref="TransportRunFixture"/>:
/// <list type="bullet">
/// <item>B2: a channel's fixed customer that is archived or gone leaves the customer Unresolved without candidates; the
/// payload's customer (id, code, name) is never consulted and nothing is confirmed automatically; a clerk's pick still
/// completes it.</item>
/// <item>M1 / build note 20: a business refusal while creating the order keeps the intake NeedsReview with the pseudo-field
/// <c>order</c> (Unresolved, the error code), writes no order, answers <c>order</c> among the unresolved paths, is
/// idempotent, and a clerk's correction then creates the order.</item>
/// <item>M2: the review list is projected (no jsonb column read) and its search matches % and _ literally.</item>
/// </list>
/// Risk class: High (order chain), Critical for the fixed customer (an API client's orders must stay its customer's).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class OrderIntakeReviewFixesTests : TransportOrderTestBase<OpenTmsEntityFrameworkCoreTestModule>
{
    private IOrderIntakeService Intake => GetRequiredService<IOrderIntakeService>();

    private IOrderIntakeAppService Review => GetRequiredService<IOrderIntakeAppService>();

    private IRepository<TransportOrder, Guid> OrderRows => GetRequiredService<IRepository<TransportOrder, Guid>>();

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

    private static OrderIntakeSubmissionDto Submission(OrderIntakeDto payload, bool trusted = true, bool autoConfirm = true, Guid? fixedCustomer = null,
        Guid? document = null, string subject = "Order", string? sender = null, string? fileName = "order.json") => new()
    {
        SourceDocumentId = document ?? Guid.NewGuid(),
        IntakeIndex = 0,
        Channel = "Api",
        SourceKind = OrderSource.Api,
        SenderAddress = sender,
        Subject = subject,
        FileName = fileName,
        Payload = payload,
        MappingConfidence = 1m,
        ProfileTrusted = trusted,
        AutoConfirm = autoConfirm,
        FixedCustomerRelationId = fixedCustomer
    };

    private async Task<OrderIntakeFieldResolutionDto> FieldAsync(Guid intakeId, string path)
        => (await Review.GetAsync(intakeId)).Resolution.Single(r => r.FieldPath == path);

    private Task<int> OrdersFromAsync(Guid intakeId) => WithUnitOfWorkAsync(() => OrderRows.CountAsync(o => o.SourceIntakeId == intakeId));

    private Task ArchiveAsync(Guid relationId) => WithUnitOfWorkAsync(async () =>
    {
        var relations = GetRequiredService<IRepository<Relation, Guid>>();
        var relation = await relations.GetAsync(relationId);
        relation.Archive();
        await relations.UpdateAsync(relation, autoSave: true);
    });

    /* ---------- B2: the fixed customer is never bypassed ---------- */

    [Fact]
    public async Task An_archived_fixed_customer_leaves_the_customer_unresolved_and_the_payloads_customer_is_never_used()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();
        await ArchiveAsync(f.CustomerId);

        /* The payload names another perfectly valid relation by id and code: it must not be taken. */
        var result = await Intake.SubmitAsync(Submission(Payload(f, p => p.Customer = new OrderIntakeCustomerDto { Code = "SH" + f.Suffix, RelationId = f.ShopId }),
            fixedCustomer: f.CustomerId));

        result.Status.ShouldBe(OrderIntakeStatus.NeedsReview);
        result.Confirmed.ShouldBeFalse();
        result.TransportOrderId.ShouldBeNull();
        result.Unresolved.ShouldContain(IntakeFieldPaths.Customer);
        var customer = await FieldAsync(result.IntakeId, IntakeFieldPaths.Customer);
        customer.Status.ShouldBe(IntakeResolutionStatus.Unresolved);
        customer.TargetId.ShouldBeNull("never the payload's relation");
        customer.Candidates.ShouldBeEmpty("no candidates from the payload's texts either");
        (await OrdersFromAsync(result.IntakeId)).ShouldBe(0);
    }

    [Fact]
    public async Task A_fixed_customer_that_no_longer_exists_is_unresolved_too()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();

        var result = await Intake.SubmitAsync(Submission(Payload(f), fixedCustomer: Guid.NewGuid()));

        result.Status.ShouldBe(OrderIntakeStatus.NeedsReview);
        var customer = await FieldAsync(result.IntakeId, IntakeFieldPaths.Customer);
        (customer.Status, customer.TargetId).ShouldBe((IntakeResolutionStatus.Unresolved, (Guid?)null));
        customer.Candidates.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_clerks_pick_completes_an_intake_whose_fixed_customer_was_archived()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();
        await ArchiveAsync(f.CustomerId);
        var result = await Intake.SubmitAsync(Submission(Payload(f), fixedCustomer: f.CustomerId));

        result.Unresolved.ShouldBe([IntakeFieldPaths.Customer], "only the customer is open");
        OrderIntakeDetailDto detail;
        try
        {
            detail = await Review.ResolveAsync(result.IntakeId, new OrderIntakeResolveDto { Picks = new Dictionary<string, Guid> { [IntakeFieldPaths.Customer] = f.ShopId } });
        }
        catch (BusinessException e)
        {
            throw new ShouldAssertException($"The clerk's pick of the customer was refused: {e.Code} {string.Join(", ", e.Data.Keys.Cast<object>().Select(k => k + "=" + e.Data[k]))}", e);
        }

        detail.Status.ShouldBe(OrderIntakeStatus.OrderCreated);
        (await Orders.GetAsync(detail.TransportOrderId!.Value)).CustomerRelationId.ShouldBe(f.ShopId);
    }

    /* ---------- M1: a refused order creation keeps the intake (build note 20) ---------- */

    /// <summary>A stop window with a start and no end: complete for the resolver, refused by the order (StopWindowAmbiguous).</summary>
    private OrderIntakeDto RefusedPayload(TransportRunWorld f) => Payload(f, p => p.Lines[0].Stops[0].TimeFrom = "08:00");

    [Fact]
    public async Task A_business_refusal_while_creating_the_order_keeps_the_intake_in_review_with_the_order_pseudo_field()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();
        var document = Guid.NewGuid();
        var ordersBefore = await WithUnitOfWorkAsync(() => OrderRows.CountAsync());

        var result = await Intake.SubmitAsync(Submission(RefusedPayload(f), document: document));

        result.Status.ShouldBe(OrderIntakeStatus.NeedsReview);
        result.TransportOrderId.ShouldBeNull();
        result.Confirmed.ShouldBeFalse();
        result.Unresolved.ShouldBe([IntakeFieldPaths.Order], "the caller's unresolved list names the refusal and nothing else");
        (await WithUnitOfWorkAsync(() => OrderRows.CountAsync())).ShouldBe(ordersBefore, "nothing of the order is written");

        var order = await FieldAsync(result.IntakeId, IntakeFieldPaths.Order);
        order.Status.ShouldBe(IntakeResolutionStatus.Unresolved);
        order.ErrorCode.ShouldBe(OrdersErrorCodes.StopWindowAmbiguous);
        (await Review.GetAsync(result.IntakeId)).Resolution.Where(r => r.FieldPath != IntakeFieldPaths.Order).ShouldAllBe(r => r.ErrorCode == null);

        var row = (await Review.GetListAsync(new OrderIntakeListRequestDto { Status = [OrderIntakeStatus.NeedsReview] })).Rows.Single(r => r.Id == result.IntakeId);
        row.OpenFieldCount.ShouldBe(1);

        /* The same submission again (a retried job): the same intake, still no order. */
        var again = await Intake.SubmitAsync(Submission(RefusedPayload(f), document: document));
        again.IntakeId.ShouldBe(result.IntakeId);
        again.Unresolved.ShouldBe([IntakeFieldPaths.Order]);
        (await WithUnitOfWorkAsync(() => OrderRows.CountAsync())).ShouldBe(ordersBefore);
    }

    [Fact]
    public async Task A_refused_intake_is_created_once_the_clerk_corrects_the_values_and_refused_again_without()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();
        var result = await Intake.SubmitAsync(Submission(RefusedPayload(f)));

        (await Should.ThrowAsync<BusinessException>(() => Review.ResolveAsync(result.IntakeId, new OrderIntakeResolveDto())))
            .Code.ShouldBe(OrdersErrorCodes.StopWindowAmbiguous, "the same values are refused the same way");
        (await Review.GetAsync(result.IntakeId)).Status.ShouldBe(OrderIntakeStatus.NeedsReview);

        var corrected = RefusedPayload(f);
        corrected.Lines[0].Stops[0].TimeTill = "12:00";
        var detail = await Review.ResolveAsync(result.IntakeId, new OrderIntakeResolveDto { ReviewedPayload = corrected });

        detail.Status.ShouldBe(OrderIntakeStatus.OrderCreated);
        detail.Corrected.ShouldBeTrue();
        detail.Resolution.ShouldNotContain(r => r.FieldPath == IntakeFieldPaths.Order, "the refusal is gone once the order exists");
        (await OrdersFromAsync(result.IntakeId)).ShouldBe(1);
    }

    /* ---------- M2: the review list ---------- */

    [Fact]
    public async Task The_review_list_reads_no_jsonb_column()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();
        await Intake.SubmitAsync(Submission(Payload(f, p => p.Customer = new OrderIntakeCustomerDto { Name = "Onbekend" }), subject: "Projection check"));

        using var capture = EfCommandCapture.Begin();
        var list = await Review.GetListAsync(new OrderIntakeListRequestDto { WildcardSearch = "projection" });

        list.Rows.ShouldHaveSingleItem().Subject.ShouldBe("Projection check");
        var reads = capture.On("OrderIntakes");
        reads.ShouldNotBeEmpty("control: the list query was captured");
        foreach (var sql in reads)
        {
            sql.ShouldNotContain("\"Payload\"");
            sql.ShouldNotContain("\"Resolution\"");
            sql.ShouldNotContain("\"ReviewedPayload\"");
        }
    }

    [Fact]
    public async Task The_review_lists_search_matches_percent_and_underscore_literally_and_ignores_case()
    {
        using var features = IntakeOn();
        using var tenant = InTenantA();
        var f = await FixtureAsync();
        var open = (Action<OrderIntakeDto>)(p => p.Customer = new OrderIntakeCustomerDto { Name = "Onbekend" });
        await Intake.SubmitAsync(Submission(Payload(f, open), subject: "50% korting week 45"));
        await Intake.SubmitAsync(Submission(Payload(f, open), subject: "Order A_B Venlo"));
        await Intake.SubmitAsync(Submission(Payload(f, open), subject: "Order AxB Venlo", fileName: "plain.csv"));

        async Task<List<string?>> SearchAsync(string term)
            => (await Review.GetListAsync(new OrderIntakeListRequestDto { WildcardSearch = term })).Rows.Select(r => r.Subject).ToList();

        (await SearchAsync("%")).ShouldBe(["50% korting week 45"]);
        (await SearchAsync("a_b")).ShouldBe(["Order A_B Venlo"], "_ is not a wildcard");
        (await SearchAsync("VENLO")).Count.ShouldBe(2);
        (await SearchAsync("plain.CSV")).ShouldBe(["Order AxB Venlo"], "the file name is searched too");
    }

    [Fact]
    public async Task Tenant_B_finds_none_of_tenant_As_intakes_by_search()
    {
        using var features = IntakeOn();
        using (InTenantA())
        {
            var f = await FixtureAsync();
            await Intake.SubmitAsync(Submission(Payload(f, p => p.Customer = new OrderIntakeCustomerDto { Name = "Onbekend" }), subject: "Secret subject of A"));
        }

        using (InTenantB())
        {
            (await Review.GetListAsync(new OrderIntakeListRequestDto { WildcardSearch = "secret" })).Rows.ShouldBeEmpty();
            (await Review.GetListAsync(new OrderIntakeListRequestDto())).TotalCount.ShouldBe(0);
        }
    }
}
