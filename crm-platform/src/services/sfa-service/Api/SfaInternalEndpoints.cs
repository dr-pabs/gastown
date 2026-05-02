using CrmPlatform.SfaService.Application.Leads;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace CrmPlatform.SfaService.Api;

/// <summary>
/// Internal endpoints called only by Durable Functions (lead-score-decay).
/// Not exposed via API gateway — internal network only.
/// </summary>
public static class SfaInternalEndpoints
{
    public static IEndpointRouteBuilder MapSfaInternalEndpoints(this IEndpointRouteBuilder app)
    {
        var internal_ = app.MapGroup("/internal");

        // ─── POST /internal/leads/decay-scores ────────────────────────────────
        // Called daily by the lead-score-decay Durable Function.
        // Delegates to the existing ScoreDecayService logic.
        internal_.MapPost("/leads/decay-scores", async (
            DecayTriggeredRequest req,
            ScoreDecayHandler handler,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger(typeof(SfaInternalEndpoints));
            logger.LogInformation(
                "Lead score decay triggered externally at {TriggeredAt:u}", req.TriggeredAt);

            var result = await handler.HandleAsync();

            return result.IsSuccess
                ? Results.Ok(new DecayResultResponse(result.Value))
                : result.ToHttpResult();
        });

        return app;
    }
}

public sealed record DecayTriggeredRequest(DateTime TriggeredAt);
public sealed record DecayResultResponse(int DecayedLeadCount);
