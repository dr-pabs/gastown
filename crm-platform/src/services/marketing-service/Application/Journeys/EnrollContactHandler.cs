using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CrmPlatform.MarketingService.Domain.Entities;
using CrmPlatform.MarketingService.Domain.Enums;
using CrmPlatform.MarketingService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;

namespace CrmPlatform.MarketingService.Application.Journeys;

public sealed record EnrollContactCommand(
    Guid JourneyId,
    Guid ContactId);

public sealed record EnrollContactResult(Guid EnrollmentId);

public sealed class EnrollContactHandler(
    MarketingDbContext db,
    ITenantContext tenantContext,
    ServiceBusEventPublisher publisher,
    ILogger<EnrollContactHandler> logger)
{
    public async Task<Result<EnrollContactResult>> HandleAsync(
        EnrollContactCommand command,
        CancellationToken ct = default)
    {
        var journey = await db.Journeys
            .FirstOrDefaultAsync(j => j.Id == command.JourneyId, ct);

        if (journey is null)
            return Result.Fail<EnrollContactResult>("Journey not found.", ResultErrorCode.NotFound);

        if (!journey.IsPublished)
            return Result.Fail<EnrollContactResult>(
                "Cannot enroll into an unpublished journey.", ResultErrorCode.ValidationError);

        // Prevent duplicate active enrollment
        var existing = await db.Enrollments
            .AnyAsync(e =>
                e.JourneyId  == command.JourneyId
                && e.ContactId == command.ContactId
                && e.Status    == EnrollmentStatus.Active,
                ct);

        if (existing)
            return Result.Fail<EnrollContactResult>(
                "Contact already has an active enrollment in this journey.", ResultErrorCode.Conflict);

        var enrollment = JourneyEnrollment.Create(
            tenantContext.TenantId,
            command.JourneyId,
            command.ContactId);

        db.Enrollments.Add(enrollment);
        await db.SaveChangesAsync(ct);

        // Publish enrollment created event — the Durable Function orchestrator
        // picks this up to start the journey engine for this enrollment.
        foreach (var evt in enrollment.DomainEvents)
            await publisher.PublishAsync("crm.marketing", evt, ct);
        enrollment.ClearDomainEvents();

        logger.LogInformation(
            "Contact {ContactId} enrolled in journey {JourneyId}, enrollment {EnrollmentId}",
            command.ContactId, command.JourneyId, enrollment.Id);

        return Result.Ok(new EnrollContactResult(enrollment.Id));
    }
}
