using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;

/// <summary>
/// Layer 2 auth middleware (ADR 0004).
/// Runs AFTER JWT bearer validation (Layer 1, handled by APIM in production,
/// by JWT middleware in local dev).
///
/// Responsibilities:
///   1. Extract tid (tenantId) + sub (userId) + roles from validated JWT claims.
///   2. Reject requests where tenantId is missing/invalid.
///   3. Populate ITenantContext for use in handlers and DbContext.
///
/// Must be registered AFTER app.UseAuthentication() and app.UseAuthorization().
/// </summary>
public sealed class TenantContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext httpContext, TenantContext tenantContext)
    {
        // Skip health check endpoints — they must be unauthenticated
        if (httpContext.Request.Path.StartsWithSegments("/health"))
        {
            await next(httpContext);
            return;
        }

        var user = httpContext.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await httpContext.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
            return;
        }

        // Extract tenantId from 'tid' claim (Entra ID standard + auth-stub)
        var tidClaim = user.FindFirstValue("tid");
        if (!Guid.TryParse(tidClaim, out var tenantId) || tenantId == Guid.Empty)
        {
            httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            await httpContext.Response.WriteAsJsonAsync(new { error = "Missing or invalid tenant claim" });
            return;
        }

        // Extract userId from 'sub' claim
        var subClaim = user.FindFirstValue(ClaimTypes.NameIdentifier)
                       ?? user.FindFirstValue(JwtClaimTypes.Sub);
        if (!Guid.TryParse(subClaim, out var userId) || userId == Guid.Empty)
        {
            httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            await httpContext.Response.WriteAsJsonAsync(new { error = "Missing or invalid subject claim" });
            return;
        }

        // Extract role — first match wins (roles array or single claim)
        var role = user.FindFirstValue(ClaimTypes.Role)
                   ?? user.FindFirstValue("roles")
                   ?? string.Empty;

        tenantContext.SetFromClaims(tenantId, userId, role);

        await next(httpContext);
    }

    private static class JwtClaimTypes
    {
        public const string Sub = "sub";
    }
}
