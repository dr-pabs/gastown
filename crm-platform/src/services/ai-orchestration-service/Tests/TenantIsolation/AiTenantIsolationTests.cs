using Microsoft.EntityFrameworkCore;
using Moq;
using FluentAssertions;
using Xunit;
using CrmPlatform.AiOrchestrationService.Domain.Entities;
using CrmPlatform.AiOrchestrationService.Domain.Enums;
using CrmPlatform.AiOrchestrationService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;

namespace CrmPlatform.AiOrchestrationService.Tests.TenantIsolation;

[Trait("Category", "TenantIsolation")]
public sealed class AiTenantIsolationTests
{
    // ── Factory ───────────────────────────────────────────────────────────────

    private static AiDbContext CreateContext(string dbName, Guid tenantId)
    {
        var opts = new DbContextOptionsBuilder<AiDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var accessor = new Mock<ITenantContextAccessor>();
        accessor.Setup(a => a.TenantId).Returns(tenantId);
        return new AiDbContext(opts, accessor.Object);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AiJobs_TenantA_CannotSee_TenantB_Jobs()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var dbName  = Guid.NewGuid().ToString();

        // Seed as TenantB
        await using (var seed = CreateContext(dbName, tenantB))
        {
            seed.AiJobs.Add(AiJob.Create(tenantB, CapabilityType.LeadScoring,
                UseCase.LeadCreated, null, "{\"leadId\":\"b\"}"));
            await seed.SaveChangesAsync();
        }

        // Query as TenantA
        await using var query = CreateContext(dbName, tenantA);
        var results = await query.AiJobs.ToListAsync();

        results.Should().BeEmpty("TenantA must not see TenantB's AI jobs.");
    }

    [Fact]
    public async Task AiResults_TenantA_CannotSee_TenantB_Results()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var dbName  = Guid.NewGuid().ToString();

        await using (var seed = CreateContext(dbName, tenantB))
        {
            seed.AiResults.Add(AiResult.Record(
                tenantB, null, CapabilityType.LeadScoring, UseCase.LeadCreated,
                "claude", "prompt", "{\"score\":90}", 100, 50));
            await seed.SaveChangesAsync();
        }

        await using var query = CreateContext(dbName, tenantA);
        var results = await query.AiResults.ToListAsync();

        results.Should().BeEmpty("TenantA must not see TenantB's AI results.");
    }

    [Fact]
    public async Task PromptTemplates_TenantA_CannotSee_TenantB_Templates()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var dbName  = Guid.NewGuid().ToString();

        await using (var seed = CreateContext(dbName, tenantB))
        {
            seed.PromptTemplates.Add(PromptTemplate.Create(
                tenantB, CapabilityType.LeadScoring, UseCase.LeadCreated,
                "TenantB system.", "TenantB user {{x}}."));
            await seed.SaveChangesAsync();
        }

        await using var query = CreateContext(dbName, tenantA);
        var results = await query.PromptTemplates.ToListAsync();

        results.Should().BeEmpty("TenantA must not see TenantB's prompt templates.");
    }

    [Fact]
    public async Task SmsRecords_TenantA_CannotSee_TenantB_Records()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var dbName  = Guid.NewGuid().ToString();

        await using (var seed = CreateContext(dbName, tenantB))
        {
            seed.SmsRecords.Add(SmsRecord.Create(tenantB, "+447700900000", "Hello TenantB!"));
            await seed.SaveChangesAsync();
        }

        await using var query = CreateContext(dbName, tenantA);
        var results = await query.SmsRecords.ToListAsync();

        results.Should().BeEmpty("TenantA must not see TenantB's SMS records.");
    }

    [Fact]
    public async Task TeamsCallRecords_TenantA_CannotSee_TenantB_Records()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var dbName  = Guid.NewGuid().ToString();

        await using (var seed = CreateContext(dbName, tenantB))
        {
            seed.TeamsCallRecords.Add(
                TeamsCallRecord.Create(tenantB, "8:acs:user-b", "{\"lead\":\"b\"}"));
            await seed.SaveChangesAsync();
        }

        await using var query = CreateContext(dbName, tenantA);
        var results = await query.TeamsCallRecords.ToListAsync();

        results.Should().BeEmpty("TenantA must not see TenantB's Teams call records.");
    }

    [Fact]
    public async Task PlatformAdmin_IgnoreQueryFilters_SeesAllTenants()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var dbName  = Guid.NewGuid().ToString();

        // Seed records for both tenants
        await using (var seedA = CreateContext(dbName, tenantA))
        {
            seedA.AiJobs.Add(AiJob.Create(tenantA, CapabilityType.LeadScoring,
                UseCase.LeadCreated, null, "{\"t\":\"a\"}"));
            await seedA.SaveChangesAsync();
        }

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            seedB.AiJobs.Add(AiJob.Create(tenantB, CapabilityType.LeadScoring,
                UseCase.LeadCreated, null, "{\"t\":\"b\"}"));
            await seedB.SaveChangesAsync();
        }

        // PlatformAdmin bypasses query filters
        await using var adminCtx = CreateContext(dbName, tenantA);
        var all = await adminCtx.AiJobs.IgnoreQueryFilters().ToListAsync();

        all.Should().HaveCount(2, "PlatformAdmin with IgnoreQueryFilters should see both tenants.");
    }
}
