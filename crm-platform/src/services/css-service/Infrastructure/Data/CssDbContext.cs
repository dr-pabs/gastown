using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using CrmPlatform.CssService.Domain.Entities;
using CrmPlatform.CssService.Domain.Enums;
using CrmPlatform.ServiceTemplate.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;

namespace CrmPlatform.CssService.Infrastructure.Data;

public sealed class CssDbContext(
    DbContextOptions<CssDbContext> options,
    ITenantContextAccessor tenantContextAccessor)
    : ServiceDbContext(options, tenantContextAccessor)
{
    public DbSet<Case>             Cases              { get; init; } = null!;
    public DbSet<CaseComment>      CaseComments       { get; init; } = null!;
    public DbSet<EscalationRecord> EscalationRecords  { get; init; } = null!;
    public DbSet<SlaPolicy>        SlaPolicies        { get; init; } = null!;
    public DbSet<CssIdempotencyRecord> IdempotencyRecords { get; init; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // global tenant + soft-delete filters first

        modelBuilder.ApplyConfiguration(new CaseConfiguration());
        modelBuilder.ApplyConfiguration(new CaseCommentConfiguration());
        modelBuilder.ApplyConfiguration(new EscalationRecordConfiguration());
        modelBuilder.ApplyConfiguration(new SlaPolicyConfiguration());
        modelBuilder.ApplyConfiguration(new CssIdempotencyRecordConfiguration());
    }
}

file sealed class CaseConfiguration : IEntityTypeConfiguration<Case>
{
    public void Configure(EntityTypeBuilder<Case> b)
    {
        b.ToTable("Cases", "css");
        b.HasKey(e => e.Id);
        b.Property(e => e.Title).IsRequired().HasMaxLength(512);
        b.Property(e => e.Description).IsRequired().HasMaxLength(8000);
        b.Property(e => e.Status).HasConversion<string>().IsRequired();
        b.Property(e => e.Priority).HasConversion<string>().IsRequired();
        b.Property(e => e.Channel).HasConversion<string>().IsRequired();
        b.Property(e => e.DurableFunctionInstanceId).HasMaxLength(256);

        b.HasIndex(e => new { e.TenantId, e.Status });
        b.HasIndex(e => new { e.TenantId, e.Priority });
        b.HasIndex(e => new { e.TenantId, e.AccountId });
        b.HasIndex(e => new { e.TenantId, e.SlaDeadline })
            .HasFilter("[SlaBreached] = 0 AND [Status] NOT IN ('Resolved','Closed')");

        b.HasMany(e => e.Comments)
            .WithOne()
            .HasForeignKey(c => c.CaseId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(e => e.Escalations)
            .WithOne()
            .HasForeignKey(e => e.CaseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

file sealed class CaseCommentConfiguration : IEntityTypeConfiguration<CaseComment>
{
    public void Configure(EntityTypeBuilder<CaseComment> b)
    {
        b.ToTable("CaseComments", "css");
        b.HasKey(e => e.Id);
        b.Property(e => e.Body).IsRequired().HasMaxLength(16_000);
        b.Property(e => e.AuthorType).HasConversion<string>().IsRequired();

        b.HasIndex(e => new { e.TenantId, e.CaseId, e.CreatedAt });
    }
}

file sealed class EscalationRecordConfiguration : IEntityTypeConfiguration<EscalationRecord>
{
    public void Configure(EntityTypeBuilder<EscalationRecord> b)
    {
        b.ToTable("EscalationRecords", "css");
        b.HasKey(e => e.Id);
        b.Property(e => e.Reason).IsRequired().HasMaxLength(1000);

        b.HasIndex(e => new { e.TenantId, e.CaseId });
    }
}

file sealed class SlaPolicyConfiguration : IEntityTypeConfiguration<SlaPolicy>
{
    public void Configure(EntityTypeBuilder<SlaPolicy> b)
    {
        b.ToTable("SlaPolicies", "css");
        b.HasKey(e => e.Id);
        b.Property(e => e.Name).IsRequired().HasMaxLength(128);
        b.Property(e => e.Priority).HasConversion<string>().IsRequired();

        // One SLA policy per priority per tenant
        b.HasIndex(e => new { e.TenantId, e.Priority }).IsUnique();
    }
}

file sealed class CssIdempotencyRecordConfiguration : IEntityTypeConfiguration<CssIdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<CssIdempotencyRecord> b)
    {
        b.ToTable("IdempotencyStore", "css");
        b.HasKey(e => e.MessageId);
        b.Property(e => e.MessageId).HasMaxLength(200);
    }
}
