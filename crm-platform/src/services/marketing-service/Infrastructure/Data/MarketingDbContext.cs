using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using CrmPlatform.MarketingService.Domain.Entities;
using CrmPlatform.ServiceTemplate.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;

namespace CrmPlatform.MarketingService.Infrastructure.Data;

public sealed class MarketingDbContext(
    DbContextOptions<MarketingDbContext> options,
    ITenantContextAccessor tenantContextAccessor)
    : ServiceDbContext(options, tenantContextAccessor)
{
    public DbSet<Campaign>          Campaigns    { get; init; } = null!;
    public DbSet<Journey>           Journeys     { get; init; } = null!;
    public DbSet<JourneyEnrollment> Enrollments  { get; init; } = null!;
    public DbSet<EmailTemplate>     EmailTemplates { get; init; } = null!;
    public DbSet<MarketingIdempotencyRecord> IdempotencyRecords { get; init; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // global tenant + soft-delete filters first

        modelBuilder.ApplyConfiguration(new CampaignConfiguration());
        modelBuilder.ApplyConfiguration(new JourneyConfiguration());
        modelBuilder.ApplyConfiguration(new JourneyEnrollmentConfiguration());
        modelBuilder.ApplyConfiguration(new EmailTemplateConfiguration());
        modelBuilder.ApplyConfiguration(new MarketingIdempotencyRecordConfiguration());
    }
}

file sealed class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> b)
    {
        b.ToTable("Campaigns", "marketing");
        b.HasKey(e => e.Id);
        b.Property(e => e.Name).IsRequired().HasMaxLength(256);
        b.Property(e => e.Description).HasMaxLength(2000);
        b.Property(e => e.Channel).HasConversion<string>().IsRequired();
        b.Property(e => e.Status).HasConversion<string>().IsRequired();

        b.HasIndex(e => new { e.TenantId, e.Status });
        b.HasIndex(e => new { e.TenantId, e.CreatedByUserId });

        b.HasMany(e => e.Journeys)
            .WithOne(j => j.Campaign)
            .HasForeignKey(j => j.CampaignId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

file sealed class JourneyConfiguration : IEntityTypeConfiguration<Journey>
{
    public void Configure(EntityTypeBuilder<Journey> b)
    {
        b.ToTable("Journeys", "marketing");
        b.HasKey(e => e.Id);
        b.Property(e => e.Name).IsRequired().HasMaxLength(256);
        b.Property(e => e.Description).HasMaxLength(2000);
        b.Property(e => e.StepsJson).IsRequired().HasMaxLength(65_536);

        b.HasIndex(e => new { e.TenantId, e.CampaignId });
        b.HasIndex(e => new { e.TenantId, e.IsPublished });

        b.HasMany(e => e.Enrollments)
            .WithOne(e => e.Journey)
            .HasForeignKey(e => e.JourneyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

file sealed class JourneyEnrollmentConfiguration : IEntityTypeConfiguration<JourneyEnrollment>
{
    public void Configure(EntityTypeBuilder<JourneyEnrollment> b)
    {
        b.ToTable("JourneyEnrollments", "marketing");
        b.HasKey(e => e.Id);
        b.Property(e => e.Status).HasConversion<string>().IsRequired();
        b.Property(e => e.ExitReason).HasMaxLength(512);
        b.Property(e => e.DurableFunctionInstanceId).HasMaxLength(256);

        // Each contact can only be enrolled once per journey (active enrollment)
        b.HasIndex(e => new { e.TenantId, e.JourneyId, e.ContactId }).IsUnique();
        b.HasIndex(e => new { e.TenantId, e.Status });
    }
}

file sealed class EmailTemplateConfiguration : IEntityTypeConfiguration<EmailTemplate>
{
    public void Configure(EntityTypeBuilder<EmailTemplate> b)
    {
        b.ToTable("EmailTemplates", "marketing");
        b.HasKey(e => e.Id);
        b.Property(e => e.Name).IsRequired().HasMaxLength(256);
        b.Property(e => e.Subject).IsRequired().HasMaxLength(512);
        b.Property(e => e.HtmlBody).IsRequired().HasMaxLength(1_000_000);
        b.Property(e => e.PlainTextBody).HasMaxLength(500_000);
        b.Property(e => e.Engine).HasConversion<string>().IsRequired();

        b.HasIndex(e => new { e.TenantId, e.Name, e.Version }).IsUnique();
    }
}

file sealed class MarketingIdempotencyRecordConfiguration : IEntityTypeConfiguration<MarketingIdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<MarketingIdempotencyRecord> b)
    {
        b.ToTable("IdempotencyStore", "marketing");
        b.HasKey(e => e.MessageId);
        b.Property(e => e.MessageId).HasMaxLength(200);
    }
}
