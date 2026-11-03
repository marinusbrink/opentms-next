using Microsoft.EntityFrameworkCore;
using OpenTms.Orders.Intake;
using OpenTms.Orders.TransportOrders;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace OpenTms.Orders.EntityFrameworkCore;

public partial class OrdersDbContext
{
    /* Design 72 "Migration strategy": OrderIntakes and IntakeAliases, additive only. */
    private static void ConfigureOrderIntake(ModelBuilder builder)
    {
        builder.Entity<OrderIntake>(b =>
        {
            b.ToTable("OrderIntakes", OrdersDbProperties.DbSchema, t =>
            {
                t.HasCheckConstraint("CK_OrderIntakes_Status", "\"Status\" BETWEEN 1 AND 3");
                t.HasCheckConstraint("CK_OrderIntakes_IntakeIndex", "\"IntakeIndex\" >= 0");
            });
            b.ConfigureByConvention();
            b.Property(i => i.Channel).HasMaxLength(IntakeConsts.MaxChannelLength).IsRequired();
            b.Property(i => i.SenderAddress).HasMaxLength(IntakeConsts.MaxSenderAddressLength);
            b.Property(i => i.Subject).HasMaxLength(IntakeConsts.MaxSubjectLength);
            b.Property(i => i.FileName).HasMaxLength(IntakeConsts.MaxFileNameLength);
            b.Property(i => i.Payload).HasColumnType("jsonb").IsRequired();
            b.Property(i => i.ReviewedPayload).HasColumnType("jsonb");
            b.Property(i => i.Resolution).HasJsonbResolution();
            b.Property(i => i.MappingConfidence).HasPrecision(5, 4);
            b.Property(i => i.NameSimilarityThreshold).HasPrecision(3, 2);
            b.Property(i => i.RejectReason).HasMaxLength(IntakeConsts.MaxRejectReasonLength);
            b.Property(i => i.CustomerReference).HasMaxLength(OrdersConsts.MaxCustomerReferenceLength);
            b.Property(i => i.CustomerName).HasMaxLength(IntakeConsts.MaxNameLength);
            b.HasIndex(i => i.TenantId);
            /* The natural key: the same document and index again returns the stored intake (constitution rule 3). */
            b.HasIndex(i => new { i.TenantId, i.SourceDocumentId, i.IntakeIndex }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
            /* The review queue: by status, newest first. */
            b.HasIndex(i => new { i.TenantId, i.Status, i.CreationTime }).IsDescending(false, false, true);
            /* The list's search over sender, subject, file name and customer reference (2,000 intakes a day per tenant). */
            b.HasIndex(i => i.SenderAddress, "IX_OrderIntakes_SenderAddress_Trigram").HasMethod("gin").HasOperators("gin_trgm_ops");
            b.HasIndex(i => i.Subject, "IX_OrderIntakes_Subject_Trigram").HasMethod("gin").HasOperators("gin_trgm_ops");
            b.HasIndex(i => i.FileName, "IX_OrderIntakes_FileName_Trigram").HasMethod("gin").HasOperators("gin_trgm_ops");
            b.HasIndex(i => i.CustomerReference, "IX_OrderIntakes_CustomerReference_Trigram").HasMethod("gin").HasOperators("gin_trgm_ops");
        });

        builder.Entity<IntakeAlias>(b =>
        {
            b.ToTable("IntakeAliases", OrdersDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(a => a.NormalizedText).HasMaxLength(IntakeConsts.MaxAliasTextLength).IsRequired();
            b.HasIndex(a => a.TenantId);
            /* One alias per tenant, kind, scope and text; a tenant-wide alias (scope null) is unique too. */
            b.HasIndex(a => new { a.TenantId, a.Kind, a.ScopeRelationId, a.NormalizedText }).IsUnique().AreNullsDistinct(false);
        });
    }
}
