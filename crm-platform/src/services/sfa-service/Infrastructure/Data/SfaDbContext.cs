using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using CrmPlatform.SfaService.Domain.Entities;
using CrmPlatform.SfaService.Domain.Enums;
using CrmPlatform.SfaService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;

namespace CrmPlatform.SfaService.Infrastructure.Data;

public sealed class SfaDbContext(
    DbContextOptions<SfaDbContext> options,
    ITenantContextAccessor tenantContextAccessor)
    : ServiceDbContext(options, tenantContextAccessor)
{
    public DbSet<Lead>        Leads        { get; init; } = null!;
    public DbSet<Contact>     Contacts     { get; init; } = null!;
    public DbSet<Account>     Accounts     { get; init; } = null!;
    public DbSet<Opportunity> Opportunities { get; init; } = null!;
    public DbSet<Quote>       Quotes        { get; init; } = null!;
    public DbSet<Activity>    Activities    { get; init; } = null!;
    public DbSet<SfaIdempotencyRecord> IdempotencyRecords { get; init; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // global tenant + soft-delete filters first

        modelBuilder.ApplyConfiguration(new LeadConfiguration());
        modelBuilder.ApplyConfiguration(new ContactConfiguration());
        modelBuilder.ApplyConfiguration(new AccountConfiguration());
        modelBuilder.ApplyConfiguration(new OpportunityConfiguration());
        modelBuilder.ApplyConfiguration(new QuoteConfiguration());
        modelBuilder.ApplyConfiguration(new ActivityConfiguration());
        modelBuilder.ApplyConfiguration(new SfaIdempotencyRecordConfiguration());
    }
}

file sealed class LeadConfiguration : IEntityTypeConfiguration<Lead>
{
    public void Configure(EntityTypeBuilder<Lead> b)
    {
        b.ToTable("Leads", "sfa");
        b.HasKey(e => e.Id);
        b.Property(e => e.Name).IsRequired().HasMaxLength(256);
        b.Property(e => e.Email).IsRequired().HasMaxLength(256);
        b.Property(e => e.Phone).HasMaxLength(50);
        b.Property(e => e.Company).HasMaxLength(256);
        b.Property(e => e.Source).HasConversion<string>().IsRequired();
        b.Property(e => e.Status).HasConversion<string>().IsRequired();
        b.Property(e => e.Score).IsRequired();

        b.HasIndex(e => new { e.TenantId, e.Status });
        b.HasIndex(e => new { e.TenantId, e.AssignedToUserId });
        // Activities are queried by RelatedEntityId — no EF navigation (polymorphic entity)
    }
}

file sealed class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> b)
    {
        b.ToTable("Contacts", "sfa");
        b.HasKey(e => e.Id);
        b.Property(e => e.FirstName).IsRequired().HasMaxLength(128);
        b.Property(e => e.LastName).IsRequired().HasMaxLength(128);
        b.Property(e => e.Email).IsRequired().HasMaxLength(256);
        b.Property(e => e.Phone).HasMaxLength(50);

        b.HasIndex(e => new { e.TenantId, e.Email });

        b.HasOne(e => e.Account)
            .WithMany(a => a.Contacts)
            .HasForeignKey(e => e.AccountId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

file sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> b)
    {
        b.ToTable("Accounts", "sfa");
        b.HasKey(e => e.Id);
        b.Property(e => e.Name).IsRequired().HasMaxLength(256);
        b.Property(e => e.Industry).HasMaxLength(128);
        b.Property(e => e.Size).HasMaxLength(64);
        b.Property(e => e.BillingAddress).HasMaxLength(512);

        b.HasIndex(e => new { e.TenantId, e.Name });
    }
}

file sealed class OpportunityConfiguration : IEntityTypeConfiguration<Opportunity>
{
    public void Configure(EntityTypeBuilder<Opportunity> b)
    {
        b.ToTable("Opportunities", "sfa");
        b.HasKey(e => e.Id);
        b.Property(e => e.Title).IsRequired().HasMaxLength(256);
        b.Property(e => e.Stage).HasConversion<string>().IsRequired();
        b.Property(e => e.Value).HasPrecision(18, 2);

        b.HasIndex(e => new { e.TenantId, e.Stage });

        b.HasOne(e => e.Contact)
            .WithMany(c => c.Opportunities)
            .HasForeignKey(e => e.ContactId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasOne(e => e.Account)
            .WithMany(a => a.Opportunities)
            .HasForeignKey(e => e.AccountId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasMany(e => e.Quotes)
            .WithOne(q => q.Opportunity)
            .HasForeignKey(q => q.OpportunityId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

file sealed class QuoteConfiguration : IEntityTypeConfiguration<Quote>
{
    public void Configure(EntityTypeBuilder<Quote> b)
    {
        b.ToTable("Quotes", "sfa");
        b.HasKey(e => e.Id);
        b.Property(e => e.LineItemsJson).IsRequired().HasMaxLength(8000);
        b.Property(e => e.TotalValue).HasPrecision(18, 2);
        b.Property(e => e.Status).HasConversion<string>().IsRequired();
    }
}

file sealed class ActivityConfiguration : IEntityTypeConfiguration<Activity>
{
    public void Configure(EntityTypeBuilder<Activity> b)
    {
        b.ToTable("Activities", "sfa");
        b.HasKey(e => e.Id);
        b.Property(e => e.ActivityType).HasConversion<string>().IsRequired();
        b.Property(e => e.RelatedEntityType).IsRequired().HasMaxLength(64);
        b.Property(e => e.Notes).HasMaxLength(4000);

        b.HasIndex(e => new { e.TenantId, e.RelatedEntityId, e.OccurredAt });
    }
}

file sealed class SfaIdempotencyRecordConfiguration : IEntityTypeConfiguration<SfaIdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<SfaIdempotencyRecord> b)
    {
        b.ToTable("IdempotencyStore", "sfa");
        b.HasKey(e => e.MessageId);
        b.Property(e => e.MessageId).HasMaxLength(200);
    }
}
