using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTms.Financial.Invoicing;
using OpenTms.Platform.Localization;
using OpenTms.Platform.NumberSequences;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace OpenTms.Financial.Seed;

/// <summary>
/// Seeds the Dutch VAT categories and the outbound and inbound invoice types per tenant database (design 52, Migration
/// strategy), run by the DbMigrator and the provisioning job through ABP's data seeder. Idempotent on the code: an
/// existing or soft-deleted row, and the rates of an existing category, are never touched; a seeded row becomes the
/// default only when its scope has none. Skips the host (host-level master data is parked, assessment §4). Logs tenant
/// id and counts only.
/// </summary>
public class FinancialDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private static readonly DateOnly Since2001 = new(2001, 1, 1);

    private readonly IRepository<VatPercentage, Guid> _vatPercentages;
    private readonly IRepository<OutboundInvoiceType, Guid> _outboundTypes;
    private readonly IRepository<InboundInvoiceType, Guid> _inboundTypes;
    private readonly ICurrentTenant _currentTenant;
    private readonly IDataFilter _dataFilter;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ILogger<FinancialDataSeedContributor> _logger;

    public FinancialDataSeedContributor(
        IRepository<VatPercentage, Guid> vatPercentages,
        IRepository<OutboundInvoiceType, Guid> outboundTypes,
        IRepository<InboundInvoiceType, Guid> inboundTypes,
        ICurrentTenant currentTenant,
        IDataFilter dataFilter,
        IGuidGenerator guidGenerator,
        ILogger<FinancialDataSeedContributor> logger)
    {
        _vatPercentages = vatPercentages;
        _outboundTypes = outboundTypes;
        _inboundTypes = inboundTypes;
        _currentTenant = currentTenant;
        _dataFilter = dataFilter;
        _guidGenerator = guidGenerator;
        _logger = logger;
    }

    private static LocalizedName Name(string nl, string en)
        => LocalizedName.Create(new Dictionary<string, string?> { ["en"] = en, ["nl"] = nl });

    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        if (context.TenantId == null)
        {
            return;
        }

        using (_currentTenant.Change(context.TenantId))
        {
            var vat = await SeedVatPercentagesAsync(context.TenantId.Value);
            var outbound = await SeedOutboundInvoiceTypesAsync(context.TenantId.Value);
            var inbound = await SeedInboundInvoiceTypesAsync(context.TenantId.Value);
            if (vat + outbound + inbound > 0)
            {
                _logger.LogInformation(
                    "Financial seed for tenant {TenantId}: {Vat} VAT percentages, {Outbound} outbound and {Inbound} inbound invoice types inserted.",
                    context.TenantId, vat, outbound, inbound);
            }
        }
    }

    private async Task<int> SeedVatPercentagesAsync(Guid tenantId)
    {
        List<VatPercentage> existing;
        using (_dataFilter.Disable<ISoftDelete>())
        {
            existing = await _vatPercentages.GetListAsync();
        }

        var rows = new (string Code, LocalizedName Name, VatKind Kind, (DateOnly, decimal)[] Rates)[]
        {
            ("HIGH", Name("Hoog", "High"), VatKind.Standard, [(Since2001, 19m), (new DateOnly(2012, 10, 1), 21m)]),
            ("LOW", Name("Laag", "Low"), VatKind.Reduced, [(Since2001, 6m), (new DateOnly(2019, 1, 1), 9m)]),
            ("ZERO", Name("Nul", "Zero"), VatKind.Zero, [(Since2001, 0m)]),
            ("EXEMPT", Name("Vrijgesteld", "Exempt"), VatKind.Exempt, [(Since2001, 0m)]),
            ("REVERSE", Name("Verlegd", "Reverse charge"), VatKind.ReverseCharge, [(Since2001, 0m)]),
            ("ICP", Name("Intracommunautair", "Intra-community"), VatKind.IntraCommunity, [(Since2001, 0m)]),
            ("EXPORT", Name("Export buiten EU", "Export outside the EU"), VatKind.Export, [(Since2001, 0m)]),
        };

        var hasDefault = existing.Any(v => v.IsDefault && !v.IsDeleted);
        var inserted = 0;
        foreach (var row in rows.Where(r => existing.All(v => v.Code != r.Code)))
        {
            var category = new VatPercentage(_guidGenerator.Create(), tenantId, row.Code, row.Name, row.Kind, "NL");
            category.SetRates(row.Rates, fixedOn: null, _guidGenerator.Create);
            if (row.Code == "HIGH" && !hasDefault)
            {
                category.SetDefault(true);
            }

            await _vatPercentages.InsertAsync(category);
            inserted++;
        }

        return inserted;
    }

    private async Task<int> SeedOutboundInvoiceTypesAsync(Guid tenantId)
    {
        List<OutboundInvoiceType> existing;
        using (_dataFilter.Disable<ISoftDelete>())
        {
            existing = await _outboundTypes.GetListAsync();
        }

        var rows = new (string Code, LocalizedName Name, OutboundInvoiceKind Kind, string Prefix)[]
        {
            ("INV", Name("Factuur", "Invoice"), OutboundInvoiceKind.Invoice, "F"),
            ("CRN", Name("Creditnota", "Credit note"), OutboundInvoiceKind.CreditNote, "C"),
        };

        var inserted = 0;
        foreach (var row in rows.Where(r => existing.All(t => t.Code != r.Code)))
        {
            var type = new OutboundInvoiceType(
                _guidGenerator.Create(), tenantId, row.Code, row.Name, row.Kind,
                NumberSequenceFormat.Create(row.Prefix, includeYear: true, includeMonth: false, separator: "-", digits: 5));
            if (!existing.Any(t => t.Kind == row.Kind && t.IsDefault && !t.IsDeleted))
            {
                type.SetDefault(true);
            }

            await _outboundTypes.InsertAsync(type);
            inserted++;
        }

        return inserted;
    }

    private async Task<int> SeedInboundInvoiceTypesAsync(Guid tenantId)
    {
        List<InboundInvoiceType> existing;
        using (_dataFilter.Disable<ISoftDelete>())
        {
            existing = await _inboundTypes.GetListAsync();
        }

        var rows = new (string Code, string Name, InboundInvoiceKind Kind)[]
        {
            ("CARRIER", "Inkoopfactuur charter", InboundInvoiceKind.CarrierInvoice),
            ("OTHER", "Overige inkoop", InboundInvoiceKind.Other),
        };

        var inserted = 0;
        foreach (var row in rows.Where(r => existing.All(t => t.Code != r.Code)))
        {
            await _inboundTypes.InsertAsync(new InboundInvoiceType(_guidGenerator.Create(), tenantId, row.Code, row.Name, row.Kind));
            inserted++;
        }

        return inserted;
    }
}
