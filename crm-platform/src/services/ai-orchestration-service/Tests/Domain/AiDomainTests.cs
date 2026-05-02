using CrmPlatform.AiOrchestrationService.Domain.Entities;
using CrmPlatform.AiOrchestrationService.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace CrmPlatform.AiOrchestrationService.Tests.Domain;

public sealed class AiDomainTests
{
    // ══════════════════════════════════════════════════════════════════════════
    // AiJob
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AiJob_Create_SetsQueuedStatusAndDefaults()
    {
        var tenantId = Guid.NewGuid();
        var job = AiJob.Create(tenantId, CapabilityType.LeadScoring,
            UseCase.LeadCreated, null, "{\"leadId\":\"abc\"}");

        job.Id.Should().NotBeEmpty();
        job.TenantId.Should().Be(tenantId);
        job.CapabilityType.Should().Be(CapabilityType.LeadScoring);
        job.UseCase.Should().Be(UseCase.LeadCreated);
        job.Status.Should().Be(AiJobStatus.Queued);
        job.AttemptCount.Should().Be(0);
        job.AiResultId.Should().BeNull();
        job.IsTerminal.Should().BeFalse();
        job.IsStale.Should().BeFalse();  // just created
    }

    [Fact]
    public void AiJob_Create_ThrowsWhenPayloadEmpty()
    {
        var act = () => AiJob.Create(Guid.NewGuid(), CapabilityType.LeadScoring,
            UseCase.LeadCreated, null, "  ");
        act.Should().Throw<ArgumentException>().WithMessage("*payload*");
    }

    [Fact]
    public void AiJob_MarkInProgress_IncrementsAttemptAndSetsTimestamp()
    {
        var job = MakeJob();
        job.MarkInProgress();

        job.Status.Should().Be(AiJobStatus.InProgress);
        job.AttemptCount.Should().Be(1);
        job.FirstAttemptAt.Should().NotBeNull();
        job.LastAttemptAt.Should().NotBeNull();
        job.NextRetryAt.Should().BeNull();
    }

    [Fact]
    public void AiJob_MarkInProgress_SecondAttempt_KeepsFirstAttemptAt()
    {
        var job = MakeJob();
        job.MarkInProgress();
        var first = job.FirstAttemptAt;

        job.MarkFailed("error", DateTime.UtcNow.AddSeconds(-1)); // eligible for retry
        job.MarkInProgress();

        job.AttemptCount.Should().Be(2);
        job.FirstAttemptAt.Should().Be(first);
    }

    [Fact]
    public void AiJob_MarkSucceeded_SetsSucceededStatus()
    {
        var job      = MakeJob();
        var resultId = Guid.NewGuid();
        job.MarkInProgress();
        job.MarkSucceeded(resultId);

        job.Status.Should().Be(AiJobStatus.Succeeded);
        job.AiResultId.Should().Be(resultId);
        job.CompletedAt.Should().NotBeNull();
        job.FailureReason.Should().BeNull();
        job.NextRetryAt.Should().BeNull();
        job.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void AiJob_MarkFailed_SetsFailedStatusAndNextRetry()
    {
        var job     = MakeJob();
        var retryAt = DateTime.UtcNow.AddSeconds(30);
        job.MarkInProgress();
        job.MarkFailed("Claude timeout", retryAt);

        job.Status.Should().Be(AiJobStatus.Failed);
        job.FailureReason.Should().Be("Claude timeout");
        job.NextRetryAt.Should().BeCloseTo(retryAt, TimeSpan.FromSeconds(1));
        job.IsTerminal.Should().BeFalse();
    }

    [Fact]
    public void AiJob_Abandon_SetsAbandonedStatus()
    {
        var job = MakeJob();
        job.MarkInProgress();
        job.MarkFailed("transient");
        job.Abandon("All retries exhausted.");

        job.Status.Should().Be(AiJobStatus.Abandoned);
        job.AbandonedAt.Should().NotBeNull();
        job.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void AiJob_Abandon_OnTerminalJob_Throws()
    {
        var job = MakeJob();
        job.MarkInProgress();
        job.MarkSucceeded();

        var act = () => job.Abandon("too late");
        act.Should().Throw<InvalidOperationException>().WithMessage("*terminal*");
    }

    [Fact]
    public void AiJob_MarkInProgress_OnNonQueuedJob_Throws()
    {
        var job = MakeJob();
        job.MarkInProgress();
        job.MarkSucceeded();

        var act = () => job.MarkInProgress();
        act.Should().Throw<InvalidOperationException>();
    }

    private static AiJob MakeJob() => AiJob.Create(
        Guid.NewGuid(), CapabilityType.LeadScoring, UseCase.LeadCreated,
        Guid.NewGuid(), "{\"leadId\":\"test\"}");

    // ══════════════════════════════════════════════════════════════════════════
    // AiResult
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AiResult_Record_StoresAllFields()
    {
        var tenantId = Guid.NewGuid();
        var jobId    = Guid.NewGuid();

        var result = AiResult.Record(
            tenantId, jobId,
            CapabilityType.LeadScoring, UseCase.LeadCreated,
            "claude-3-haiku", "SYSTEM: ...", "{\"score\":85}",
            100, 50);

        result.Id.Should().NotBeEmpty();
        result.TenantId.Should().Be(tenantId);
        result.JobId.Should().Be(jobId);
        result.CapabilityType.Should().Be(CapabilityType.LeadScoring);
        result.OutputContent.Should().Be("{\"score\":85}");
        result.InputTokens.Should().Be(100);
        result.OutputTokens.Should().Be(50);
        result.RecordedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void AiResult_Record_ThrowsWhenOutputEmpty()
    {
        var act = () => AiResult.Record(
            Guid.NewGuid(), null,
            CapabilityType.EmailDraft, UseCase.Default,
            "claude", "prompt", "   ", 0, 0);
        act.Should().Throw<ArgumentException>().WithMessage("*Output*");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // PromptTemplate
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void PromptTemplate_Create_SetsFields()
    {
        var t = PromptTemplate.Create(
            Guid.NewGuid(), CapabilityType.EmailDraft, UseCase.EmailDraftLeadAssigned,
            "You are a sales email writer.", "Write email for {{leadName}}.");

        t.Id.Should().NotBeEmpty();
        t.SystemPrompt.Should().Contain("sales email writer");
        t.UserPromptTemplate.Should().Contain("{{leadName}}");
        t.IsDeleted.Should().BeFalse();
        t.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void PromptTemplate_Update_ChangesPromptsAndSetsUpdatedAt()
    {
        var t = MakePromptTemplate();
        t.Update("New system.", "New user {{x}}.");

        t.SystemPrompt.Should().Be("New system.");
        t.UserPromptTemplate.Should().Be("New user {{x}}.");
        t.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void PromptTemplate_Update_ThrowsWhenDeleted()
    {
        var t = MakePromptTemplate();
        t.Delete();

        var act = () => t.Update("s", "u");
        act.Should().Throw<InvalidOperationException>().WithMessage("*deleted*");
    }

    [Fact]
    public void PromptTemplate_Delete_SetsIsDeleted()
    {
        var t = MakePromptTemplate();
        t.Delete();

        t.IsDeleted.Should().BeTrue();
        t.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public void PromptTemplate_Create_ThrowsWhenSystemPromptEmpty()
    {
        var act = () => PromptTemplate.Create(
            Guid.NewGuid(), CapabilityType.LeadScoring, UseCase.LeadCreated,
            "  ", "user {{x}}");
        act.Should().Throw<ArgumentException>().WithMessage("*System prompt*");
    }

    private static PromptTemplate MakePromptTemplate() =>
        PromptTemplate.Create(Guid.NewGuid(), CapabilityType.LeadScoring, UseCase.LeadCreated,
            "System prompt.", "User prompt {{x}}.");

    // ══════════════════════════════════════════════════════════════════════════
    // SmsRecord
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SmsRecord_Create_SetsPendingState()
    {
        var s = SmsRecord.Create(Guid.NewGuid(), "+447700900000", "Hello there!", null);

        s.Id.Should().NotBeEmpty();
        s.IsSent.Should().BeFalse();
        s.IsFailed.Should().BeFalse();
        s.AcsMessageId.Should().BeNull();
    }

    [Fact]
    public void SmsRecord_MarkSent_SetsDeliveredState()
    {
        var s = SmsRecord.Create(Guid.NewGuid(), "+447700900000", "Hello!", null);
        s.MarkSent("acs-msg-123");

        s.IsSent.Should().BeTrue();
        s.AcsMessageId.Should().Be("acs-msg-123");
        s.SentAt.Should().NotBeNull();
    }

    [Fact]
    public void SmsRecord_MarkFailed_SetsFailedState()
    {
        var s = SmsRecord.Create(Guid.NewGuid(), "+447700900000", "Hello!", null);
        s.MarkFailed("Rate limit exceeded.");

        s.IsFailed.Should().BeTrue();
        s.FailureReason.Should().Be("Rate limit exceeded.");
        s.FailedAt.Should().NotBeNull();
    }

    [Fact]
    public void SmsRecord_MarkSent_AfterFailed_Throws()
    {
        var s = SmsRecord.Create(Guid.NewGuid(), "+447700900000", "Hello!", null);
        s.MarkFailed("err");

        var act = () => s.MarkSent("id");
        act.Should().Throw<InvalidOperationException>().WithMessage("*terminal*");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // TeamsCallRecord
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void TeamsCallRecord_Create_SetsPendingState()
    {
        var c = TeamsCallRecord.Create(Guid.NewGuid(), "8:acs:user123", "{\"leadId\":\"abc\"}");

        c.Id.Should().NotBeEmpty();
        c.IsConnected.Should().BeFalse();
        c.IsEnded.Should().BeFalse();
        c.AcsCallId.Should().BeNull();
        c.TranscriptText.Should().BeNull();
    }

    [Fact]
    public void TeamsCallRecord_MarkConnected_SetsConnectedState()
    {
        var c = TeamsCallRecord.Create(Guid.NewGuid(), "8:acs:user123", "{}");
        c.MarkConnected("call-id-456");

        c.IsConnected.Should().BeTrue();
        c.AcsCallId.Should().Be("call-id-456");
        c.ConnectedAt.Should().NotBeNull();
    }

    [Fact]
    public void TeamsCallRecord_SetTranscript_StoresText()
    {
        var c = TeamsCallRecord.Create(Guid.NewGuid(), "8:acs:user123", "{}");
        c.MarkConnected("call-id");
        c.SetTranscript("Agent: Hello. Customer: Hi.");

        c.TranscriptText.Should().Contain("Hello");
        c.TranscriptAt.Should().NotBeNull();
    }

    [Fact]
    public void TeamsCallRecord_MarkEnded_SetsDuration()
    {
        var c = TeamsCallRecord.Create(Guid.NewGuid(), "8:acs:user123", "{}");
        c.MarkConnected("call-id");
        c.MarkEnded(180);

        c.IsEnded.Should().BeTrue();
        c.DurationSeconds.Should().Be(180);
        c.EndedAt.Should().NotBeNull();
    }

    [Fact]
    public void TeamsCallRecord_MarkEnded_Twice_Throws()
    {
        var c = TeamsCallRecord.Create(Guid.NewGuid(), "8:acs:user123", "{}");
        c.MarkConnected("call-id");
        c.MarkEnded(60);

        var act = () => c.MarkEnded(120);
        act.Should().Throw<InvalidOperationException>().WithMessage("*already ended*");
    }

    [Fact]
    public void TeamsCallRecord_Create_ThrowsWhenTargetUserIdEmpty()
    {
        var act = () => TeamsCallRecord.Create(Guid.NewGuid(), "  ", "{}");
        act.Should().Throw<ArgumentException>().WithMessage("*Target user*");
    }
}
