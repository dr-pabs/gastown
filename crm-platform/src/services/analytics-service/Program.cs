using CrmPlatform.AnalyticsService.Api;
using CrmPlatform.AnalyticsService.Application;
using CrmPlatform.AnalyticsService.Infrastructure.Data;
using CrmPlatform.AnalyticsService.Infrastructure.Messaging;
using CrmPlatform.ServiceTemplate.Infrastructure;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using Hellang.Middleware.ProblemDetails;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ─── Platform bootstrap (auth, health, OTel, SB client) ──────────────────────
builder.Services.AddCrmService(builder.Configuration, builder.Environment, "analytics-service");

// ─── Database ─────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<AnalyticsDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Default"),
        sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "analytics")));

// ─── Idempotency store ────────────────────────────────────────────────────────
builder.Services.AddScoped<IIdempotencyStore, AnalyticsIdempotencyStore>();

// ─── Application handlers ─────────────────────────────────────────────────────
builder.Services.AddScoped<MetricQueryHandler>();

// ─── Background services ──────────────────────────────────────────────────────
builder.Services.AddHostedService<MetricRollupService>();

// ─── Service Bus consumers — one per topic ────────────────────────────────────
builder.Services.Configure<ServiceBusConsumerOptions>(
    builder.Configuration.GetSection("ServiceBus:ConsumerOptions"));

builder.Services.AddSingleton<SfaEventConsumer>();
builder.Services.AddSingleton<CssEventConsumer>();
builder.Services.AddSingleton<MarketingEventConsumer>();
builder.Services.AddSingleton<PlatformEventConsumer>();

builder.Services.AddHostedService<ConsumerHostedService<SfaEventConsumer>>();
builder.Services.AddHostedService<ConsumerHostedService<CssEventConsumer>>();
builder.Services.AddHostedService<ConsumerHostedService<MarketingEventConsumer>>();
builder.Services.AddHostedService<ConsumerHostedService<PlatformEventConsumer>>();

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
app.MapAnalyticsEndpoints();

app.Run();
