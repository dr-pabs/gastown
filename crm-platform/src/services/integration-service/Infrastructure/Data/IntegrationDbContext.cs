using CrmPlatform.IntegrationService.Domain.Entities;
using CrmPlatform.IntegrationService.Domain.Enums;
using CrmPlatform.ServiceTemplate.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CrmPlatform.IntegrationService.Infrastructure.Data;

public sealed class IntegrationDbContext(
    DbContextOptions<IntegrationDbContext> options,
    ITenantContextAccessor tenantContextAccessor)
    : ServiceDbContext(options, tenantContextAccessor)
{
    public DbSet<ConnectorConfig>               Connectors          { get; init; } = null!;
    public DbSet<OutboundJob>                   OutboundJobs        { get; init; } = null!;
    public DbSet<InboundEvent>                  InboundEvents       { get; init; } = null!;
    public DbSet<IntegrationIdempotencyRecord>  IdempotencyRecords  { get; init; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // global tenant + soft-delete filters

        modelBuilder.HasDefaultSchema("integrations");

        // ─── ConnectorConfig ──────────────────────────────────────────────────
        modelBuilder.Entity<ConnectorConfig>(e =>
        {
            e.ToTable("ConnectorConfigs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Label).HasMaxLength(200).IsRequired();
            e.Property(x => x.KeyVaultSecretName).HasMaxLength(256);
            e.Property(x => x.ExternalAccountId).HasMaxLength(256);
            e.Property(x => x.OAuthScopes).HasMaxLength(1000);
            e.Property(x => x.WebhookSecret).HasMaxLength(512);
            e.Property(x => x.ErrorMessage).HasMaxLength(2000);

            // Owned value object — maps to same table
            e.OwnsOne(x => x.RetryPolicy, rp =>
            {
                rp.Property(p => p.MaxRetryDurationMinutes).HasColumnName("RetryMaxDurationMinutes").HasDefaultValue(60);
                rp.Property(p => p.InitialRetryDelaySeconds).HasColumnName("RetryInitialDelaySeconds").HasDefaultValue(30);
                rp.Property(p => p.MaxRetryDelaySeconds).HasColumnName("RetryMaxDelaySeconds").HasDefaultValue(300);
                rp.Property(p => p.BackoffMultiplier).HasColumnName("RetryBackoffMultiplier").HasDefaultValue(2.0);
            });

            // One active config per connector type per tenant
            // (Active = not Disconnected and not soft-deleted)
            e.HasIndex(x => new { x.TenantId, x.ConnectorType })
             .HasFilter("[IsDeleted] = 0 AND [Status] != 0")
             .IsUnique()
             .HasDatabaseName("UIX_ConnectorConfigs_Tenant_Type_Active");
        });

        // ─── OutboundJob ──────────────────────────────────────────────────────
        modelBuilder.Entity<OutboundJob>(e =>
        {
            e.ToTable("OutboundJobs");
            e.HasKey(x => x.Id);
            e.Property(x => x.EventType).HasMaxLength(200).IsRequired();
            e.Property(x => x.Payload).HasColumnType("nvarchar(max)").IsRequired();
            e.Property(x => x.ExternalId).HasMaxLength(512);
            e.Property(x => x.FailureReason).HasMaxLength(2000);

            // Worker polling index
            e.HasIndex(x => new { x.Status, x.NextRetryAt })
             .HasDatabaseName("IX_OutboundJobs_Status_NextRetryAt");

            e.HasIndex(x => x.ConnectorConfigId)
             .HasDatabaseName("IX_OutboundJobs_ConnectorConfigId");

            e.HasOne(x => x.ConnectorConfig)
             .WithMany()
             .HasForeignKey(x => x.ConnectorConfigId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ─── InboundEvent ─────────────────────────────────────────────────────
        modelBuilder.Entity<InboundEvent>(e =>
        {
            e.ToTable("InboundEvents");
            e.HasKey(x => x.Id);
            e.Property(x => x.ExternalEventId).HasMaxLength(512);
            e.Property(x => x.RawPayload).HasColumnType("nvarchar(max)").IsRequired();
            e.Property(x => x.NormalisedEventType).HasMaxLength(200);
            e.Property(x => x.ServiceBusMessageId).HasMaxLength(256);
            e.Property(x => x.FailureReason).HasMaxLength(2000);

            // Query index
            e.HasIndex(x => new { x.TenantId, x.ConnectorType, x.ReceivedAt })
             .HasDatabaseName("IX_InboundEvents_Tenant_Connector_ReceivedAt");

            // Deduplication — one row per external event id (when provided)
            e.HasIndex(x => x.ExternalEventId)
             .HasFilter("[ExternalEventId] IS NOT NULL")
             .IsUnique()
             .HasDatabaseName("UIX_InboundEvents_ExternalEventId");
        });

        // ─── Idempotency ──────────────────────────────────────────────────────
        modelBuilder.Entity<IntegrationIdempotencyRecord>(e =>
        {
            e.ToTable("IdempotencyRecords");
            e.HasKey(x => x.MessageId);
            e.Property(x => x.MessageId).HasMaxLength(256);
            e.Property(x => x.ProcessedAt).IsRequired();
        });
    }
}

public sealed class IntegrationIdempotencyRecord
{
    public string   MessageId   { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; }
}
