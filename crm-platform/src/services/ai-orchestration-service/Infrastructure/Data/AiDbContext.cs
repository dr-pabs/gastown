using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using CrmPlatform.AiOrchestrationService.Domain.Entities;
using CrmPlatform.AiOrchestrationService.Domain.Enums;
using CrmPlatform.ServiceTemplate.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;

namespace CrmPlatform.AiOrchestrationService.Infrastructure.Data;

public sealed class AiDbContext(
    DbContextOptions<AiDbContext> options,
    ITenantContextAccessor tenantContextAccessor)
    : ServiceDbContext(options, tenantContextAccessor)
{
    public DbSet<AiJob>               AiJobs             { get; init; } = null!;
    public DbSet<AiResult>            AiResults          { get; init; } = null!;
    public DbSet<PromptTemplate>      PromptTemplates    { get; init; } = null!;
    public DbSet<SmsRecord>           SmsRecords         { get; init; } = null!;
    public DbSet<TeamsCallRecord>     TeamsCallRecords   { get; init; } = null!;
    public DbSet<AiIdempotencyRecord> IdempotencyRecords { get; init; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // global tenant + soft-delete query filters

        modelBuilder.ApplyConfiguration(new AiJobConfiguration());
        modelBuilder.ApplyConfiguration(new AiResultConfiguration());
        modelBuilder.ApplyConfiguration(new PromptTemplateConfiguration());
        modelBuilder.ApplyConfiguration(new SmsRecordConfiguration());
        modelBuilder.ApplyConfiguration(new TeamsCallRecordConfiguration());
        modelBuilder.ApplyConfiguration(new AiIdempotencyRecordConfiguration());
    }
}

// ── Entity type configurations ────────────────────────────────────────────────

file sealed class AiJobConfiguration : IEntityTypeConfiguration<AiJob>
{
    public void Configure(EntityTypeBuilder<AiJob> b)
    {
        b.ToTable("AiJobs", "ai");
        b.HasKey(e => e.Id);

        b.Property(e => e.CapabilityType).HasConversion<string>().IsRequired().HasMaxLength(64);
        b.Property(e => e.UseCase).HasConversion<string>().IsRequired().HasMaxLength(64);
        b.Property(e => e.Status).HasConversion<string>().IsRequired().HasMaxLength(32);
        b.Property(e => e.InputPayload).IsRequired().HasColumnType("nvarchar(max)");
        b.Property(e => e.FailureReason).HasMaxLength(1024);

        b.HasIndex(e => new { e.TenantId, e.Status });
        b.HasIndex(e => new { e.TenantId, e.CapabilityType });
        b.HasIndex(e => new { e.Status, e.NextRetryAt });
        b.HasIndex(e => e.CreatedAt);
    }
}

file sealed class AiResultConfiguration : IEntityTypeConfiguration<AiResult>
{
    public void Configure(EntityTypeBuilder<AiResult> b)
    {
        b.ToTable("AiResults", "ai");
        b.HasKey(e => e.Id);

        b.Property(e => e.CapabilityType).HasConversion<string>().IsRequired().HasMaxLength(64);
        b.Property(e => e.UseCase).HasConversion<string>().IsRequired().HasMaxLength(64);
        b.Property(e => e.ModelName).IsRequired().HasMaxLength(128);
        b.Property(e => e.PromptUsed).IsRequired().HasColumnType("nvarchar(max)");
        b.Property(e => e.OutputContent).IsRequired().HasColumnType("nvarchar(max)");

        b.HasIndex(e => new { e.TenantId, e.CapabilityType });
        b.HasIndex(e => e.JobId);
        b.HasIndex(e => e.RecordedAt);
    }
}

file sealed class PromptTemplateConfiguration : IEntityTypeConfiguration<PromptTemplate>
{
    public void Configure(EntityTypeBuilder<PromptTemplate> b)
    {
        b.ToTable("PromptTemplates", "ai");
        b.HasKey(e => e.Id);

        b.Property(e => e.CapabilityType).HasConversion<string>().IsRequired().HasMaxLength(64);
        b.Property(e => e.UseCase).HasConversion<string>().IsRequired().HasMaxLength(64);
        b.Property(e => e.SystemPrompt).IsRequired().HasColumnType("nvarchar(max)");
        b.Property(e => e.UserPromptTemplate).IsRequired().HasColumnType("nvarchar(max)");

        // One template per (tenant, capability, use-case)
        b.HasIndex(e => new { e.TenantId, e.CapabilityType, e.UseCase }).IsUnique();
    }
}

file sealed class SmsRecordConfiguration : IEntityTypeConfiguration<SmsRecord>
{
    public void Configure(EntityTypeBuilder<SmsRecord> b)
    {
        b.ToTable("SmsRecords", "ai");
        b.HasKey(e => e.Id);

        b.Property(e => e.RecipientPhone).IsRequired().HasMaxLength(32);
        b.Property(e => e.ComposedMessage).IsRequired().HasColumnType("nvarchar(max)");
        b.Property(e => e.AcsMessageId).HasMaxLength(256);
        b.Property(e => e.FailureReason).HasMaxLength(1024);

        b.HasIndex(e => new { e.TenantId, e.CreatedAt });
        b.HasIndex(e => e.JobId);
    }
}

file sealed class TeamsCallRecordConfiguration : IEntityTypeConfiguration<TeamsCallRecord>
{
    public void Configure(EntityTypeBuilder<TeamsCallRecord> b)
    {
        b.ToTable("TeamsCallRecords", "ai");
        b.HasKey(e => e.Id);

        b.Property(e => e.TargetUserId).IsRequired().HasMaxLength(256);
        b.Property(e => e.CallContext).IsRequired().HasColumnType("nvarchar(max)");
        b.Property(e => e.AcsCallId).HasMaxLength(256);
        b.Property(e => e.TranscriptText).HasColumnType("nvarchar(max)");

        b.HasIndex(e => new { e.TenantId, e.CreatedAt });
        b.HasIndex(e => e.TargetUserId);
    }
}

file sealed class AiIdempotencyRecordConfiguration : IEntityTypeConfiguration<AiIdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<AiIdempotencyRecord> b)
    {
        b.ToTable("IdempotencyRecords", "ai");
        b.HasKey(e => e.MessageId);
        b.Property(e => e.MessageId).IsRequired().HasMaxLength(512);
    }
}
