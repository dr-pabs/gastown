using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Azure.Messaging.ServiceBus;
using CrmPlatform.ServiceTemplate.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;

namespace CrmPlatform.ServiceTemplate.Infrastructure;

/// <summary>
/// Extension methods registered by every CRM service in Program.cs.
/// Call builder.Services.AddCrmService(builder.Configuration, builder.Environment, "sfa-service")
/// to wire everything up.
/// </summary>
public static class ServiceBootstrap
{
    public static IServiceCollection AddCrmService(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        string serviceName)
    {
        // ─── Multi-tenancy ────────────────────────────────────────────────────
        services.AddHttpContextAccessor();
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<ITenantContextAccessor, TenantContextAccessor>();

        // ─── Auth: JWT bearer ─────────────────────────────────────────────────
        services.AddCrmAuthentication(configuration, environment);

        // ─── Service Bus client ───────────────────────────────────────────────
        var sbConnectionString = configuration["ServiceBus:ConnectionString"]
            ?? throw new InvalidOperationException("ServiceBus:ConnectionString is required");
        services.AddSingleton(_ => new ServiceBusClient(sbConnectionString));
        services.AddScoped<ServiceBusEventPublisher>();

        // ─── Health checks ────────────────────────────────────────────────────
        var healthBuilder = services.AddHealthChecks();
        var sqlConnectionString = configuration.GetConnectionString("Default");
        if (!string.IsNullOrEmpty(sqlConnectionString))
            healthBuilder.AddSqlServer(sqlConnectionString, name: "sql", tags: ["ready"]);

        healthBuilder.AddAzureServiceBusQueue(
            sbConnectionString,
            queueName: serviceName,   // services don't use queues but this validates connectivity
            name: "servicebus",
            tags: ["ready"]);

        // ─── OpenTelemetry ────────────────────────────────────────────────────
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter());

        return services;
    }

    public static WebApplication UseCrmService(this WebApplication app)
    {
        // Health endpoints (unauthenticated — must be before UseAuthentication)
        app.MapHealthChecks("/health/live",  new() { Predicate = _ => false });   // liveness: always 200 if up
        app.MapHealthChecks("/health/ready", new() { Predicate = hc => hc.Tags.Contains("ready") });
        app.MapHealthChecks("/health/start", new() { Predicate = _ => false });   // startup probe

        app.UseAuthentication();
        app.UseAuthorization();

        // Layer 2 tenant context middleware — runs after JWT is validated
        app.UseMiddleware<TenantContextMiddleware>();

        return app;
    }

    // ─── Auth wiring ──────────────────────────────────────────────────────────
    private static void AddCrmAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var devAuthMode = configuration["DevAuthMode"] ?? "entra";

        var authBuilder = services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme);

        if (environment.IsDevelopment() && devAuthMode == "stub")
        {
            // Local dev: validate against auth-stub's HMAC secret
            var signingSecret = configuration["DevAuthStubSigningKey"]
                ?? "dev-only-hmac-secret-crm-platform-2024-not-for-production";

            authBuilder.AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = "crm-auth-stub",
                    ValidAudience = "crm-api",
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingSecret)),
                    ValidateIssuerSigningKey = true,
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                };
            });
        }
        else
        {
            // Production / Azure: validate against Entra ID
            var tenantId = configuration["Entra:TenantId"]
                ?? throw new InvalidOperationException("Entra:TenantId is required outside Development/stub mode");
            var audience = configuration["Entra:Audience"]
                ?? throw new InvalidOperationException("Entra:Audience is required outside Development/stub mode");

            authBuilder.AddJwtBearer(options =>
            {
                options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
                options.Audience = audience;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                };
            });
        }

        services.AddAuthorization();
    }
}
