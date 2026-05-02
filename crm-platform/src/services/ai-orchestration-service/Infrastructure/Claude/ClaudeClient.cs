using Azure;
using Azure.AI.Inference;
using HandlebarsDotNet;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using CrmPlatform.AiOrchestrationService.Domain.Entities;
using CrmPlatform.AiOrchestrationService.Domain.Enums;

namespace CrmPlatform.AiOrchestrationService.Infrastructure.Claude;

/// <summary>Result of a single Claude invocation.</summary>
public sealed record ClaudeResponse(
    string Content,
    string ModelName,
    string PromptUsed,
    int    InputTokens,
    int    OutputTokens);

public interface IClaudeClient
{
    Task<ClaudeResponse> CompleteAsync(
        Guid             tenantId,
        CapabilityType   capabilityType,
        UseCase          useCase,
        object           templateVars,
        CancellationToken ct = default);
}

/// <summary>
/// Wraps Azure.AI.Inference ChatCompletionsClient to invoke Claude models via
/// Azure AI Services (Model-as-a-Service endpoint).
/// Prompt resolution: tenant custom (DB) → platform hard-coded default.
/// </summary>
public sealed class ClaudeClient(
    IConfiguration             config,
    IPromptResolver            promptResolver,
    ILogger<ClaudeClient>      logger)
    : IClaudeClient
{
    private ChatCompletionsClient BuildClient() =>
        new(
            new Uri(config["Azure:AI:Endpoint"]!),
            new AzureKeyCredential(config["Azure:AI:ApiKey"]!));

    public async Task<ClaudeResponse> CompleteAsync(
        Guid              tenantId,
        CapabilityType    capabilityType,
        UseCase           useCase,
        object            templateVars,
        CancellationToken ct = default)
    {
        var (systemPrompt, userTemplate) = await promptResolver.ResolveAsync(
            tenantId, capabilityType, useCase, ct);

        // Render Handlebars template
        var hbsTemplate = Handlebars.Compile(userTemplate);
        var userMessage  = hbsTemplate(templateVars);

        var modelName = config[$"Azure:AI:Models:{capabilityType}"]
                     ?? config["Azure:AI:Models:Default"]
                     ?? "claude-3-7-sonnet-20250219";

        var options = new ChatCompletionsOptions
        {
            Model = modelName,
            Messages =
            {
                new ChatRequestSystemMessage(systemPrompt),
                new ChatRequestUserMessage(userMessage)
            },
            MaxTokens    = 2048,
            Temperature  = 0.3f
        };

        logger.LogDebug("Calling Claude model={Model} capability={Capability} useCase={UseCase}",
            modelName, capabilityType, useCase);

        var client   = BuildClient();
        var response = await client.CompleteAsync(options, ct);
        var choice   = response.Value.Choices[0];
        var content  = choice.Message.Content ?? string.Empty;

        var usage = response.Value.Usage;

        return new ClaudeResponse(
            Content:      content,
            ModelName:    modelName,
            PromptUsed:   $"SYSTEM: {systemPrompt}\nUSER: {userMessage}",
            InputTokens:  usage?.PromptTokens  ?? 0,
            OutputTokens: usage?.CompletionTokens ?? 0);
    }
}

// ── Prompt resolver ───────────────────────────────────────────────────────────

public interface IPromptResolver
{
    Task<(string SystemPrompt, string UserPromptTemplate)> ResolveAsync(
        Guid           tenantId,
        CapabilityType capabilityType,
        UseCase        useCase,
        CancellationToken ct = default);
}

/// <summary>
/// Resolves prompts: DB tenant custom first, then PromptDefaults (hard-coded).
/// </summary>
public sealed class PromptResolver(
    IPromptTemplateReader promptReader)
    : IPromptResolver
{
    public async Task<(string SystemPrompt, string UserPromptTemplate)> ResolveAsync(
        Guid           tenantId,
        CapabilityType capabilityType,
        UseCase        useCase,
        CancellationToken ct = default)
    {
        var template = await promptReader.FindAsync(tenantId, capabilityType, useCase, ct);
        if (template is not null)
            return (template.SystemPrompt, template.UserPromptTemplate);

        // Fall back to hard-coded platform defaults
        return PromptDefaults.Get(capabilityType, useCase);
    }
}

public interface IPromptTemplateReader
{
    Task<PromptTemplate?> FindAsync(
        Guid           tenantId,
        CapabilityType capabilityType,
        UseCase        useCase,
        CancellationToken ct = default);
}

// ── Platform hard-coded defaults ──────────────────────────────────────────────

public static class PromptDefaults
{
    private static readonly Dictionary<(CapabilityType, UseCase), (string System, string User)> _defaults = new()
    {
        [(CapabilityType.LeadScoring, UseCase.LeadCreated)] = (
            "You are a CRM lead scoring assistant. Analyse the lead data and return a score from 0-100 with rationale.",
            "Lead data: {{leadData}}\nReturn JSON: {\"score\": <int>, \"rationale\": \"<string>\", \"confidence\": <float 0-1>}"),

        [(CapabilityType.LeadScoring, UseCase.LeadAssigned)] = (
            "You are a CRM lead scoring assistant. Re-evaluate the lead after assignment.",
            "Lead data: {{leadData}}\nAssigned to user: {{assignedTo}}\nReturn JSON: {\"score\": <int>, \"rationale\": \"<string>\", \"confidence\": <float 0-1>}"),

        [(CapabilityType.EmailDraft, UseCase.EmailDraftLeadAssigned)] = (
            "You are a professional sales copywriter. Write a short, personalised outreach email.",
            "Lead: {{leadName}}, Company: {{company}}, Product interest: {{productInterest}}\nWrite a concise outreach email (max 150 words)."),

        [(CapabilityType.EmailDraft, UseCase.EmailDraftOpportunityWon)] = (
            "You are a professional relationship manager. Write a warm congratulatory email.",
            "Contact: {{contactName}}, Opportunity: {{opportunityName}}, Value: {{value}}\nWrite a brief congratulatory email (max 100 words)."),

        [(CapabilityType.CaseSummarisation, UseCase.CaseResolved)] = (
            "You are a CRM case summarisation assistant. Summarise the case concisely.",
            "Case data: {{caseData}}\nProvide a 2-3 sentence summary of the issue and resolution."),

        [(CapabilityType.SentimentAnalysis, UseCase.CaseCommentAdded)] = (
            "You are a sentiment analysis assistant. Classify the sentiment of the customer comment.",
            "Comment: {{commentText}}\nReturn JSON: {\"sentiment\": \"Positive|Neutral|Negative|Mixed\", \"score\": <float 0-1>}"),

        [(CapabilityType.NextBestAction, UseCase.NbaLeadAssigned)] = (
            "You are a sales strategy assistant. Recommend the next best action for this lead.",
            "Lead data: {{leadData}}\nReturn JSON: {\"action\": \"<string>\", \"rationale\": \"<string>\"}"),

        [(CapabilityType.NextBestAction, UseCase.NbaOpportunityStageChanged)] = (
            "You are a sales strategy assistant. Recommend the next best action for this opportunity.",
            "Opportunity: {{opportunityData}}, Stage change: {{oldStage}} → {{newStage}}\nReturn JSON: {\"action\": \"<string>\", \"rationale\": \"<string>\"}"),

        [(CapabilityType.JourneyPersonalisation, UseCase.JourneyEnrollmentCreated)] = (
            "You are a marketing personalisation assistant. Recommend the best journey branch.",
            "Contact: {{contactData}}, Journey: {{journeyData}}, Branches: {{branches}}\nReturn JSON: {\"recommendedBranchId\": \"<guid>\", \"rationale\": \"<string>\"}"),

        [(CapabilityType.SmsComposition, UseCase.SmsBroadcast)] = (
            "You are an SMS copywriter. Write a concise, compliant SMS message (max 160 chars).",
            "Campaign: {{campaignName}}, Audience: {{audienceDescription}}, Goal: {{goal}}\nWrite the SMS text only."),

        [(CapabilityType.TeamsNotification, UseCase.TeamsAdaptiveCard)] = (
            "You are a Teams notification assistant. Compose an Adaptive Card body message.",
            "Context: {{context}}\nWrite a concise notification message (max 2 sentences)."),
    };

    public static (string SystemPrompt, string UserPromptTemplate) Get(
        CapabilityType capabilityType,
        UseCase        useCase)
    {
        if (_defaults.TryGetValue((capabilityType, useCase), out var prompt))
            return prompt;

        // Generic fallback
        return (
            "You are a helpful AI assistant integrated into a CRM platform.",
            "Process the following input and return a useful, structured response:\n{{input}}");
    }
}
