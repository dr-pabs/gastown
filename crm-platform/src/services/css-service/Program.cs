using CrmPlatform.CssService.Api;
using CrmPlatform.CssService.Application.Cases;
using CrmPlatform.CssService.Application.Sla;
using CrmPlatform.CssService.Infrastructure.Data;
using CrmPlatform.CssService.Infrastructure.Messaging;
using CrmPlatform.ServiceTemplate.Infrastructure;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using Hellang.Middleware.ProblemDetails;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ─── Platform bootstrap (auth, health, OTel, SB client) ──────────────────────
builder.Services.AddCrmService(builder.Configuration, builder.Environment, "css-service");

// ─── Database ─────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<CssDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Default"),
        sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "css")));

// ─── Idempotency store ────────────────────────────────────────────────────────
builder.Services.AddScoped<IIdempotencyStore, CssIdempotencyStore>();

// ─── Application handlers ─────────────────────────────────────────────────────
builder.Services.AddScoped<CreateCaseHandler>();
builder.Services.AddScoped<TransitionStatusHandler>();
builder.Services.AddScoped<AssignCaseHandler>();
builder.Services.AddScoped<EscalateCaseHandler>();
builder.Services.AddScoped<AddCommentHandler>();
builder.Services.AddScoped<CloseCaseHandler>();
builder.Services.AddScoped<CreateSlaPolicyHandler>();

// ─── Background services ──────────────────────────────────────────────────────
builder.Services.AddHostedService<SlaMonitorService>();

// ─── Service Bus consumers (hosted services) ──────────────────────────────────
builder.Services.Configure<ServiceBusConsumerOptions>(
    builder.Configuration.GetSection("ServiceBus:ConsumerOptions"));

builder.Services.AddSingleton<TenantSuspendedConsumer>();
builder.Services.AddSingleton<OpportunityWonConsumer>();

builder.Services.AddHostedService<ConsumerHostedService<TenantSuspendedConsumer>>();
builder.Services.AddHostedService<ConsumerHostedService<OpportunityWonConsumer>>();

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
app.MapCssEndpoints();
app.MapCssInternalEndpoints();

app.Run();

// ─── Consumer hosted service wrapper ─────────────────────────────────────────
public sealed class ConsumerHostedService<TConsumer>(TConsumer consumer) : IHostedService
    where TConsumer : class
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var method = typeof(TConsumer).GetMethod("StartAsync")!;
        return (Task)method.Invoke(consumer, [cancellationToken])!;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        var method = typeof(TConsumer).GetMethod("StopAsync")!;
        return (Task)method.Invoke(consumer, [cancellationToken])!;
    }
}

// Expose for integration tests
public partial class Program { }
