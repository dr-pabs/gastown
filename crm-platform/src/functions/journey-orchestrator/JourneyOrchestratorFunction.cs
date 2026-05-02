using Microsoft.Azure.Functions.Worker;
using Azure.Messaging.ServiceBus;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrmPlatform.Functions.JourneyOrchestrator;

// ── Message contract (mirrors marketing-service JourneyEnrollmentCreatedEvent) ──

public sealed record JourneyEnrollmentCreatedMessage(
    Guid   EnrollmentId,
    Guid   TenantId,
    Guid   JourneyId,
    Guid   ContactId,
    string StepsJson,        // serialised journey steps forwarded from marketing-service
    int    StepCount);

// ── Step definition (resolved from StepsJson) ─────────────────────────────────

public sealed record JourneyStep(
    string Type,             // "email" | "delay" | "condition"
    string? TemplateId,
    int?    DelayMinutes);

// ── Input/output models ───────────────────────────────────────────────────────

public sealed record JourneyInput(
    Guid   EnrollmentId,
    Guid   TenantId,
    Guid   JourneyId,
    Guid   ContactId,
    IReadOnlyList<JourneyStep> Steps);

public sealed record SendEmailInput(
    Guid   TenantId,
    Guid   ContactId,
    string TemplateId,
    Guid   EnrollmentId,
    int    StepIndex);

public sealed record AdvanceEnrollmentInput(
    Guid TenantId,
    Guid EnrollmentId,
    int  StepIndex);

public sealed record CompleteEnrollmentInput(
    Guid TenantId,
    Guid EnrollmentId);

// ── Trigger: Service Bus → start new orchestration ────────────────────────────

public static class JourneyTrigger
{
    [Function(nameof(JourneyTrigger))]
    public static async Task Run(
        [ServiceBusTrigger("crm.marketing", "journey-orchestrator", Connection = "ServiceBusConnection")]
        ServiceBusReceivedMessage serviceBusMessage,
        [DurableClient] DurableTaskClient client,
        FunctionContext context)
    {
        var logger = context.GetLogger(nameof(JourneyTrigger));
        var message = System.Text.Json.JsonSerializer.Deserialize<JourneyEnrollmentCreatedMessage>(serviceBusMessage.Body.ToString())
            ?? throw new InvalidOperationException("Journey trigger payload is required.");

        // Parse steps from JSON
        var steps = System.Text.Json.JsonSerializer
            .Deserialize<List<JourneyStep>>(message.StepsJson)
            ?? [];

        var input = new JourneyInput(
            message.EnrollmentId,
            message.TenantId,
            message.JourneyId,
            message.ContactId,
            steps);

        // Instance ID is deterministic — prevents duplicate orchestrations if the
        // trigger is redelivered (idempotency at the Durable Functions level).
        var instanceId = $"journey-{message.EnrollmentId}";

        // Check for existing orchestration before starting
        var existing = await client.GetInstanceAsync(instanceId);
        if (existing?.RuntimeStatus is
            OrchestrationRuntimeStatus.Running or
            OrchestrationRuntimeStatus.Pending)
        {
            logger.LogInformation(
                "Journey orchestration {InstanceId} already running — skipping duplicate trigger",
                instanceId);
            return;
        }

        await client.ScheduleNewOrchestrationInstanceAsync(
            nameof(JourneyOrchestrator),
            input,
            new StartOrchestrationOptions(instanceId));

        logger.LogInformation(
            "Started journey orchestration {InstanceId} for enrollment {EnrollmentId}",
            instanceId, message.EnrollmentId);
    }
}

// ── Orchestrator ──────────────────────────────────────────────────────────────

public static class JourneyOrchestrator
{
    [Function(nameof(JourneyOrchestrator))]
    public static async Task<string> Run(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var logger = context.CreateReplaySafeLogger(nameof(JourneyOrchestrator));
        var input  = context.GetInput<JourneyInput>()
            ?? throw new InvalidOperationException("JourneyInput is required.");

        // Store instance ID on the enrollment record immediately
        await context.CallActivityAsync(
            nameof(StoreInstanceIdActivity),
            new StoreInstanceIdInput(
                input.TenantId,
                input.EnrollmentId,
                context.InstanceId));

        // Process each step in sequence
        for (var i = 0; i < input.Steps.Count; i++)
        {
            var step = input.Steps[i];

            // ── Check for external cancel event ──────────────────────────────
            // Raised when an enrollment is exited (opt-out) from the API.
            var cancelToken = context.WaitForExternalEvent<bool>("cancel");

            switch (step.Type.ToLowerInvariant())
            {
                case "email":
                    var emailTask = context.CallActivityAsync(
                        nameof(SendEmailActivity),
                        new SendEmailInput(
                            input.TenantId,
                            input.ContactId,
                            step.TemplateId ?? string.Empty,
                            input.EnrollmentId,
                            i));

                    // Wait for email activity OR cancel
                    var emailOrCancel = await Task.WhenAny(emailTask, cancelToken);

                    if (emailOrCancel == cancelToken)
                    {
                        logger.LogInformation(
                            "Journey {InstanceId} cancelled at step {StepIndex}",
                            context.InstanceId, i);
                        return "cancelled";
                    }

                    break;

                case "delay":
                    var delayMinutes = step.DelayMinutes ?? 60;
                    var deadline     = context.CurrentUtcDateTime.AddMinutes(delayMinutes);

                    var delayTimer = context.CreateTimer(deadline, CancellationToken.None);
                    var delayOrCancel = await Task.WhenAny(delayTimer, cancelToken);

                    if (delayOrCancel == cancelToken)
                    {
                        logger.LogInformation(
                            "Journey {InstanceId} cancelled during delay at step {StepIndex}",
                            context.InstanceId, i);
                        return "cancelled";
                    }

                    break;

                default:
                    logger.LogWarning(
                        "Journey {InstanceId}: unknown step type '{Type}' at index {Index} — skipping",
                        context.InstanceId, step.Type, i);
                    break;
            }

            // Advance the enrollment step counter in the database
            await context.CallActivityAsync(
                nameof(AdvanceEnrollmentActivity),
                new AdvanceEnrollmentInput(input.TenantId, input.EnrollmentId, i + 1));
        }

        // All steps complete — mark enrollment done and publish journey.completed
        await context.CallActivityAsync(
            nameof(CompleteEnrollmentActivity),
            new CompleteEnrollmentInput(input.TenantId, input.EnrollmentId));

        logger.LogInformation(
            "Journey {InstanceId} completed for enrollment {EnrollmentId}",
            context.InstanceId, input.EnrollmentId);

        return "completed";
    }
}

// ── Activity functions ────────────────────────────────────────────────────────

public sealed record StoreInstanceIdInput(
    Guid   TenantId,
    Guid   EnrollmentId,
    string InstanceId);

public static class StoreInstanceIdActivity
{
    [Function(nameof(StoreInstanceIdActivity))]
    public static async Task Run(
        [ActivityTrigger] StoreInstanceIdInput input,
        FunctionContext context)
    {
        var logger = context.GetLogger(nameof(StoreInstanceIdActivity));

        // POST to marketing-service internal API to persist InstanceId
        // In production this uses Managed Identity with internal VNet routing.
        // Using IHttpClientFactory registered in Program.cs.
        var factory = context.InstanceServices
            .GetService(typeof(IHttpClientFactory)) as IHttpClientFactory;

        if (factory is null)
        {
            logger.LogWarning("IHttpClientFactory not available — cannot store InstanceId");
            return;
        }

        var client = factory.CreateClient("marketing-service");
        var resp   = await client.PatchAsync(
            $"/internal/enrollments/{input.EnrollmentId}/instance-id",
            JsonContent.Create(new { instanceId = input.InstanceId }));

        resp.EnsureSuccessStatusCode();

        logger.LogInformation(
            "Stored InstanceId {InstanceId} for enrollment {EnrollmentId}",
            input.InstanceId, input.EnrollmentId);
    }
}

file static class JsonContent
{
    public static System.Net.Http.StringContent Create<T>(T value)
        => new(
            System.Text.Json.JsonSerializer.Serialize(value),
            System.Text.Encoding.UTF8,
            "application/json");
}

public static class SendEmailActivity
{
    [Function(nameof(SendEmailActivity))]
    public static async Task Run(
        [ActivityTrigger] SendEmailInput input,
        FunctionContext context)
    {
        var logger = context.GetLogger(nameof(SendEmailActivity));

        // POST to notification-service (future) or direct SMTP/SendGrid adapter
        // Managed Identity auth — no credentials in code.
        logger.LogInformation(
            "Sending email template {TemplateId} to contact {ContactId} for tenant {TenantId} (step {Step})",
            input.TemplateId, input.ContactId, input.TenantId, input.StepIndex);

        // Placeholder: replace with actual email send call
        await Task.CompletedTask;
    }
}

public static class AdvanceEnrollmentActivity
{
    [Function(nameof(AdvanceEnrollmentActivity))]
    public static async Task Run(
        [ActivityTrigger] AdvanceEnrollmentInput input,
        FunctionContext context)
    {
        var logger  = context.GetLogger(nameof(AdvanceEnrollmentActivity));
        var factory = context.InstanceServices.GetRequiredService<IHttpClientFactory>();
        var client  = factory.CreateClient("marketing-service");

        var resp = await client.PostAsync(
            $"/internal/enrollments/{input.EnrollmentId}/advance",
            JsonContent.Create(new { stepIndex = input.StepIndex }));

        resp.EnsureSuccessStatusCode();

        logger.LogInformation(
            "Enrollment {EnrollmentId} advanced to step {StepIndex}",
            input.EnrollmentId, input.StepIndex);
    }
}

public static class CompleteEnrollmentActivity
{
    [Function(nameof(CompleteEnrollmentActivity))]
    public static async Task Run(
        [ActivityTrigger] CompleteEnrollmentInput input,
        FunctionContext context)
    {
        var logger  = context.GetLogger(nameof(CompleteEnrollmentActivity));
        var factory = context.InstanceServices.GetRequiredService<IHttpClientFactory>();
        var client  = factory.CreateClient("marketing-service");

        var resp = await client.PostAsync(
            $"/internal/enrollments/{input.EnrollmentId}/complete",
            JsonContent.Create(new { tenantId = input.TenantId }));

        resp.EnsureSuccessStatusCode();

        logger.LogInformation(
            "Enrollment {EnrollmentId} marked complete via activity",
            input.EnrollmentId);
    }
}
