using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using CrmPlatform.PlatformAdminService.Domain.Entities;
using CrmPlatform.PlatformAdminService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;

namespace CrmPlatform.PlatformAdminService.Infrastructure.Data;

public sealed class PlatformDbContext(
    DbContextOptions<PlatformDbContext> options,
    ITenantContextAccessor tenantContextAccessor)
    : ServiceDbContext(options, tenantContextAccessor)
{
    public DbSet<Tenant>                 Tenants              { get; init; } = null!;
    public DbSet<TenantProvisioningLog>  ProvisioningLogs     { get; init; } = null!;
    public DbSet<PlatformIdempotencyRecord> IdempotencyRecords { get; init; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // global filters

        modelBuilder.ApplyConfiguration(new TenantConfiguration());
        modelBuilder.ApplyConfiguration(new TenantProvisioningLogConfiguration());
        modelBuilder.ApplyConfiguration(new PlatformIdempotencyRecordConfiguration());
    }
}

file sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.ToTable("Tenants", "platform");
        b.HasKey(e => e.Id);
        b.Property(e => e.Name).IsRequired().HasMaxLength(200);
        b.Property(e => e.Slug).IsRequired().HasMaxLength(100);
        b.Property(e => e.PlanId).IsRequired().HasMaxLength(50);
        b.Property(e => e.Status).HasConversion<string>().IsRequired();
        b.Property(e => e.CreatedBy).IsRequired().HasMaxLength(200);

        b.HasIndex(e => e.Slug).IsUnique();

        b.HasMany<TenantProvisioningLog>()
            .WithOne(l => l.Tenant)
            .HasForeignKey(l => l.TenantEntityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

file sealed class TenantProvisioningLogConfiguration : IEntityTypeConfiguration<TenantProvisioningLog>
{
    public void Configure(EntityTypeBuilder<TenantProvisioningLog> b)
    {
        b.ToTable("TenantProvisioningLogs", "platform");
        b.HasKey(e => e.Id);
        b.Property(e => e.Step).IsRequired().HasMaxLength(100);
        b.Property(e => e.StepStatus).HasConversion<string>().IsRequired();
        b.Property(e => e.Details).HasMaxLength(2000);
        b.HasIndex(e => new { e.TenantEntityId, e.OccurredAt });
    }
}

file sealed class PlatformIdempotencyRecordConfiguration : IEntityTypeConfiguration<PlatformIdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<PlatformIdempotencyRecord> b)
    {
        b.ToTable("IdempotencyStore", "platform");
        b.HasKey(e => e.MessageId);
        b.Property(e => e.MessageId).HasMaxLength(200);
    }
}
