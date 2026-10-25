using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Financial.Invoicing;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Financial;

/// <summary>
/// The outbound and inbound invoice types on SQLite (design 52). Risk class: Critical for the one default per kind and
/// isolation; High for numbering (design 50), CRUD and the default refusals.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class InvoiceTypeAppServiceTests : FinancialAppServiceTestBase
{
    private readonly IOutboundInvoiceTypeAppService _outbound;
    private readonly IInboundInvoiceTypeAppService _inbound;

    public InvoiceTypeAppServiceTests()
    {
        _outbound = GetRequiredService<IOutboundInvoiceTypeAppService>();
        _inbound = GetRequiredService<IInboundInvoiceTypeAppService>();
    }

    private Task<OutboundInvoiceTypeDto> CreateAsync(string code, OutboundInvoiceKind kind, string prefix = "F")
        => _outbound.CreateAsync(new OutboundInvoiceTypeCreateDto { Code = code, Names = Names(code), Kind = kind, Numbering = Numbering(prefix, year: true) });

    private static OutboundInvoiceTypeUpdateDto UpdateFrom(OutboundInvoiceTypeDto dto) => new()
    {
        Code = dto.Code,
        Names = Copy(dto.Names),
        Kind = dto.Kind,
        Numbering = dto.Numbering,
        IsArchived = dto.IsArchived,
        ConcurrencyStamp = dto.ConcurrencyStamp
    };

    [Fact]
    public async Task The_first_type_of_each_kind_becomes_its_default()
    {
        using var _ = InTenantA();
        var invoice = await CreateAsync("INV", OutboundInvoiceKind.Invoice);
        var credit = await CreateAsync("CRN", OutboundInvoiceKind.CreditNote, "C");
        var second = await CreateAsync("INV2", OutboundInvoiceKind.Invoice);

        invoice.IsDefault.ShouldBeTrue();
        credit.IsDefault.ShouldBeTrue("one default per kind (Assumption 5)");
        second.IsDefault.ShouldBeFalse();
        invoice.NextNumberPreview.ShouldBe($"F-{DateTime.Now.Year}-00001");

        var rows = (await _outbound.GetListAsync(List())).Rows;
        rows.Single(r => r.Code == "CRN").NumberingExample.ShouldBe($"C-{DateTime.Now.Year}-00001");
    }

    [Fact]
    public async Task Set_default_moves_only_the_default_of_the_same_kind()
    {
        using var _ = InTenantA();
        var invoice = await CreateAsync("INV", OutboundInvoiceKind.Invoice);
        var credit = await CreateAsync("CRN", OutboundInvoiceKind.CreditNote, "C");
        var second = await CreateAsync("INV2", OutboundInvoiceKind.Invoice);

        await _outbound.SetDefaultAsync(second.Id);

        (await _outbound.GetAsync(second.Id)).IsDefault.ShouldBeTrue();
        (await _outbound.GetAsync(invoice.Id)).IsDefault.ShouldBeFalse();
        (await _outbound.GetAsync(credit.Id)).IsDefault.ShouldBeTrue("the credit note default is another kind's");
    }

    [Fact]
    public async Task A_default_cannot_be_deleted_archived_or_change_kind()
    {
        using var _ = InTenantA();
        var invoice = await CreateAsync("INV", OutboundInvoiceKind.Invoice);
        var second = await CreateAsync("INV2", OutboundInvoiceKind.Invoice);

        (await Should.ThrowAsync<BusinessException>(() => _outbound.DeleteAsync(invoice.Id))).Code.ShouldBe(ReferenceDataErrorCodes.DefaultRequired);
        var archive = UpdateFrom(invoice);
        archive.IsArchived = true;
        (await Should.ThrowAsync<BusinessException>(() => _outbound.UpdateAsync(invoice.Id, archive))).Code.ShouldBe(ReferenceDataErrorCodes.DefaultRequired);
        var kind = UpdateFrom(invoice);
        kind.Kind = OutboundInvoiceKind.CreditNote;
        (await Should.ThrowAsync<BusinessException>(() => _outbound.UpdateAsync(invoice.Id, kind))).Code.ShouldBe(ReferenceDataErrorCodes.DefaultRequired);

        var bulk = await _outbound.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = Explicit(invoice.Id, second.Id) });
        bulk.DeletedCount.ShouldBe(1);
        bulk.SkippedRows.Single().Id.ShouldBe(invoice.Id);
    }

    [Fact]
    public async Task Next_number_continues_only_upwards()
    {
        using var _ = InTenantA();
        var invoice = await CreateAsync("INV", OutboundInvoiceKind.Invoice);

        var change = UpdateFrom(invoice);
        change.NextNumber = 4711;
        (await _outbound.UpdateAsync(invoice.Id, change)).NextNumberPreview.ShouldBe($"F-{DateTime.Now.Year}-04711");

        var lower = UpdateFrom(await _outbound.GetAsync(invoice.Id));
        lower.NextNumber = 12;
        (await Should.ThrowAsync<BusinessException>(() => _outbound.UpdateAsync(invoice.Id, lower))).Code.ShouldBe(ReferenceDataErrorCodes.NextNumberTooLow);
    }

    [Fact]
    public async Task Inbound_types_are_listed_updated_and_deleted()
    {
        using var _ = InTenantA();
        var carrier = await _inbound.CreateAsync(new InboundInvoiceTypeCreateDto { Code = "carrier", Name = "Carrier invoice", Kind = InboundInvoiceKind.CarrierInvoice });
        await _inbound.CreateAsync(new InboundInvoiceTypeCreateDto { Code = "OTHER", Name = "Other purchase", Kind = InboundInvoiceKind.Other, CostLedgerAccount = "7000" });

        carrier.Code.ShouldBe("CARRIER");
        (await _inbound.GetListAsync(List(sortBy: "kind", sortDir: "desc"))).Rows.First().Code.ShouldBe("OTHER");
        (await Should.ThrowAsync<BusinessException>(() => _inbound.CreateAsync(new InboundInvoiceTypeCreateDto { Code = "OTHER", Name = "x" }))).Code.ShouldBe(ReferenceDataErrorCodes.DuplicateKey);

        var updated = await _inbound.UpdateAsync(carrier.Id, new InboundInvoiceTypeUpdateDto
        {
            Code = "CARRIER", Name = "Charter", Kind = InboundInvoiceKind.CarrierInvoice, IsArchived = true, ConcurrencyStamp = carrier.ConcurrencyStamp
        });
        updated.IsArchived.ShouldBeTrue();
        (await _inbound.GetLookupAsync()).Select(l => l.Code).ShouldBe(["OTHER"]);

        await _inbound.DeleteAsync(carrier.Id);
        (await _inbound.GetListAsync(List(includeArchived: true))).TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task Another_tenant_sees_nothing()
    {
        using (InTenantA())
        {
            await CreateAsync("INV", OutboundInvoiceKind.Invoice);
            await _inbound.CreateAsync(new InboundInvoiceTypeCreateDto { Code = "CARRIER", Name = "Carrier" });
        }

        using (InTenantB())
        {
            (await _outbound.GetListAsync(List())).TotalCount.ShouldBe(0);
            (await _inbound.GetListAsync(List())).TotalCount.ShouldBe(0);
            (await CreateAsync("INV", OutboundInvoiceKind.Invoice)).IsDefault.ShouldBeTrue("tenant B's first invoice type is its own default");
        }
    }
}
