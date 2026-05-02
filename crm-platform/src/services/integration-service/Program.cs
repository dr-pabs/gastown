using CrmPlatform.IntegrationService.Api;
using CrmPlatform.IntegrationService.Application;
using CrmPlatform.IntegrationService.Infrastructure.Connectors;
using CrmPlatform.IntegrationService.Infrastructure.Data;
using CrmPlatform.IntegrationService.Infrastructure.KeyVault;
using CrmPlatform.IntegrationService.Infrastructure.Messaging;
using CrmPlatform.IntegrationService.Infrastructure.Workers;
using CrmPlatform.ServiceTemplate.Infrastructure;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using Hellang.Middleware.ProblemDetails;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ─── Platform bootstrap (auth, health, OTel, SB client) ──────────────────────
builder.Services.AddCrmService(builder.Configuration, builder.Environment, "integration-service");

// ─── Database ─────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<IntegrationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Default"),
        sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "integrations")));

// ─── Idempotency ──────────────────────────────────────────────────────────────
builder.Services.AddScoped<IIdempotencyStore, IntegrationIdempotencyStore>();

// ─── Key Vault token store ────────────────────────────────────────────────────
builder.Services.Configure<KeyVaultOptions>(builder.Configuration.GetSection("KeyVault"));
builder.Services.AddSingleton<IConnectorTokenStore, KeyVaultConnectorTokenStore>();

// ─── OAuth options ────────────────────────────────────────────────────────────
builder.Services.Configure<OAuthOptions>(builder.Configuration.GetSection("OAuth"));

// ─── Connector adapters ───────────────────────────────────────────────────────
builder.Services.AddHttpClient("salesforce", c =>
    c.BaseAddress = new Uri(builder.Configuration["Connectors:SalesforceInstanceUrl"] ?? "https://login.salesforce.com"));

builder.Services.AddHttpClient("hubspot", c =>
    c.BaseAddress = new Uri("https://api.hubapi.com"));

builder.Services.AddHttpClient("oauth"); // generic — used for token exchange

builder.Services.AddScoped<IConnectorAdapter, SalesforceAdapter>();
builder.Services.AddScoped<IConnectorAdapter, HubSpotAdapter>();
builder.Services.AddScoped<IConnectorAdapter, AzureEventHubAdapter>();

// ─── Webhook validators ───────────────────────────────────────────────────────
builder.Services.AddScoped<IWebhookValidator, HubSpotWebhookValidator>();
builder.Services.AddScoped<IWebhookValidator, GenericWebhookValidator>();
builder.Services.AddScoped<IWebhookValidator, SalesforceWebhookValidator>();

// ─── Distributed cache (for OAuth nonce) ─────────────────────────────────────
builder.Services.AddStackExchangeRedisCache(opts =>
    opts.Configuration = builder.Configuration.GetConnectionString("Redis"));

// ─── Application handlers ─────────────────────────────────────────────────────
builder.Services.AddScoped<ConnectorManagementHandler>();
builder.Services.AddScoped<ConnectorOAuthHandler>();
builder.Services.AddScoped<InboundWebhookHandler>();

// ─── Service Bus consumers ────────────────────────────────────────────────────
builder.Services.Configure<ServiceBusConsumerOptions>(
    builder.Configuration.GetSection("ServiceBus:ConsumerOptions"));

builder.Services.AddSingleton<IntTenantProvisionedConsumer>();
builder.Services.AddSingleton<IntTenantSuspendedConsumer>();
builder.Services.AddSingleton<LeadAssignedConsumer>();
builder.Services.AddSingleton<OpportunityWonConsumer>();
builder.Services.AddSingleton<CaseCreatedIntConsumer>();
builder.Services.AddSingleton<CaseResolvedConsumer>();

builder.Services.AddHostedService<ConsumerHostedService<IntTenantProvisionedConsumer>>();
builder.Services.AddHostedService<ConsumerHostedService<IntTenantSuspendedConsumer>>();
builder.Services.AddHostedService<ConsumerHostedService<LeadAssignedConsumer>>();
builder.Services.AddHostedService<ConsumerHostedService<OpportunityWonConsumer>>();
builder.Services.AddHostedService<ConsumerHostedService<CaseCreatedIntConsumer>>();
builder.Services.AddHostedService<ConsumerHostedService<CaseResolvedConsumer>>();

// ─── Background workers ───────────────────────────────────────────────────────
builder.Services.AddHostedService<OutboundDispatchWorker>();
builder.Services.AddHostedService<BlobExportWorker>();

// ─── Problem Details (RFC 7807) ───────────────────────────────────────────────
builder.Services.AddProblemDetails(opts =>
{
    opts.IncludeExceptionDetails = (ctx, _) => ctx.RequestServices
        .GetRequiredService<IHostEnvironment>().IsDevelopment();
});

// ─── OpenAPI / Swagger ────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ─── Middleware pipeline ──────────────────────────────────────────────────────
app.UseProblemDetails();
app.UseCrmService(); // health + auth + TenantContextMiddleware

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ─── Endpoints ────────────────────────────────────────────────────────────────
app.MapIntegrationEndpoints();

app.Run();
