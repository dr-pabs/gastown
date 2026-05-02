// ============================================================
//  CRM Platform — Service Template Entry Point
//  Copy this Program.cs into every new service and:
//    1. Replace "ServiceTemplate" with the service name (e.g., "sfa-service")
//    2. Register service-specific DbContext and repositories
//    3. Register service-specific Service Bus consumers
//    4. Register service-specific minimal API routes
// ============================================================
using CrmPlatform.ServiceTemplate.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ─── DEV SAFETY: validate environment config ─────────────────────────────────
// Services may start before APIM is available in local dev. Guard here.
var devAuthMode = builder.Configuration["DevAuthMode"] ?? "entra";
if (devAuthMode == "stub" && builder.Environment.IsProduction())
{
    Console.Error.WriteLine("FATAL: DevAuthMode=stub is not allowed in Production.");
    return 1;
}

// ─── Core services ────────────────────────────────────────────────────────────
builder.Services.AddCrmService(
    builder.Configuration,
    builder.Environment,
    serviceName: "service-template");  // ← replace with actual service name

// ─── TODO: Register service-specific DbContext ────────────────────────────────
// builder.Services.AddDbContext<ServiceNameDbContext>((sp, options) =>
// {
//     options.UseSqlServer(builder.Configuration.GetConnectionString("Default"));
//     options.AddInterceptors(
//         sp.GetRequiredService<TenantSessionContextInterceptor>(),
//         new AuditInterceptor());
// });

// ─── TODO: Register service-specific consumers ────────────────────────────────
// builder.Services.AddHostedService<SomeEventConsumer>();

var app = builder.Build();

// ─── Middleware pipeline ──────────────────────────────────────────────────────
app.UseCrmService(); // Health, auth, tenant context

// ─── TODO: Register minimal API routes ───────────────────────────────────────
// app.MapGroup("/api").MapSfaEndpoints().RequireAuthorization();

app.Run();
return 0;
