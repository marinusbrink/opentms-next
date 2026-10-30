using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenTms.Orders.ConsignmentNotes;
using OpenTms.Orders.TransportOrders;
using OpenTms.Platform.EntityFrameworkCore.Localization;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace OpenTms.Orders.EntityFrameworkCore;

/// <summary>
/// Design 58's tables (migration OrdersTransportOrders): the order with its children, the templates, the consignment
/// note profiles, fixed descriptions and documents, and the attribute definitions. Money is numeric(18,2), unit
/// quantities numeric(18,4), CO₂ numeric(18,3); instants timestamptz; attributes and small sets jsonb; every per-tenant
/// unique key NULLS NOT DISTINCT with the soft-delete filter; cross-module ids carry no constraint (design 56).
/// </summary>
public partial class OrdersDbContext
{
    private const int MoneyPrecision = 18;
    private const int MoneyScale = 2;
    private const int QuantityScale = 4;
    private const int Co2Scale = 3;

    private static void ConfigureTransportOrders(ModelBuilder builder)
    {
        builder.Entity<TransportOrder>(b =>
        {
            b.ToTable("TransportOrders", OrdersDbProperties.DbSchema, t =>
            {
                t.HasCheckConstraint("CK_TransportOrders_Status", "\"Status\" BETWEEN 1 AND 6");
                t.HasCheckConstraint("CK_TransportOrders_CopyIndex", "\"CopyIndex\" >= 0");
            });
            b.ConfigureByConvention();
            b.Property(o => o.Number).HasMaxLength(OrdersConsts.MaxNumberLength);
            b.Property(o => o.CustomerName).HasMaxLength(OrdersConsts.MaxStopNameLength).IsRequired();
            b.Property(o => o.CustomerReference).HasMaxLength(OrdersConsts.MaxCustomerReferenceLength);
            b.Property(o => o.InternalRemarks).HasMaxLength(OrdersConsts.MaxRemarksLength);
            b.Property(o => o.InvoicingRemarks).HasMaxLength(OrdersConsts.MaxRemarksLength);
            b.Property(o => o.Attributes).HasJsonbDictionary();
            b.Property(o => o.DraftConfidence).HasPrecision(5, 4);
            b.Property(o => o.RevenueAmount).HasPrecision(MoneyPrecision, MoneyScale);
            b.Property(o => o.CostOfRevenueAmount).HasPrecision(MoneyPrecision, MoneyScale);
            b.Property(o => o.MarginAmount).HasPrecision(MoneyPrecision, MoneyScale);
            b.Property(o => o.Co2Kg).HasPrecision(MoneyPrecision, Co2Scale);
            b.Property(o => o.FirstStopName).HasMaxLength(OrdersConsts.MaxStopNameLength);
            b.Property(o => o.FirstStopCity).HasMaxLength(OrdersConsts.MaxCityLength);
            b.Property(o => o.LastStopName).HasMaxLength(OrdersConsts.MaxStopNameLength);
            b.Property(o => o.LastStopCity).HasMaxLength(OrdersConsts.MaxCityLength);
            b.Property(o => o.TripNumbers).HasMaxLength(OrdersConsts.MaxTripNumbersLength);
            b.Ignore(o => o.IsEditable);

            ConfigureFinancialTerms(b.OwnsOne(o => o.FinancialTerms));
            b.Navigation(o => o.FinancialTerms).IsRequired();
            b.OwnsOne(o => o.TripRequest, t =>
            {
                t.Property(x => x.PlannedDeparture).HasUtcInstant();
                t.Property(x => x.PlannedArrival).HasUtcInstant();
                t.Property(x => x.VehicleIds).HasJsonbList();
                t.Property(x => x.DriverIds).HasJsonbList();
                t.Property(x => x.LineIds).HasNullableJsonbList();
                t.Property(x => x.AgreedPurchaseAmount).HasPrecision(MoneyPrecision, MoneyScale);
                t.Property(x => x.AgreedPurchaseCurrencyCode).HasMaxLength(OrdersConsts.MaxCurrencyCodeLength);
                t.Property(x => x.WarningCode).HasMaxLength(OrdersConsts.MaxTripRequestWarningLength);
                t.Property(x => x.WarningField).HasMaxLength(OrdersConsts.MaxTripRequestWarningLength);
                /* An instant, as the request's times: a DateTime here would make ABP configure the type as an entity of its own. */
                t.Property(x => x.WarningAt).HasUtcInstant();
            });

            b.HasMany(o => o.Lines).WithOne().HasForeignKey(l => l.OrderId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(o => o.DefaultStops).WithOne().HasForeignKey(s => s.OrderId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(o => o.References).WithOne().HasForeignKey(r => r.OrderId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(o => o.PriceLists).WithOne().HasForeignKey(p => p.OrderId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(o => o.DiscountsOrSurcharges).WithOne().HasForeignKey(d => d.OrderId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_TransportOrderDiscountsOrSurcharges_Order");
            b.HasMany(o => o.Remarks).WithOne().HasForeignKey(r => r.OrderId).OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(o => o.TenantId);
            b.HasIndex(o => new { o.TenantId, o.Number }).IsUnique().AreNullsDistinct(false).HasFilter("\"Number\" IS NOT NULL AND " + NotDeleted);
            b.HasIndex(o => new { o.TenantId, o.Status, o.OrderDate });
            b.HasIndex(o => new { o.TenantId, o.OrderDate });
            b.HasIndex(o => new { o.TenantId, o.CustomerRelationId });
            b.HasIndex(o => o.TransportOrderTypeId);
            /* Named short: the conventional name passes PostgreSQL's 63-character limit and would be truncated. */
            b.HasIndex(o => new { o.TenantId, o.SourceTemplateId, o.OrderDate, o.CopyIndex }, "IX_TransportOrders_TenantId_Template_Date_Copy").IsUnique().AreNullsDistinct(false).HasFilter("\"SourceTemplateId\" IS NOT NULL AND " + NotDeleted);
            /* The list's wildcard search (design 58): trigram indexes, PostgreSQL only (SQLite ignores the method). */
            b.HasIndex(o => o.Number, "IX_TransportOrders_Number_Trigram").HasMethod("gin").HasOperators("gin_trgm_ops");
            b.HasIndex(o => o.CustomerReference, "IX_TransportOrders_CustomerReference_Trigram").HasMethod("gin").HasOperators("gin_trgm_ops");
            b.HasIndex(o => o.CustomerName, "IX_TransportOrders_CustomerName_Trigram").HasMethod("gin").HasOperators("gin_trgm_ops");
            b.HasIndex(o => o.FirstStopName, "IX_TransportOrders_FirstStopName_Trigram").HasMethod("gin").HasOperators("gin_trgm_ops");
            b.HasIndex(o => o.LastStopName, "IX_TransportOrders_LastStopName_Trigram").HasMethod("gin").HasOperators("gin_trgm_ops");
        });

        builder.Entity<TransportOrderLine>(b =>
        {
            b.ToTable("TransportOrderLines", OrdersDbProperties.DbSchema, t => t.HasCheckConstraint("CK_TransportOrderLines_Position", "\"Position\" >= 1"));
            b.ConfigureByConvention();
            ConfigureFreight(b.OwnsOne(l => l.Freight));
            b.Navigation(l => l.Freight).IsRequired();
            b.Property(l => l.Attributes).HasJsonbDictionary();
            b.Property(l => l.Co2Kg).HasPrecision(MoneyPrecision, Co2Scale);
            /* Constraint names kept under PostgreSQL's 63 characters (the provider would truncate them with "~"). */
            b.HasMany(l => l.Measurements).WithOne().HasForeignKey(m => m.LineId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_TransportOrderLineMeasurements_Line");
            b.HasMany(l => l.RequiredTransportCharacteristics).WithOne().HasForeignKey(c => c.LineId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_TransportOrderLineCharacteristics_Line");
            b.HasMany(l => l.Stops).WithOne().HasForeignKey(s => s.LineId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_TransportOrderStops_Line");
            b.HasIndex(l => l.TenantId);
            b.HasIndex(l => new { l.OrderId, l.Position });
        });

        builder.Entity<TransportOrderLineMeasurement>(b =>
        {
            b.ToTable("TransportOrderLineMeasurements", OrdersDbProperties.DbSchema, t => t.HasCheckConstraint("CK_TransportOrderLineMeasurements_Position", "\"Position\" BETWEEN 1 AND 3"));
            b.ConfigureByConvention();
            b.HasKey(m => new { m.LineId, m.Kind, m.Position });
            b.HasIndex(m => m.TenantId);
        });

        builder.Entity<TransportOrderLineCharacteristic>(b =>
        {
            b.ToTable("TransportOrderLineCharacteristics", OrdersDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.HasKey(c => new { c.LineId, c.TransportCharacteristicId });
            b.HasIndex(c => c.TenantId);
        });

        builder.Entity<TransportOrderStop>(b =>
        {
            b.ToTable("TransportOrderStops", OrdersDbProperties.DbSchema, t => t.HasCheckConstraint("CK_TransportOrderStops_Position", "\"Position\" >= 1"));
            b.ConfigureByConvention();
            ConfigureStopDetails(b.OwnsOne(s => s.Details));
            b.Navigation(s => s.Details).IsRequired();
            b.HasIndex(s => s.TenantId);
            b.HasIndex(s => new { s.LineId, s.Position });
        });

        builder.Entity<TransportOrderDefaultStop>(b =>
        {
            b.ToTable("TransportOrderDefaultStops", OrdersDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.HasKey(s => new { s.OrderId, s.Position });
            ConfigureStopDetails(b.OwnsOne(s => s.Details));
            b.Navigation(s => s.Details).IsRequired();
            b.HasIndex(s => s.TenantId);
        });

        builder.Entity<TransportOrderReference>(b =>
        {
            b.ToTable("TransportOrderReferences", OrdersDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(r => r.Type).HasMaxLength(OrdersConsts.MaxReferenceTypeLength).IsRequired();
            b.Property(r => r.Value).HasMaxLength(OrdersConsts.MaxReferenceValueLength).IsRequired();
            b.Property(r => r.Description).HasMaxLength(OrdersConsts.MaxReferenceDescriptionLength);
            b.HasIndex(r => r.TenantId);
            b.HasIndex(r => r.OrderId);
        });

        builder.Entity<TransportOrderPriceList>(b =>
        {
            b.ToTable("TransportOrderPriceLists", OrdersDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.HasIndex(p => p.TenantId);
            b.HasIndex(p => new { p.OrderId, p.PriceListId }).IsUnique();
        });

        builder.Entity<TransportOrderDiscountOrSurcharge>(b =>
        {
            b.ToTable("TransportOrderDiscountsOrSurcharges", OrdersDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.HasIndex(d => d.TenantId);
            b.HasIndex(d => new { d.OrderId, d.DiscountOrSurchargeId }, "IX_TransportOrderDiscountsOrSurcharges_Order_Ds").IsUnique();
        });

        builder.Entity<TransportOrderRemark>(b =>
        {
            b.ToTable("TransportOrderRemarks", OrdersDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(r => r.Text).HasMaxLength(OrdersConsts.MaxRevisionReasonLength).IsRequired();
            b.HasIndex(r => r.TenantId);
            b.HasIndex(r => r.OrderId);
        });

        // ── templates ──────────────────────────────────────────────────────────

        builder.Entity<TransportOrderTemplate>(b =>
        {
            b.ToTable("TransportOrderTemplates", OrdersDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(t => t.Name).HasMaxLength(OrdersConsts.MaxTemplateNameLength).IsRequired();
            b.Property(t => t.NormalizedName).HasMaxLength(OrdersConsts.MaxTemplateNameLength).IsRequired();
            b.Property(t => t.Description).HasMaxLength(ReferenceDataConsts.MaxDescriptionLength);
            b.Property(t => t.CustomerName).HasMaxLength(OrdersConsts.MaxStopNameLength).IsRequired();
            b.Property(t => t.CustomerReference).HasMaxLength(OrdersConsts.MaxCustomerReferenceLength);
            b.Property(t => t.InternalRemarks).HasMaxLength(OrdersConsts.MaxRemarksLength);
            b.Property(t => t.InvoicingRemarks).HasMaxLength(OrdersConsts.MaxRemarksLength);
            b.Property(t => t.Attributes).HasJsonbDictionary();
            ConfigureFinancialTerms(b.OwnsOne(t => t.FinancialTerms));
            b.Navigation(t => t.FinancialTerms).IsRequired();
            b.OwnsOne(t => t.TripRequest, r =>
            {
                r.Property(x => x.VehicleIds).HasJsonbList();
                r.Property(x => x.DriverIds).HasJsonbList();
                r.Property(x => x.LineIds).HasNullableJsonbList();
                r.Property(x => x.AgreedPurchaseAmount).HasPrecision(MoneyPrecision, MoneyScale);
                r.Property(x => x.AgreedPurchaseCurrencyCode).HasMaxLength(OrdersConsts.MaxCurrencyCodeLength);
            });
            b.OwnsOne(t => t.Schedule, s =>
            {
                s.Property(x => x.Recurrence).HasColumnName("Recurrence");
                s.Property(x => x.Interval).HasColumnName("Interval");
                s.Property(x => x.DaysOfWeek).HasColumnName("DaysOfWeek");
                s.Property(x => x.DayOfMonth).HasColumnName("DayOfMonth");
                s.Property(x => x.HorizonDays).HasColumnName("HorizonDays");
                s.Property(x => x.ValidFrom).HasColumnName("ValidFrom");
                s.Property(x => x.ValidTill).HasColumnName("ValidTill");
            });
            b.Navigation(t => t.Schedule).IsRequired();
            b.HasMany(t => t.Lines).WithOne().HasForeignKey(l => l.TemplateId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_TransportOrderTemplateLines_Template");
            b.HasMany(t => t.DefaultStops).WithOne().HasForeignKey(s => s.TemplateId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_TransportOrderTemplateDefaultStops_Template");
            b.HasMany(t => t.References).WithOne().HasForeignKey(r => r.TemplateId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_TransportOrderTemplateReferences_Template");
            b.HasMany(t => t.PriceLists).WithOne().HasForeignKey(p => p.TemplateId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_TransportOrderTemplatePriceLists_Template");
            b.HasMany(t => t.DiscountsOrSurcharges).WithOne().HasForeignKey(d => d.TemplateId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_TransportOrderTemplateDiscountsOrSurcharges_Template");
            b.HasIndex(t => t.TenantId);
            b.HasIndex(t => new { t.TenantId, t.NormalizedName }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
            b.HasIndex(t => new { t.TenantId, t.IsActive });
            b.HasIndex(t => new { t.TenantId, t.CustomerRelationId });
        });

        builder.Entity<TransportOrderTemplateLine>(b =>
        {
            b.ToTable("TransportOrderTemplateLines", OrdersDbProperties.DbSchema, t => t.HasCheckConstraint("CK_TransportOrderTemplateLines_Position", "\"Position\" >= 1"));
            b.ConfigureByConvention();
            ConfigureFreight(b.OwnsOne(l => l.Freight));
            b.Navigation(l => l.Freight).IsRequired();
            b.Property(l => l.Attributes).HasJsonbDictionary();
            b.Property(l => l.Measurements).HasJsonbList();
            b.Property(l => l.RequiredTransportCharacteristicIds).HasJsonbList();
            b.HasMany(l => l.Stops).WithOne().HasForeignKey(s => s.LineId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_TransportOrderTemplateStops_Line");
            b.HasIndex(l => l.TenantId);
            b.HasIndex(l => new { l.TemplateId, l.Position });
        });

        builder.Entity<TransportOrderTemplateStop>(b =>
        {
            b.ToTable("TransportOrderTemplateStops", OrdersDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.HasKey(s => new { s.LineId, s.Position });
            ConfigureStopDetails(b.OwnsOne(s => s.Details));
            b.Navigation(s => s.Details).IsRequired();
            b.HasIndex(s => s.TenantId);
        });

        builder.Entity<TransportOrderTemplateDefaultStop>(b =>
        {
            b.ToTable("TransportOrderTemplateDefaultStops", OrdersDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.HasKey(s => new { s.TemplateId, s.Position });
            ConfigureStopDetails(b.OwnsOne(s => s.Details));
            b.Navigation(s => s.Details).IsRequired();
            b.HasIndex(s => s.TenantId);
        });

        builder.Entity<TransportOrderTemplateReference>(b =>
        {
            b.ToTable("TransportOrderTemplateReferences", OrdersDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(r => r.Type).HasMaxLength(OrdersConsts.MaxReferenceTypeLength).IsRequired();
            b.Property(r => r.Value).HasMaxLength(OrdersConsts.MaxReferenceValueLength).IsRequired();
            b.Property(r => r.Description).HasMaxLength(OrdersConsts.MaxReferenceDescriptionLength);
            b.HasIndex(r => r.TenantId);
            b.HasIndex(r => r.TemplateId);
        });

        builder.Entity<TransportOrderTemplatePriceList>(b =>
        {
            b.ToTable("TransportOrderTemplatePriceLists", OrdersDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.HasIndex(p => p.TenantId);
            b.HasIndex(p => new { p.TemplateId, p.PriceListId }).IsUnique();
        });

        builder.Entity<TransportOrderTemplateDiscountOrSurcharge>(b =>
        {
            b.ToTable("TransportOrderTemplateDiscountsOrSurcharges", OrdersDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.HasIndex(d => d.TenantId);
            b.HasIndex(d => new { d.TemplateId, d.DiscountOrSurchargeId }, "IX_TransportOrderTemplateDiscountsOrSurcharges_Template_Ds").IsUnique();
        });

        // ── consignment notes ──────────────────────────────────────────────────

        builder.Entity<ConsignmentNoteProfile>(b =>
        {
            b.ToTable("ConsignmentNoteProfiles", OrdersDbProperties.DbSchema, t => t.HasCheckConstraint("CK_ConsignmentNoteProfiles_Copies", "\"Copies\" BETWEEN 1 AND 5"));
            b.ConfigureByConvention();
            b.Property(p => p.Name).HasMaxLength(ReferenceDataConsts.MaxNameLength).IsRequired();
            b.Property(p => p.NormalizedName).HasMaxLength(ReferenceDataConsts.MaxNameLength).IsRequired();
            b.Property(p => p.Description).HasMaxLength(ReferenceDataConsts.MaxDescriptionLength);
            b.Property(p => p.Box6Columns).HasJsonbList();
            b.Property(p => p.Box13Items).HasJsonbList();
            b.Property(p => p.Box18Items).HasJsonbList();
            b.Property(p => p.FixedDescriptionIds).HasJsonbList();
            b.HasIndex(p => p.TenantId);
            b.HasIndex(p => new { p.TenantId, p.NormalizedName }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
            b.HasIndex(p => p.TenantId, "IX_ConsignmentNoteProfiles_TenantId_Default").IsUnique().AreNullsDistinct(false).HasFilter("\"IsDefault\" = true AND " + NotDeleted);
        });

        builder.Entity<ConsignmentNoteFixedDescription>(b =>
        {
            b.ToTable("ConsignmentNoteFixedDescriptions", OrdersDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(f => f.Name).HasMaxLength(ReferenceDataConsts.MaxNameLength).IsRequired();
            b.Property(f => f.NormalizedName).HasMaxLength(ReferenceDataConsts.MaxNameLength).IsRequired();
            b.Property(f => f.Text).HasMaxLength(ConsignmentNoteConsts.MaxFixedDescriptionTextLength).IsRequired();
            b.Property(f => f.Description).HasMaxLength(ReferenceDataConsts.MaxDescriptionLength);
            b.HasIndex(f => f.TenantId);
            b.HasIndex(f => new { f.TenantId, f.NormalizedName }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
        });

        builder.Entity<ConsignmentNoteDocument>(b =>
        {
            b.ToTable("ConsignmentNoteDocuments", OrdersDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(d => d.Number).HasMaxLength(ConsignmentNoteConsts.MaxNumberLength).IsRequired();
            b.Property(d => d.BlobName).HasMaxLength(ConsignmentNoteConsts.MaxBlobNameLength).IsRequired();
            b.Property(d => d.ContentType).HasMaxLength(ConsignmentNoteConsts.MaxContentTypeLength).IsRequired();
            b.Property(d => d.FileName).HasMaxLength(ReferenceDataConsts.MaxDescriptionLength).IsRequired();
            b.Property(d => d.Sha256).HasMaxLength(ConsignmentNoteConsts.Sha256Length).IsRequired();
            b.Property(d => d.DataSnapshot).HasColumnType("jsonb").IsRequired();
            b.HasMany(d => d.Lines).WithOne().HasForeignKey(l => l.DocumentId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_ConsignmentNoteDocumentLines_Document");
            b.HasIndex(d => d.TenantId);
            b.HasIndex(d => new { d.TenantId, d.Number }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
            b.HasIndex(d => new { d.TenantId, d.OrderId });
            b.HasIndex(d => new { d.TenantId, d.TripId });
        });

        builder.Entity<ConsignmentNoteDocumentLine>(b =>
        {
            b.ToTable("ConsignmentNoteDocumentLines", OrdersDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.HasKey(l => new { l.DocumentId, l.LineId });
            b.HasIndex(l => l.TenantId);
            b.HasIndex(l => l.LineId);
        });

        // ── attribute definitions ──────────────────────────────────────────────

        builder.Entity<OrderAttributeDefinition>(b =>
        {
            b.ToTable("OrderAttributeDefinitions", OrdersDbProperties.DbSchema, t => t.HasCheckConstraint("CK_OrderAttributeDefinitions_Position", "\"Position\" >= 1"));
            b.ConfigureByConvention();
            b.Property(d => d.Key).HasMaxLength(OrdersConsts.MaxAttributeKeyLength).IsRequired();
            b.Property(d => d.Name).HasLocalizedNameColumn();
            b.Property(d => d.Description).HasMaxLength(ReferenceDataConsts.MaxDescriptionLength);
            b.HasIndex(d => d.TenantId);
            b.HasIndex(d => new { d.TenantId, d.Target, d.Key }).IsUnique().AreNullsDistinct(false).HasFilter(NotDeleted);
        });
    }

    private static void ConfigureFinancialTerms<TOwner>(OwnedNavigationBuilder<TOwner, TransportOrderFinancialTerms> t) where TOwner : class
    {
        t.Property(x => x.CurrencyCode).HasColumnName("CurrencyCode").HasMaxLength(OrdersConsts.MaxCurrencyCodeLength).IsRequired();
        t.Property(x => x.VatPercentageId).HasColumnName("VatPercentageId");
        t.Property(x => x.PaymentTermDays).HasColumnName("PaymentTermDays");
        t.Property(x => x.MultipleMatches).HasColumnName("MultipleMatches");
        t.Property(x => x.FixedPriceAmount).HasColumnName("FixedPriceAmount").HasPrecision(MoneyPrecision, MoneyScale);
        t.Property(x => x.SendFreightDocumentsWithInvoice).HasColumnName("SendFreightDocumentsWithInvoice");
        /* Design 61 point 3: the charged bases as a bit mask of QuantitySource values (0 = none, the value of existing rows). */
        t.Property(x => x.ChargedBasesMask).HasColumnName("ChargedBases");
    }

    private static void ConfigureFreight<TOwner>(OwnedNavigationBuilder<TOwner, FreightDetails> f) where TOwner : class
    {
        f.Property(x => x.Quantity).HasPrecision(MoneyPrecision, QuantityScale);
        f.Property(x => x.Positions).HasPrecision(MoneyPrecision, QuantityScale);
        f.Property(x => x.Colli).HasPrecision(MoneyPrecision, QuantityScale);
        f.Property(x => x.InvoiceReference).HasMaxLength(OrdersConsts.MaxLineTextLength);
        f.Property(x => x.Remarks).HasMaxLength(OrdersConsts.MaxRemarksLength);
        f.Property(x => x.InternalRemarks).HasMaxLength(OrdersConsts.MaxRemarksLength);
        f.Property(x => x.ShipperInstructions).HasMaxLength(OrdersConsts.MaxRemarksLength);
        f.Property(x => x.SpecialAgreements).HasMaxLength(OrdersConsts.MaxRemarksLength);
        f.Property(x => x.AttachedDocuments).HasMaxLength(OrdersConsts.MaxLineTextLength);
        f.Property(x => x.ContainerNumber).HasMaxLength(OrdersConsts.MaxContainerFieldLength);
        f.Property(x => x.SizeOrIso).HasMaxLength(OrdersConsts.MaxContainerFieldLength);
        f.Property(x => x.PinCode).HasMaxLength(OrdersConsts.MaxContainerFieldLength);
        f.Property(x => x.ShippingCompany).HasMaxLength(OrdersConsts.MaxContainerFieldLength);
        f.Property(x => x.Barcode).HasMaxLength(OrdersConsts.MaxBarcodeLength);
    }

    private static void ConfigureStopDetails<TOwner>(OwnedNavigationBuilder<TOwner, StopDetails> d) where TOwner : class
    {
        d.Property(x => x.Reference).HasMaxLength(OrdersConsts.MaxStopReferenceLength);
        d.Property(x => x.Remarks).HasMaxLength(OrdersConsts.MaxStopRemarksLength);
    }
}
