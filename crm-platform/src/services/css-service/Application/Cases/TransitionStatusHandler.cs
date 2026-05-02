using CrmPlatform.CssService.Domain.Enums;
using CrmPlatform.CssService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;

namespace CrmPlatform.CssService.Application.Cases;

public sealed record TransitionStatusCommand(Guid CaseId, CaseStatus NewStatus);

public sealed class TransitionStatusHandler(
    CssDbContext db,
    ServiceBusEventPublisher publisher)
{
    public async Task<Result<bool>> HandleAsync(
        TransitionStatusCommand cmd, CancellationToken ct = default)
    {
        var c = await db.Cases.FirstOrDefaultAsync(x => x.Id == cmd.CaseId, ct);

        if (c is null)
            return Result.Fail<bool>("Case not found.", ResultErrorCode.NotFound);

        try
        {
            switch (cmd.NewStatus)
            {
                case CaseStatus.Open:
                    // Open without SLA (manual open; deadline already set or null)
                    c.Open(c.SlaDeadline ?? DateTime.UtcNow.AddDays(1));
                    break;
                case CaseStatus.Pending:
                    c.SetPending();
                    break;
                case CaseStatus.Resolved:
                    c.Resolve();
                    break;
                case CaseStatus.Closed:
                    c.Close();
                    break;
                default:
                    return Result.Fail<bool>(
                        $"Status {cmd.NewStatus} cannot be set via this endpoint.",
                        ResultErrorCode.ValidationError);
            }
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail<bool>(ex.Message, ResultErrorCode.ValidationError);
        }

        await db.SaveChangesAsync(ct);

        foreach (var evt in c.DomainEvents)
            await publisher.PublishAsync("crm.css", evt, ct);

        return Result.Ok(true);
    }
}
