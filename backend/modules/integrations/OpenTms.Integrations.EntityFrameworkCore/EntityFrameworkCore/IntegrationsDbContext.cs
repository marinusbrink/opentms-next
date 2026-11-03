using Microsoft.EntityFrameworkCore;
using OpenTms.Integrations.OrderIntake;
using OpenTms.Integrations.OrderIntake.Profiles;
using OpenTms.Integrations.Routing;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace OpenTms.Integrations.EntityFrameworkCore;

[ConnectionStringName(IntegrationsDbProperties.ConnectionStringName)]
public class IntegrationsDbContext : AbpDbContext<IntegrationsDbContext>
{
    /// <summary>The route cache of design 60: integrations."RouteCache", one row per (tenant, key), expiring rows.</summary>
    public DbSet<RouteCacheEntry> RouteCache { get; set; } = default!;

    /* Design 72: the order intake. */

    public DbSet<MailboxConnection> MailboxConnections { get; set; } = default!;

    public DbSet<ApiClient> ApiClients { get; set; } = default!;

    public DbSet<InboundMessage> InboundMessages { get; set; } = default!;

    public DbSet<InboundDocument> InboundDocuments { get; set; } = default!;

    public DbSet<InboundDocumentStep> InboundDocumentSteps { get; set; } = default!;

    public DbSet<ImportProfile> ImportProfiles { get; set; } = default!;

    /// <summary>AP-05: written with the host as the current tenant, so its rows live in the host database.</summary>
    public DbSet<AiUsageRecord> AiUsageRecords { get; set; } = default!;

    public IntegrationsDbContext(DbContextOptions<IntegrationsDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        /* Every table of this module lives in its own schema ("integrations").
         * Never map an entity of another module into this context. */
        builder.HasDefaultSchema(IntegrationsDbProperties.DbSchema);
        /* The Received list's trigram search (design 72 review M2, as Orders and Master Data). */
        builder.HasPostgresExtension("pg_trgm");

        builder.Entity<RouteCacheEntry>(b =>
        {
            b.ToTable("RouteCache", IntegrationsDbProperties.DbSchema);
            b.ConfigureByConvention();
            b.Property(e => e.Key).IsRequired().HasMaxLength(RoutingConsts.MaxKeyLength);
            b.Property(e => e.Provider).IsRequired().HasMaxLength(RoutingConsts.MaxProviderLength);
            b.Property(e => e.TollAmount).HasPrecision(RoutingConsts.MoneyPrecision, RoutingConsts.MoneyScale);
            b.Property(e => e.TollCurrencyCode).HasMaxLength(RoutingConsts.MaxCurrencyCodeLength);
            b.Property(e => e.Co2Kg).HasPrecision(RoutingConsts.Co2Precision, RoutingConsts.Co2Scale);
            /* The lookup of every routing call. NULLS NOT DISTINCT so host-level rows are unique too. */
            b.HasIndex(e => new { e.TenantId, e.Key }).IsUnique().AreNullsDistinct(false);
            /* The daily purge of expired rows. */
            b.HasIndex(e => e.ExpiresAt);
        });

        ConfigureOrderIntake(builder);
    }

    private const string NotDeleted = "\"IsDeleted\" = false";

    /// <summary>
    /// Design 72 "Migration strategy": lengths from <see cref="OrderIntakeConsts"/>; Master Data and Orders ids without a
    /// foreign key (another module's schema); instants as UTC <c>DateTime</c>s like the module's route cache.
    /// </summary>
    private static void ConfigureOrderIntake(ModelBuilder builder)
    {
        var schema = IntegrationsDbProperties.DbSchema;

        builder.Entity<MailboxConnection>(b =>
        {
            b.ToTable("MailboxConnections", schema);
            b.ConfigureByConvention();
            b.Ignore(c => c.IsSyncable);
            b.Ignore(c => c.AwaitsConsent);
            b.Property(c => c.DisplayName).HasMaxLength(OrderIntakeConsts.MaxDisplayNameLength).IsRequired();
            b.Property(c => c.EmailAddress).HasMaxLength(OrderIntakeConsts.MaxEmailAddressLength);
            b.Property(c => c.FolderId).HasMaxLength(OrderIntakeConsts.MaxFolderIdLength);
            b.Property(c => c.FolderName).HasMaxLength(OrderIntakeConsts.MaxFolderNameLength);
            b.Property(c => c.RefreshTokenEncrypted).HasMaxLength(OrderIntakeConsts.MaxRefreshTokenLength);
            b.Property(c => c.SyncCursor).HasMaxLength(OrderIntakeConsts.MaxSyncCursorLength);
            b.Property(c => c.LastErrorCode).HasMaxLength(OrderIntakeConsts.MaxErrorCodeLength);
            /* One connection per mailbox and folder among undeleted rows; rows still awaiting consent have no address yet. */
            b.HasIndex(c => new { c.TenantId, c.Provider, c.EmailAddress, c.FolderId })
                .IsUnique().AreNullsDistinct(false).HasFilter("\"EmailAddress\" IS NOT NULL AND " + NotDeleted);
            /* The sync worker's sweep of Active connections. */
            b.HasIndex(c => new { c.TenantId, c.Status });
        });

        builder.Entity<ApiClient>(b =>
        {
            b.ToTable("ApiClients", schema);
            b.ConfigureByConvention();
            b.Property(c => c.Name).HasMaxLength(OrderIntakeConsts.MaxApiClientNameLength).IsRequired();
            b.Property(c => c.ClientId).HasMaxLength(OrderIntakeConsts.MaxClientIdLength).IsRequired();
            b.Property(c => c.SecretLastFour).HasMaxLength(OrderIntakeConsts.SecretLastFourLength).IsRequired();
            /* Unique across every tenant: the token server finds the application by it. */
            b.HasIndex(c => c.ClientId).IsUnique();
            b.HasIndex(c => c.TenantId);
        });

        builder.Entity<InboundMessage>(b =>
        {
            b.ToTable("InboundMessages", schema);
            b.ConfigureByConvention();
            b.Property(m => m.ExternalId).HasMaxLength(OrderIntakeConsts.MaxExternalIdLength).IsRequired();
            b.Property(m => m.From).HasMaxLength(OrderIntakeConsts.MaxFromLength);
            b.Property(m => m.Subject).HasMaxLength(OrderIntakeConsts.MaxSubjectLength);
            b.Property(m => m.RawBlobName).HasMaxLength(OrderIntakeConsts.MaxBlobNameLength);
            /* The dedupe key: the same mail or API ExternalId is stored once (constitution rule 3). */
            b.HasIndex(m => new { m.TenantId, m.Channel, m.SourceId, m.ExternalId }).IsUnique().AreNullsDistinct(false);
            /* The Received list, newest first, and its status filter. */
            b.HasIndex(m => new { m.TenantId, m.ReceivedAt }).IsDescending(false, true);
            b.HasIndex(m => new { m.TenantId, m.Status });
            /* The list's ILIKE search over sender, subject and external id (review M2): every column of the OR is indexed, so
             * the planner can combine them instead of scanning the table. */
            b.HasIndex(m => m.From, "IX_InboundMessages_From_Trigram").HasMethod("gin").HasOperators("gin_trgm_ops");
            b.HasIndex(m => m.Subject, "IX_InboundMessages_Subject_Trigram").HasMethod("gin").HasOperators("gin_trgm_ops");
            b.HasIndex(m => m.ExternalId, "IX_InboundMessages_ExternalId_Trigram").HasMethod("gin").HasOperators("gin_trgm_ops");
        });

        builder.Entity<InboundDocument>(b =>
        {
            b.ToTable("InboundDocuments", schema);
            b.ConfigureByConvention();
            b.Property(d => d.FileName).HasMaxLength(OrderIntakeConsts.MaxFileNameLength);
            b.Property(d => d.ContentType).HasMaxLength(OrderIntakeConsts.MaxContentTypeLength).IsRequired();
            b.Property(d => d.Sha256).HasMaxLength(OrderIntakeConsts.Sha256Length).IsRequired();
            b.Property(d => d.BlobName).HasMaxLength(OrderIntakeConsts.MaxBlobNameLength).IsRequired();
            b.Property(d => d.SenderKey).HasMaxLength(OrderIntakeConsts.MaxSenderKeyLength);
            b.Property(d => d.Fingerprint).HasMaxLength(OrderIntakeConsts.FingerprintLength);
            b.Property(d => d.Mapping).HasJsonbMapping();
            b.HasOne<InboundMessage>().WithMany().HasForeignKey(d => d.InboundMessageId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(d => d.Steps).WithOne().HasForeignKey(s => s.InboundDocumentId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(d => new { d.TenantId, d.InboundMessageId });
            b.HasIndex(d => new { d.TenantId, d.ImportProfileId });
        });

        builder.Entity<InboundDocumentStep>(b =>
        {
            b.ToTable("InboundDocumentSteps", schema);
            b.ConfigureByConvention();
            b.Property(s => s.MessageKey).HasMaxLength(OrderIntakeConsts.MaxMessageKeyLength).IsRequired();
            b.Property(s => s.Parameters).HasJsonbDictionary();
            b.Property(s => s.ModelId).HasMaxLength(OrderIntakeConsts.MaxModelIdLength);
            b.Property(s => s.OrderNumber).HasMaxLength(OrderIntakeConsts.MaxOrderNumberLength);
            b.HasIndex(s => s.InboundDocumentId);
        });

        builder.Entity<ImportProfile>(b =>
        {
            b.ToTable("ImportProfiles", schema);
            b.ConfigureByConvention();
            b.Ignore(p => p.IsCandidate);
            b.Property(p => p.Name).HasMaxLength(OrderIntakeConsts.MaxProfileNameLength).IsRequired();
            b.Property(p => p.SenderKey).HasMaxLength(OrderIntakeConsts.MaxSenderKeyLength);
            b.Property(p => p.Target).HasMaxLength(OrderIntakeConsts.MaxTargetLength).IsRequired();
            b.Property(p => p.ModelId).HasMaxLength(OrderIntakeConsts.MaxModelIdLength);
            b.Property(p => p.Rules).HasJsonbRules();
            b.Property(p => p.PreviousRules).HasNullableJsonbRules();
            b.Property(p => p.Signature).HasJsonbStringList();
            /* Profile matching: candidates by format and sender key. */
            b.HasIndex(p => new { p.TenantId, p.Format, p.SenderKey });
        });

        builder.Entity<AiUsageRecord>(b =>
        {
            b.ToTable("AiUsageRecords", schema);
            b.ConfigureByConvention();
            b.Property(r => r.Agent).HasMaxLength(OrderIntakeConsts.MaxAgentLength).IsRequired();
            b.Property(r => r.ModelId).HasMaxLength(OrderIntakeConsts.MaxModelIdLength).IsRequired();
            b.HasIndex(r => new { r.TenantId, r.At });
        });
    }
}
