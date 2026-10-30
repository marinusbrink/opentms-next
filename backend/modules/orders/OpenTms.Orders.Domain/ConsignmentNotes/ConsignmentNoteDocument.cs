using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Orders.ConsignmentNotes;

/// <summary>
/// A printed consignment note (design 58 "CMR"): its number, the order and lines it covers, the trip if printed from
/// one, the profile and report definition that rendered it, the stored blob and the data snapshot the template got.
/// Immutable after the print (OpenTMS had no numbered CMR, routing-co2-cmr.md scenario 34); a reprint is a new row.
/// </summary>
public class ConsignmentNoteDocument : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public string Number { get; private set; } = default!;

    public Guid OrderId { get; private set; }

    public Guid? TripId { get; private set; }

    public Guid ProfileId { get; private set; }

    public Guid ReportDefinitionId { get; private set; }

    public DateTime PrintedAt { get; private set; }

    public Guid? PrintedByUserId { get; private set; }

    public string BlobName { get; private set; } = default!;

    public string ContentType { get; private set; } = default!;

    public string FileName { get; private set; } = default!;

    public long SizeBytes { get; private set; }

    public string Sha256 { get; private set; } = default!;

    /// <summary>The catalogue payload the template rendered, as JSON (jsonb).</summary>
    public string DataSnapshot { get; private set; } = default!;

    public virtual ICollection<ConsignmentNoteDocumentLine> Lines { get; private set; } = new List<ConsignmentNoteDocumentLine>();

    protected ConsignmentNoteDocument()
    {
    }

    public ConsignmentNoteDocument(
        Guid id, Guid? tenantId, string number, Guid orderId, IEnumerable<Guid> lineIds, Guid? tripId, Guid profileId, Guid reportDefinitionId,
        DateTime printedAt, Guid? printedByUserId, string blobName, string contentType, string fileName, long sizeBytes, string sha256, string dataSnapshot)
        : base(id)
    {
        TenantId = tenantId;
        Number = Check.NotNullOrWhiteSpace(number, nameof(number), ConsignmentNoteConsts.MaxNumberLength);
        OrderId = orderId;
        TripId = tripId;
        ProfileId = profileId;
        ReportDefinitionId = reportDefinitionId;
        PrintedAt = printedAt;
        PrintedByUserId = printedByUserId;
        BlobName = Check.NotNullOrWhiteSpace(blobName, nameof(blobName), ConsignmentNoteConsts.MaxBlobNameLength);
        ContentType = Check.NotNullOrWhiteSpace(contentType, nameof(contentType), ConsignmentNoteConsts.MaxContentTypeLength);
        FileName = Check.NotNullOrWhiteSpace(fileName, nameof(fileName), ReferenceDataConsts.MaxDescriptionLength);
        SizeBytes = sizeBytes;
        Sha256 = Check.NotNullOrWhiteSpace(sha256, nameof(sha256), ConsignmentNoteConsts.Sha256Length);
        DataSnapshot = Check.NotNullOrWhiteSpace(dataSnapshot, nameof(dataSnapshot));
        foreach (var lineId in lineIds.Distinct())
        {
            Lines.Add(new ConsignmentNoteDocumentLine(tenantId, id, lineId));
        }
    }
}

/// <summary>A line the document covers; key (document, line).</summary>
public class ConsignmentNoteDocumentLine : Entity, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid DocumentId { get; private set; }

    public Guid LineId { get; private set; }

    protected ConsignmentNoteDocumentLine()
    {
    }

    public ConsignmentNoteDocumentLine(Guid? tenantId, Guid documentId, Guid lineId)
    {
        TenantId = tenantId;
        DocumentId = documentId;
        LineId = lineId;
    }

    public override object[] GetKeys() => [DocumentId, LineId];
}
