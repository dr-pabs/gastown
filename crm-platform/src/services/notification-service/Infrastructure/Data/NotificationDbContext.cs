using CrmPlatform.NotificationService.Domain.Entities;
using CrmPlatform.ServiceTemplate.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CrmPlatform.NotificationService.Infrastructure.Data;

public sealed class NotificationDbContext(
    DbContextOptions<NotificationDbContext> options,
    ITenantContextAccessor tenantContextAccessor)
    : ServiceDbContext(options, tenantContextAccessor)
{
    public DbSet<NotificationTemplate>   Templates    { get; init; } = null!;
    public DbSet<NotificationRecord>     Records      { get; init; } = null!;
    public DbSet<InAppNotification>      InAppItems   { get; init; } = null!;
    public DbSet<NotificationPreference> Preferences  { get; init; } = null!;
    public DbSet<NotificationIdempotencyRecord> IdempotencyRecords { get; init; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // applies global tenant + soft-delete filters

        // ─── Schema ───────────────────────────────────────────────────────────
        modelBuilder.HasDefaultSchema("notifications");

        // ─── NotificationTemplate ─────────────────────────────────────────────
        modelBuilder.Entity<NotificationTemplate>(e =>
        {
            e.ToTable("Templates");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.BodyPlainTemplate).HasMaxLength(8000).IsRequired();
            e.Property(x => x.SubjectTemplate).HasMaxLength(500);
            e.Property(x => x.BodyHtmlTemplate).HasColumnType("nvarchar(max)");
            // Unique: one active template per tenant+channel+category
            e.HasIndex(x => new { x.TenantId, x.Channel, x.Category, x.IsActive })
             .HasFilter("[IsActive] = 1")
             .IsUnique()
             .HasDatabaseName("UIX_Templates_TenantChannelCategory_Active");
        });

        // ─── NotificationRecord ───────────────────────────────────────────────
        modelBuilder.Entity<NotificationRecord>(e =>
        {
            e.ToTable("Records");
            e.HasKey(x => x.Id);
            e.Property(x => x.RecipientAddress).HasMaxLength(500).IsRequired();
            e.Property(x => x.BodyPlain).HasMaxLength(8000).IsRequired();
            e.Property(x => x.BodyHtml).HasColumnType("nvarchar(max)");
            e.Property(x => x.Subject).HasMaxLength(500);
            e.Property(x => x.ProviderMessageId).HasMaxLength(256);
            e.Property(x => x.FailureReason).HasMaxLength(2000);
            // Index for webhook lookup by provider message id
            e.HasIndex(x => x.ProviderMessageId)
             .HasFilter("[ProviderMessageId] IS NOT NULL")
             .HasDatabaseName("IX_Records_ProviderMessageId");
            // Index for user inbox queries
            e.HasIndex(x => new { x.TenantId, x.RecipientUserId, x.Channel, x.CreatedAt })
             .HasDatabaseName("IX_Records_RecipientChannel_Date");
        });

        // ─── InAppNotification ────────────────────────────────────────────────
        modelBuilder.Entity<InAppNotification>(e =>
        {
            e.ToTable("InAppNotifications");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.Body).HasMaxLength(2000).IsRequired();
            e.Property(x => x.ActionUrl).HasMaxLength(1000);
            // Index for inbox queries — unread first
            e.HasIndex(x => new { x.TenantId, x.RecipientUserId, x.IsRead, x.CreatedAt })
             .HasDatabaseName("IX_InApp_User_IsRead_Date");
        });

        // ─── NotificationPreference ───────────────────────────────────────────
        modelBuilder.Entity<NotificationPreference>(e =>
        {
            e.ToTable("Preferences");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.UserId, x.Channel, x.Category })
             .IsUnique()
             .HasDatabaseName("UIX_Preferences_UserChannelCategory");
        });

        // ─── Idempotency ──────────────────────────────────────────────────────
        modelBuilder.Entity<NotificationIdempotencyRecord>(e =>
        {
            e.ToTable("IdempotencyRecords");
            e.HasKey(x => x.MessageId);
            e.Property(x => x.MessageId).HasMaxLength(256);
        });
    }
}
