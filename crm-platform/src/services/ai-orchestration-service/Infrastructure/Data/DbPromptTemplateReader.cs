using Microsoft.EntityFrameworkCore;
using CrmPlatform.AiOrchestrationService.Domain.Entities;
using CrmPlatform.AiOrchestrationService.Domain.Enums;
using CrmPlatform.AiOrchestrationService.Infrastructure.Claude;

namespace CrmPlatform.AiOrchestrationService.Infrastructure.Data;

/// <summary>
/// Reads tenant-custom PromptTemplates from the DB for prompt resolution.
/// </summary>
public sealed class DbPromptTemplateReader(AiDbContext db) : IPromptTemplateReader
{
    public async Task<PromptTemplate?> FindAsync(
        Guid           tenantId,
        CapabilityType capabilityType,
        UseCase        useCase,
        CancellationToken ct = default) =>
        await db.PromptTemplates
            .FirstOrDefaultAsync(p =>
                p.TenantId       == tenantId &&
                p.CapabilityType == capabilityType &&
                p.UseCase        == useCase &&
                !p.IsDeleted,
            ct);
}
