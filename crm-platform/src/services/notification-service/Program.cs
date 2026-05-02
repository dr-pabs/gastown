using CrmPlatform.NotificationService.Api;
using CrmPlatform.NotificationService.Application;
using CrmPlatform.NotificationService.Infrastructure.Acs;
using CrmPlatform.NotificationService.Infrastructure.Data;
using CrmPlatform.NotificationService.Infrastructure.Messaging;
using CrmPlatform.ServiceTemplate.Infrastructure;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using Hellang.Middleware.ProblemDetails;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ─── Platform bootstrap (auth, health, OTel, SB client) ──────────────────────
builder.Services.AddCrmService(builder.Configuration, builder.Environment, "notification-service");

// ─── Database ─────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Default"),
        sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "notifications")));

// ─── Idempotency store ────────────────────────────────────────────────────────
builder.Services.AddScoped<IIdempotencyStore, NotificationIdempotencyStore>();

// ─── ACS ─────────────────────────────────────────────────────────────────────
builder.Services.Configure<AcsOptions>(builder.Configuration.GetSection("Acs"));
builder.Services.AddScoped<INotificationSender, AcsNotificationSender>();

// ─── Application handlers ─────────────────────────────────────────────────────
builder.Services.AddScoped<SendNotificationHandler>();
builder.Services.AddScoped<TemplateHandler>();
builder.Services.AddScoped<PreferenceHandler>();

// ─── Service Bus consumers ────────────────────────────────────────────────────
builder.Services.Configure<ServiceBusConsumerOptions>(
    builder.Configuration.GetSection("ServiceBus:ConsumerOptions"));

// crm.platform
builder.Services.AddSingleton<TenantProvisionedConsumer>();
builder.Services.AddSingleton<TenantSuspendedConsumer>();
// crm.identity
builder.Services.AddSingleton<UserProvisionedConsumer>();
// crm.css
builder.Services.AddSingleton<CaseCreatedConsumer>();
builder.Services.AddSingleton<CaseAssignedConsumer>();
builder.Services.AddSingleton<CaseStatusChangedConsumer>();
builder.Services.AddSingleton<SlaBreachedConsumer>();
// crm.sfa
builder.Services.AddSingleton<LeadAssignedConsumer>();
builder.Services.AddSingleton<OpportunityWonConsumer>();

builder.Services.AddHostedService<ConsumerHostedService<TenantProvisionedConsumer>>();
builder.Services.AddHostedService<ConsumerHostedService<TenantSuspendedConsumer>>();
builder.Services.AddHostedService<ConsumerHostedService<UserProvisionedConsumer>>();
builder.Services.AddHostedService<ConsumerHostedService<CaseCreatedConsumer>>();
builder.Services.AddHostedService<ConsumerHostedService<CaseAssignedConsumer>>();
builder.Services.AddHostedService<ConsumerHostedService<CaseStatusChangedConsumer>>();
builder.Services.AddHostedService<ConsumerHostedService<SlaBreachedConsumer>>();
builder.Services.AddHostedService<ConsumerHostedService<LeadAssignedConsumer>>();
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
app.MapNotificationEndpoints();

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
