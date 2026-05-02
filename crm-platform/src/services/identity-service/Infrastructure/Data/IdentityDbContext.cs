using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using CrmPlatform.IdentityService.Domain.Entities;
using CrmPlatform.ServiceTemplate.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;

namespace CrmPlatform.IdentityService.Infrastructure.Data;

public sealed class IdentityDbContext(
    DbContextOptions<IdentityDbContext> options,
    ITenantContextAccessor tenantContextAccessor)
    : ServiceDbContext(options, tenantContextAccessor)
{
    public DbSet<TenantUser>          TenantUsers         { get; init; } = null!;
    public DbSet<UserRole>            UserRoles           { get; init; } = null!;
    public DbSet<TenantRegistry>      TenantRegistries    { get; init; } = null!;
    public DbSet<ConsentRecord>       ConsentRecords      { get; init; } = null!;
    public DbSet<UserProvisioningLog> UserProvisioningLogs { get; init; } = null!;
    public DbSet<IdempotencyRecord>   IdempotencyRecords  { get; init; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // MUST be first — registers global filters

        modelBuilder.ApplyConfiguration(new TenantUserConfiguration());
        modelBuilder.ApplyConfiguration(new UserRoleConfiguration());
        modelBuilder.ApplyConfiguration(new TenantRegistryConfiguration());
        modelBuilder.ApplyConfiguration(new ConsentRecordConfiguration());
        modelBuilder.ApplyConfiguration(new UserProvisioningLogConfiguration());
        modelBuilder.ApplyConfiguration(new IdempotencyRecordConfiguration());
    }
}

// ─── Entity Configurations ────────────────────────────────────────────────────

file sealed class TenantUserConfiguration : IEntityTypeConfiguration<TenantUser>
{
    public void Configure(EntityTypeBuilder<TenantUser> b)
    {
        b.ToTable("TenantUsers", "identity");
        b.HasKey(e => e.Id);
        b.Property(e => e.EntraObjectId).IsRequired().HasMaxLength(100);
        b.Property(e => e.Email).IsRequired().HasMaxLength(320);
        b.Property(e => e.DisplayName).IsRequired().HasMaxLength(200);
        b.Property(e => e.Status).HasConversion<string>().IsRequired();

        b.HasIndex(e => new { e.TenantId, e.EntraObjectId }).IsUnique();
        b.HasIndex(e => new { e.TenantId, e.Email });

        b.HasMany(e => e.Roles)
            .WithOne(r => r.TenantUser)
            .HasForeignKey(r => r.TenantUserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(e => e.ConsentRecords)
            .WithOne(r => r.TenantUser)
            .HasForeignKey(r => r.TenantUserId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasMany(e => e.ProvisioningLogs)
            .WithOne(r => r.TenantUser)
            .HasForeignKey(r => r.TenantUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

file sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> b)
    {
        b.ToTable("UserRoles", "identity");
        b.HasKey(e => e.Id);
        b.Property(e => e.Role).IsRequired().HasMaxLength(50);
        b.Property(e => e.GrantedBy).IsRequired().HasMaxLength(200);
        b.HasIndex(e => new { e.TenantUserId, e.Role })
            .HasFilter("[IsDeleted] = 0")
            .IsUnique();
    }
}

file sealed class TenantRegistryConfiguration : IEntityTypeConfiguration<TenantRegistry>
{
    public void Configure(EntityTypeBuilder<TenantRegistry> b)
    {
        b.ToTable("TenantRegistries", "identity");
        b.HasKey(e => e.Id);
        b.Property(e => e.Status).HasConversion<string>().IsRequired();
        b.HasIndex(e => e.TenantId).IsUnique();
        b.HasIndex(e => e.EntraTenantId);
    }
}

file sealed class ConsentRecordConfiguration : IEntityTypeConfiguration<ConsentRecord>
{
    public void Configure(EntityTypeBuilder<ConsentRecord> b)
    {
        b.ToTable("ConsentRecords", "identity");
        b.HasKey(e => e.Id);
        b.Property(e => e.ConsentType).HasConversion<string>().IsRequired();
        b.Property(e => e.IpAddressHash).IsRequired().HasMaxLength(64);
        b.HasIndex(e => new { e.TenantUserId, e.ConsentType });
    }
}

file sealed class UserProvisioningLogConfiguration : IEntityTypeConfiguration<UserProvisioningLog>
{
    public void Configure(EntityTypeBuilder<UserProvisioningLog> b)
    {
        b.ToTable("UserProvisioningLogs", "identity");
        b.HasKey(e => e.Id);
        b.Property(e => e.Action).HasConversion<string>().IsRequired();
        b.Property(e => e.InitiatedBy).IsRequired().HasMaxLength(200);
        b.Property(e => e.Details).HasMaxLength(1000);
        b.HasIndex(e => new { e.TenantUserId, e.OccurredAt });
    }
}

file sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> b)
    {
        b.ToTable("IdempotencyStore", "identity");
        b.HasKey(e => e.MessageId);
        b.Property(e => e.MessageId).HasMaxLength(200);
        b.Property(e => e.ProcessedAt).IsRequired();
    }
}
