using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Moq;
using FluentAssertions;
using Xunit;
using CrmPlatform.AiOrchestrationService.Application;
using CrmPlatform.AiOrchestrationService.Api.Dtos;
using CrmPlatform.AiOrchestrationService.Domain.Enums;
using CrmPlatform.AiOrchestrationService.Infrastructure.Claude;
using CrmPlatform.AiOrchestrationService.Infrastructure.Data;
using CrmPlatform.AiOrchestrationService.Infrastructure.Teams;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;

namespace CrmPlatform.AiOrchestrationService.Tests.Application;

public sealed class AiHandlerTests
{
    // ── Shared helpers ────────────────────────────────────────────────────────

    private static AiDbContext CreateDb(string dbName, Guid tenantId)
    {
        var opts = new DbContextOptionsBuilder<AiDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var accessor = new Mock<ITenantContextAccessor>();
        accessor.Setup(a => a.TenantId).Returns(tenantId);
        return new AiDbContext(opts, accessor.Object);
    }

    private static IConfiguration EmptyConfig() =>
        new ConfigurationBuilder().Build();

    // ══════════════════════════════════════════════════════════════════════════
    // AsyncAiJobHandler
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EnqueueSms_CreatesJobInDb()
    {
        var tenantId  = Guid.NewGuid();
        var dbName    = Guid.NewGuid().ToString();
        using var db  = CreateDb(dbName, tenantId);

        var accessor  = new Mock<ITenantContextAccessor>();
        accessor.Setup(a => a.TenantId).Returns(tenantId);

        var handler = new AsyncAiJobHandler(db, accessor.Object,
            NullLogger<AsyncAiJobHandler>.Instance);

        var req = new SmsComposeRequest("+447700900000", "+447700111111",
            "Spring Sale", "Drive conversions");

        var result = await handler.EnqueueSmsAsync(req, Guid.NewGuid(), CancellationToken.None);

        result.CapabilityType.Should().Be(CapabilityType.SmsComposition);
        result.Status.Should().Be(AiJobStatus.Queued);

        using var verify = CreateDb(dbName, tenantId);
        (await verify.AiJobs.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task EnqueueCaseSummarisation_CreatesJobWithOnDemandUseCase()
    {
        var tenantId = Guid.NewGuid();
        var dbName   = Guid.NewGuid().ToString();
        using var db = CreateDb(dbName, tenantId);

        var accessor = new Mock<ITenantContextAccessor>();
        accessor.Setup(a => a.TenantId).Returns(tenantId);

        var handler = new AsyncAiJobHandler(db, accessor.Object,
            NullLogger<AsyncAiJobHandler>.Instance);

        var caseId = Guid.NewGuid();
        var req    = new CaseSummarisationRequest(caseId, "Customer reported an issue...");

        var result = await handler.EnqueueCaseSummarisationAsync(req, null, CancellationToken.None);

        result.UseCase.Should().Be(UseCase.CaseOnDemand);
        result.CapabilityType.Should().Be(CapabilityType.CaseSummarisation);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // PromptManagementHandler
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UpsertPromptTemplate_CreatesNewWhenNoneExists()
    {
        var tenantId = Guid.NewGuid();
        var dbName   = Guid.NewGuid().ToString();
        using var db = CreateDb(dbName, tenantId);

        var accessor = new Mock<ITenantContextAccessor>();
        accessor.Setup(a => a.TenantId).Returns(tenantId);

        var handler = new PromptManagementHandler(db, accessor.Object,
            NullLogger<PromptManagementHandler>.Instance);

        var req = new UpsertPromptTemplateRequest(
            CapabilityType.LeadScoring, UseCase.LeadCreated,
            "System: you are a scorer.", "Score {{leadData}}.");

        var result = await handler.UpsertAsync(req, CancellationToken.None);

        result.Id.Should().NotBeEmpty();
        result.SystemPrompt.Should().Contain("scorer");

        using var verify = CreateDb(dbName, tenantId);
        (await verify.PromptTemplates.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task UpsertPromptTemplate_UpdatesExistingTemplate()
    {
        var tenantId = Guid.NewGuid();
        var dbName   = Guid.NewGuid().ToString();

        // Seed
        using (var seedDb = CreateDb(dbName, tenantId))
        {
            var handler = new PromptManagementHandler(seedDb,
                MockAccessor(tenantId).Object,
                NullLogger<PromptManagementHandler>.Instance);

            await handler.UpsertAsync(new UpsertPromptTemplateRequest(
                CapabilityType.LeadScoring, UseCase.LeadCreated,
                "Old system.", "Old user."), CancellationToken.None);
        }

        // Update
        using (var updateDb = CreateDb(dbName, tenantId))
        {
            var handler = new PromptManagementHandler(updateDb,
                MockAccessor(tenantId).Object,
                NullLogger<PromptManagementHandler>.Instance);

            var result = await handler.UpsertAsync(new UpsertPromptTemplateRequest(
                CapabilityType.LeadScoring, UseCase.LeadCreated,
                "New system.", "New user {{x}}."), CancellationToken.None);

            result.SystemPrompt.Should().Be("New system.");
        }

        // Should still be 1 record
        using var verify = CreateDb(dbName, tenantId);
        (await verify.PromptTemplates.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task DeletePromptTemplate_SetsIsDeleted()
    {
        var tenantId = Guid.NewGuid();
        var dbName   = Guid.NewGuid().ToString();
        Guid templateId;

        // Seed
        using (var seedDb = CreateDb(dbName, tenantId))
        {
            var handler = new PromptManagementHandler(seedDb,
                MockAccessor(tenantId).Object,
                NullLogger<PromptManagementHandler>.Instance);

            var r = await handler.UpsertAsync(new UpsertPromptTemplateRequest(
                CapabilityType.LeadScoring, UseCase.LeadCreated,
                "System.", "User."), CancellationToken.None);
            templateId = r.Id;
        }

        // Delete
        using (var delDb = CreateDb(dbName, tenantId))
        {
            var handler = new PromptManagementHandler(delDb,
                MockAccessor(tenantId).Object,
                NullLogger<PromptManagementHandler>.Instance);
            await handler.DeleteAsync(templateId, CancellationToken.None);
        }

        // Verify deleted — List returns empty
        using var verify = CreateDb(dbName, tenantId);
        var listHandler = new PromptManagementHandler(verify,
            MockAccessor(tenantId).Object,
            NullLogger<PromptManagementHandler>.Instance);
        var list = await listHandler.ListAsync(CancellationToken.None);
        list.Should().BeEmpty();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AiReadHandler
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetJob_ReturnsNullForUnknownId()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateDb(Guid.NewGuid().ToString(), tenantId);

        var reader = new AiReadHandler(db, MockAccessor(tenantId).Object);
        var result = await reader.GetJobAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ListJobs_FiltersByStatus()
    {
        var tenantId = Guid.NewGuid();
        var dbName   = Guid.NewGuid().ToString();

        // Seed: 1 Queued, 1 Succeeded
        using (var seedDb = CreateDb(dbName, tenantId))
        {
            var j1 = Domain.Entities.AiJob.Create(tenantId, CapabilityType.LeadScoring,
                UseCase.LeadCreated, null, "{\"x\":1}");
            var j2 = Domain.Entities.AiJob.Create(tenantId, CapabilityType.EmailDraft,
                UseCase.EmailDraftLeadAssigned, null, "{\"x\":2}");
            j2.MarkInProgress();
            j2.MarkSucceeded();
            seedDb.AiJobs.AddRange(j1, j2);
            await seedDb.SaveChangesAsync();
        }

        using var db     = CreateDb(dbName, tenantId);
        var       reader = new AiReadHandler(db, MockAccessor(tenantId).Object);

        var result = await reader.ListJobsAsync(
            AiJobStatus.Queued, null, 1, 25, CancellationToken.None);

        result.Items.Should().HaveCount(1);
        result.Items[0].Status.Should().Be(AiJobStatus.Queued);
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static Mock<ITenantContextAccessor> MockAccessor(Guid tenantId)
    {
        var m = new Mock<ITenantContextAccessor>();
        m.Setup(a => a.TenantId).Returns(tenantId);
        return m;
    }
}
