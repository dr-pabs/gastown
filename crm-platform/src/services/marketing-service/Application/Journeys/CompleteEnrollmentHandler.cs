using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CrmPlatform.MarketingService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;

namespace CrmPlatform.MarketingService.Application.Journeys;

public sealed record CompleteEnrollmentCommand(
    Guid EnrollmentId,
    Guid LeadId);   // passed through so sfa-service can match

/// <summary>
/// Called by the journey-orchestrator Durable Function when all steps are done.
/// Publishes journey.completed → consumed by sfa-service to qualify the lead.
/// </summary>
public sealed class CompleteEnrollmentHandler(
    MarketingDbContext db,
    ITenantContext tenantContext,
    ServiceBusEventPublisher publisher,
    ILogger<CompleteEnrollmentHandler> logger)
{
    public async Task<Result<bool>> HandleAsync(
        CompleteEnrollmentCommand command,
        CancellationToken ct = default)
    {
        var enrollment = await db.Enrollments
            .FirstOrDefaultAsync(e => e.Id == command.EnrollmentId, ct);

        if (enrollment is null)
            return Result.Fail<bool>("Enrollment not found.", ResultErrorCode.NotFound);

        try
        {
            enrollment.Complete();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail<bool>(ex.Message, ResultErrorCode.ValidationError);
        }

        await db.SaveChangesAsync(ct);

        foreach (var evt in enrollment.DomainEvents)
            await publisher.PublishAsync("crm.marketing", evt, ct);
        enrollment.ClearDomainEvents();

        logger.LogInformation(
            "Enrollment {EnrollmentId} completed for contact {ContactId}",
            enrollment.Id, enrollment.ContactId);

        return Result.Ok(true);
    }
}
