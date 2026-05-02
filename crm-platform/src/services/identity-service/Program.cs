using CrmPlatform.IdentityService.Api;
using CrmPlatform.IdentityService.Application.Consent;
using CrmPlatform.IdentityService.Application.Roles;
using CrmPlatform.IdentityService.Application.Tenants;
using CrmPlatform.IdentityService.Application.Users;
using CrmPlatform.IdentityService.Infrastructure.Data;
using CrmPlatform.IdentityService.Infrastructure.Messaging;
using CrmPlatform.ServiceTemplate.Infrastructure;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using Hellang.Middleware.ProblemDetails;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ─── Platform bootstrap (auth, health, OTel, SB client) ──────────────────────
builder.Services.AddCrmService(builder.Configuration, builder.Environment, "identity-service");

// ─── Database ─────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<IdentityDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Default"),
        sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "identity")));

// ─── Idempotency store ────────────────────────────────────────────────────────
builder.Services.AddScoped<IIdempotencyStore, IdentityIdempotencyStore>();

// ─── Application handlers ─────────────────────────────────────────────────────
builder.Services.AddScoped<ProvisionUserHandler>();
builder.Services.AddScoped<DeprovisionUserHandler>();
builder.Services.AddScoped<GrantRoleHandler>();
builder.Services.AddScoped<RevokeRoleHandler>();
builder.Services.AddScoped<GetTenantRegistryHandler>();
builder.Services.AddScoped<RecordConsentHandler>();

// ─── Service Bus consumers (hosted services) ──────────────────────────────────
builder.Services.Configure<ServiceBusConsumerOptions>(
    builder.Configuration.GetSection("ServiceBus:ConsumerOptions"));

builder.Services.AddSingleton<TenantSuspendedConsumer>();
builder.Services.AddSingleton<TenantDeprovisionedConsumer>();

builder.Services.AddHostedService<ConsumerHostedService<TenantSuspendedConsumer>>();
builder.Services.AddHostedService<ConsumerHostedService<TenantDeprovisionedConsumer>>();

// ─── Problem Details (RFC 7807) ───────────────────────────────────────────────
builder.Services.AddProblemDetails(opts =>
{
    opts.IncludeExceptionDetails = (ctx, _) => ctx.RequestServices
        .GetRequiredService<IHostEnvironment>().IsDevelopment();
});

// ─── Memory cache (role cache) ────────────────────────────────────────────────
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
app.MapIdentityEndpoints();

app.Run();

// Expose for integration tests
public partial class Program { }
