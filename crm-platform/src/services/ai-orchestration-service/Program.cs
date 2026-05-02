using CrmPlatform.AiOrchestrationService.Api;
using CrmPlatform.AiOrchestrationService.Application;
using CrmPlatform.AiOrchestrationService.Infrastructure.Claude;
using CrmPlatform.AiOrchestrationService.Infrastructure.Data;
using CrmPlatform.AiOrchestrationService.Infrastructure.Messaging;
using CrmPlatform.AiOrchestrationService.Infrastructure.Sms;
using CrmPlatform.AiOrchestrationService.Infrastructure.Teams;
using CrmPlatform.AiOrchestrationService.Infrastructure.Workers;
using CrmPlatform.ServiceTemplate.Infrastructure;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using Hellang.Middleware.ProblemDetails;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ─── Platform bootstrap (auth, health, OTel, SB client) ──────────────────────
builder.Services.AddCrmService(builder.Configuration, builder.Environment, "ai-orchestration-service");

// ─── Database ─────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<AiDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Default"),
        sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "ai")));

// ─── Idempotency ──────────────────────────────────────────────────────────────
builder.Services.AddScoped<IIdempotencyStore, AiIdempotencyStore>();

// ─── Prompt resolution ────────────────────────────────────────────────────────
builder.Services.AddScoped<IPromptTemplateReader, DbPromptTemplateReader>();
builder.Services.AddScoped<IPromptResolver, PromptResolver>();

// ─── Claude AI client ─────────────────────────────────────────────────────────
builder.Services.AddScoped<IClaudeClient, ClaudeClient>();

// ─── ACS SMS client ───────────────────────────────────────────────────────────
builder.Services.AddScoped<IAiSmsClient, AiSmsClient>();

// ─── Teams clients ────────────────────────────────────────────────────────────
builder.Services.AddHttpClient<ITeamsNotificationClient, TeamsNotificationClient>();
builder.Services.AddScoped<ITeamsCallingClient, TeamsCallingClient>();

// ─── Application handlers ─────────────────────────────────────────────────────
builder.Services.AddScoped<SyncAiHandler>();
builder.Services.AddScoped<AsyncAiJobHandler>();
builder.Services.AddScoped<PromptManagementHandler>();
builder.Services.AddScoped<AiReadHandler>();

// ─── Service Bus consumers ────────────────────────────────────────────────────
builder.Services.Configure<ServiceBusConsumerOptions>(
    builder.Configuration.GetSection("ServiceBus:ConsumerOptions"));

builder.Services.AddSingleton<LeadCreatedConsumer>();
builder.Services.AddSingleton<LeadAssignedConsumer>();
builder.Services.AddSingleton<OpportunityStageChangedConsumer>();
builder.Services.AddSingleton<CaseResolvedConsumer>();
builder.Services.AddSingleton<CaseCommentAddedConsumer>();
builder.Services.AddSingleton<JourneyEnrollmentCreatedConsumer>();
builder.Services.AddSingleton<TenantProvisionedConsumer>();
builder.Services.AddSingleton<TenantSuspendedConsumer>();

builder.Services.AddHostedService<ConsumerHostedService<LeadCreatedConsumer>>();
builder.Services.AddHostedService<ConsumerHostedService<LeadAssignedConsumer>>();
builder.Services.AddHostedService<ConsumerHostedService<OpportunityStageChangedConsumer>>();
builder.Services.AddHostedService<ConsumerHostedService<CaseResolvedConsumer>>();
builder.Services.AddHostedService<ConsumerHostedService<CaseCommentAddedConsumer>>();
builder.Services.AddHostedService<ConsumerHostedService<JourneyEnrollmentCreatedConsumer>>();
builder.Services.AddHostedService<ConsumerHostedService<TenantProvisionedConsumer>>();
builder.Services.AddHostedService<ConsumerHostedService<TenantSuspendedConsumer>>();

// ─── Background AI job worker ─────────────────────────────────────────────────
builder.Services.AddHostedService<AiJobWorker>();

// ─── Authorization policies ───────────────────────────────────────────────────
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("TenantAdminOrAiPromptEditor", policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim("role", "TenantAdmin") ||
            ctx.User.HasClaim("permission", "AiPromptEditor")));
});

// ─── Problem Details (RFC 7807) ───────────────────────────────────────────────
builder.Services.AddProblemDetails(opts =>
{
    opts.IncludeExceptionDetails = (ctx, _) => ctx.RequestServices
        .GetRequiredService<IHostEnvironment>().IsDevelopment();
});

// ─── OpenAPI / Swagger ────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "CRM AI Orchestration Service", Version = "v1" });
});

var app = builder.Build();

// ─── Middleware pipeline ──────────────────────────────────────────────────────
app.UseProblemDetails();
app.UseStaticFiles();          // Serves wwwroot/.well-known/ai-plugin.json
app.UseCrmService();           // health + auth + TenantContextMiddleware

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Serve the Copilot plugin OpenAPI spec filtered to Copilot-scoped endpoints
    app.MapGet("/ai/openapi.json", (IHostEnvironment env) =>
        Results.File(
            Path.Combine(env.ContentRootPath, "wwwroot", "ai-openapi.json"),
            "application/json"));
}
else
{
    app.MapGet("/ai/openapi.json", (IHostEnvironment env) =>
        Results.File(
            Path.Combine(env.ContentRootPath, "wwwroot", "ai-openapi.json"),
            "application/json"));
}

// ─── Endpoints ────────────────────────────────────────────────────────────────
app.MapAiEndpoints();

app.Run();

// Required for integration test WebApplicationFactory
public partial class Program { }
