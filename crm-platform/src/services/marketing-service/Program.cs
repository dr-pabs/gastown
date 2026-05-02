using CrmPlatform.MarketingService.Api;
using CrmPlatform.MarketingService.Application.Campaigns;
using CrmPlatform.MarketingService.Application.Journeys;
using CrmPlatform.MarketingService.Application.Templates;
using CrmPlatform.MarketingService.Infrastructure.Data;
using CrmPlatform.MarketingService.Infrastructure.Messaging;
using CrmPlatform.ServiceTemplate.Infrastructure;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using Hellang.Middleware.ProblemDetails;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ─── Platform bootstrap (auth, health, OTel, SB client) ──────────────────────
builder.Services.AddCrmService(builder.Configuration, builder.Environment, "marketing-service");

// ─── Database ─────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<MarketingDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Default"),
        sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "marketing")));

// ─── Idempotency store ────────────────────────────────────────────────────────
builder.Services.AddScoped<IIdempotencyStore, MarketingIdempotencyStore>();

// ─── Application handlers ─────────────────────────────────────────────────────
builder.Services.AddScoped<CreateCampaignHandler>();
builder.Services.AddScoped<TransitionCampaignHandler>();
builder.Services.AddScoped<CreateJourneyHandler>();
builder.Services.AddScoped<PublishJourneyHandler>();
builder.Services.AddScoped<EnrollContactHandler>();
builder.Services.AddScoped<CompleteEnrollmentHandler>();
builder.Services.AddScoped<CreateEmailTemplateHandler>();

// ─── Service Bus consumers (hosted services) ──────────────────────────────────
builder.Services.Configure<ServiceBusConsumerOptions>(
    builder.Configuration.GetSection("ServiceBus:ConsumerOptions"));

builder.Services.AddSingleton<TenantSuspendedConsumer>();

builder.Services.AddHostedService<ConsumerHostedService<TenantSuspendedConsumer>>();

// ─── Problem Details (RFC 7807) ───────────────────────────────────────────────
builder.Services.AddProblemDetails(opts =>
{
    opts.IncludeExceptionDetails = (ctx, _) => ctx.RequestServices
        .GetRequiredService<IHostEnvironment>().IsDevelopment();
});

// ─── Memory cache ─────────────────────────────────────────────────────────────
builder.Services.AddMemoryCache();

// ─── OpenAPI / Swagger ────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ─── Middleware pipeline ──────────────────────────────────────────────────────
app.UseProblemDetails();
app.UseCrmService(); // health checks + auth + TenantContextMiddleware

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ─── Endpoints ────────────────────────────────────────────────────────────────
app.MapMarketingEndpoints();
app.MapMarketingInternalEndpoints();

app.Run();
