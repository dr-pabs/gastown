using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using CrmPlatform.AnalyticsService.Domain.Entities;
using CrmPlatform.ServiceTemplate.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;

namespace CrmPlatform.AnalyticsService.Infrastructure.Data;

public sealed class AnalyticsDbContext(
    DbContextOptions<AnalyticsDbContext> options,
    ITenantContextAccessor tenantContextAccessor)
    : ServiceDbContext(options, tenantContextAccessor)
{
    public DbSet<AnalyticsEvent>   Events          { get; init; } = null!;
    public DbSet<MetricSnapshot>   MetricSnapshots { get; init; } = null!;
    public DbSet<AnalyticsIdempotencyRecord> IdempotencyRecords { get; init; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // global tenant + soft-delete filters first

        modelBuilder.ApplyConfiguration(new AnalyticsEventConfiguration());
        modelBuilder.ApplyConfiguration(new MetricSnapshotConfiguration());
        modelBuilder.ApplyConfiguration(new AnalyticsIdempotencyRecordConfiguration());
    }
}

file sealed class AnalyticsEventConfiguration : IEntityTypeConfiguration<AnalyticsEvent>
{
    public void Configure(EntityTypeBuilder<AnalyticsEvent> b)
    {
        b.ToTable("Events", "analytics");
        b.HasKey(e => e.Id);
        b.Property(e => e.Source).HasConversion<string>().IsRequired();
        b.Property(e => e.EventType).IsRequired().HasMaxLength(128);
        b.Property(e => e.SourceId).IsRequired().HasMaxLength(128);
        b.Property(e => e.PayloadJson).IsRequired().HasMaxLength(65_536);

        b.HasIndex(e => new { e.TenantId, e.EventType, e.OccurredAt });
        b.HasIndex(e => new { e.TenantId, e.Source, e.OccurredAt });
    }
}

file sealed class MetricSnapshotConfiguration : IEntityTypeConfiguration<MetricSnapshot>
{
    public void Configure(EntityTypeBuilder<MetricSnapshot> b)
    {
        b.ToTable("MetricSnapshots", "analytics");
        b.HasKey(e => e.Id);
        b.Property(e => e.Key).HasConversion<string>().IsRequired();
        b.Property(e => e.Granularity).HasConversion<string>().IsRequired();
        b.Property(e => e.Value).HasColumnType("decimal(18,4)");

        // One snapshot per key + granularity + bucket per tenant
        b.HasIndex(e => new { e.TenantId, e.Key, e.Granularity, e.BucketStart }).IsUnique();
    }
}

file sealed class AnalyticsIdempotencyRecordConfiguration : IEntityTypeConfiguration<AnalyticsIdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<AnalyticsIdempotencyRecord> b)
    {
        b.ToTable("IdempotencyStore", "analytics");
        b.HasKey(e => e.MessageId);
        b.Property(e => e.MessageId).HasMaxLength(200);
    }
}
