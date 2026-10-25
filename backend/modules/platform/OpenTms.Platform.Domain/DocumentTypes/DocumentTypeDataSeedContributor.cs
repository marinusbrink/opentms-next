using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTms.Platform.Localization;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace OpenTms.Platform.DocumentTypes;

/// <summary>
/// Seeds the two standard document types per tenant database (design 54, "Seed"), run through ABP's data seeder by the
/// DbMigrator and by the tenant-provisioning job. Upserts on the code and never overwrites a tenant's edits: a missing
/// code is inserted, an existing one is left as the tenant keeps it, a soft-deleted one stays deleted (the read of
/// existing codes disables the soft-delete filter only; the tenant filter stays on, constitution rule 1). The code never
/// matches on the name. Skips the host (host-level rows are parked). Logs tenant id and counts only.
/// </summary>
public class DocumentTypeDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    public const string FreightCode = "FREIGHT";
    public const string GeneralCode = "GENERAL";

    private readonly IRepository<DocumentType, Guid> _documentTypes;
    private readonly ICurrentTenant _currentTenant;
    private readonly IDataFilter _dataFilter;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ILogger<DocumentTypeDataSeedContributor> _logger;

    public DocumentTypeDataSeedContributor(
        IRepository<DocumentType, Guid> documentTypes,
        ICurrentTenant currentTenant,
        IDataFilter dataFilter,
        IGuidGenerator guidGenerator,
        ILogger<DocumentTypeDataSeedContributor> logger)
    {
        _documentTypes = documentTypes;
        _currentTenant = currentTenant;
        _dataFilter = dataFilter;
        _guidGenerator = guidGenerator;
        _logger = logger;
    }

    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        if (context.TenantId == null)
        {
            return;
        }

        using (_currentTenant.Change(context.TenantId))
        {
            List<string> existingCodes;
            using (_dataFilter.Disable<ISoftDelete>())
            {
                /* At most a hundred rows per tenant (design 54, expected volumes). */
                existingCodes = (await _documentTypes.GetListAsync(includeDetails: false)).Select(t => t.Code).ToList();
            }

            var toInsert = new List<DocumentType>();
            foreach (var (code, nl, en, isFreightDocument) in Standard)
            {
                if (existingCodes.Contains(code, StringComparer.Ordinal))
                {
                    continue;
                }

                var type = new DocumentType(_guidGenerator.Create(), context.TenantId, code, LocalizedName.Create([new("nl", nl), new("en", en)]));
                type.SetFlags(isFreightDocument, includeWithInvoice: isFreightDocument, showInListIndicator: isFreightDocument);
                toInsert.Add(type);
            }

            if (toInsert.Count > 0)
            {
                await _documentTypes.InsertManyAsync(toInsert);
            }

            _logger.LogInformation("Platform seed for tenant {TenantId}: {Inserted} document types inserted, {Existing} existing.", context.TenantId, toInsert.Count, existingCodes.Count);
        }
    }

    /// <summary>FREIGHT is a freight document, attached to the invoice and shown in the list indicator; GENERAL is none of these.</summary>
    private static readonly (string Code, string Nl, string En, bool IsFreightDocument)[] Standard =
    [
        (FreightCode, "Vrachtdocument", "Freight document", true),
        (GeneralCode, "Algemeen document", "General document", false)
    ];
}
