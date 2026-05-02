using CrmPlatform.MarketingService.Domain.Entities;
using CrmPlatform.MarketingService.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace CrmPlatform.MarketingService.Tests.Domain;

public sealed class JourneyTests
{
    private static readonly Guid TenantId    = Guid.NewGuid();
    private static readonly Guid CampaignId  = Guid.NewGuid();
    private static readonly Guid UserId      = Guid.NewGuid();

    private static Journey MakeJourney()
        => Journey.Create(TenantId, CampaignId, "Welcome Journey", "Desc", UserId);

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public void Create_IsNotPublished()
    {
        var j = MakeJourney();
        j.IsPublished.Should().BeFalse();
        j.StepCount.Should().Be(0);
    }

    [Fact]
    public void Create_EmptyName_Throws()
    {
        var act = () => Journey.Create(TenantId, CampaignId, "", "desc", UserId);
        act.Should().Throw<ArgumentException>();
    }

    // ── SetSteps ──────────────────────────────────────────────────────────────

    [Fact]
    public void SetSteps_ValidJson_SetsStepCount()
    {
        var j = MakeJourney();
        j.SetSteps("[{\"type\":\"email\"}]", 1);
        j.StepCount.Should().Be(1);
        j.StepsJson.Should().Contain("email");
    }

    [Fact]
    public void SetSteps_ZeroSteps_Throws()
    {
        var j = MakeJourney();
        var act = () => j.SetSteps("[{}]", 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── Publish ───────────────────────────────────────────────────────────────

    [Fact]
    public void Publish_WithSteps_SetsPublished()
    {
        var j = MakeJourney();
        j.SetSteps("[{\"type\":\"email\"}]", 1);
        j.Publish();
        j.IsPublished.Should().BeTrue();
    }

    [Fact]
    public void Publish_WithoutSteps_Throws()
    {
        var j = MakeJourney();
        var act = () => j.Publish();
        act.Should().Throw<InvalidOperationException>().WithMessage("*step*");
    }

    [Fact]
    public void Publish_Twice_Throws()
    {
        var j = MakeJourney();
        j.SetSteps("[{\"type\":\"email\"}]", 1);
        j.Publish();
        var act = () => j.Publish();
        act.Should().Throw<InvalidOperationException>().WithMessage("*already published*");
    }

    [Fact]
    public void Publish_RaisesJourneyPublishedEvent()
    {
        var j = MakeJourney();
        j.SetSteps("[{\"type\":\"email\"}]", 2);
        j.Publish();
        j.DomainEvents.Should().ContainSingle(e => e.EventType == "journey.published");
    }

    [Fact]
    public void SetSteps_AfterPublish_Throws()
    {
        var j = MakeJourney();
        j.SetSteps("[{\"type\":\"email\"}]", 1);
        j.Publish();
        var act = () => j.SetSteps("[{\"type\":\"delay\"}]", 2);
        act.Should().Throw<InvalidOperationException>().WithMessage("*immutable*");
    }
}

public sealed class JourneyEnrollmentTests
{
    private static readonly Guid TenantId   = Guid.NewGuid();
    private static readonly Guid JourneyId  = Guid.NewGuid();
    private static readonly Guid ContactId  = Guid.NewGuid();

    private static JourneyEnrollment Make()
        => JourneyEnrollment.Create(TenantId, JourneyId, ContactId);

    [Fact]
    public void Create_StatusIsActive()
    {
        var e = Make();
        e.Status.Should().Be(EnrollmentStatus.Active);
        e.CurrentStepIndex.Should().Be(0);
    }

    [Fact]
    public void AdvanceStep_IncrementsIndex()
    {
        var e = Make();
        e.AdvanceStep();
        e.AdvanceStep();
        e.CurrentStepIndex.Should().Be(2);
    }

    [Fact]
    public void Complete_SetsCompleted_RaisesEvent()
    {
        var e = Make();
        e.Complete();
        e.Status.Should().Be(EnrollmentStatus.Completed);
        e.CompletedAt.Should().NotBeNull();
        e.DomainEvents.Should().ContainSingle(ev => ev.EventType == "journey.completed");
    }

    [Fact]
    public void Complete_Twice_Throws()
    {
        var e = Make();
        e.Complete();
        var act = () => e.Complete();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Exit_SetsExited_WithReason()
    {
        var e = Make();
        e.Exit("unsubscribed");
        e.Status.Should().Be(EnrollmentStatus.Exited);
        e.ExitReason.Should().Be("unsubscribed");
    }

    [Fact]
    public void AdvanceStep_OnCompleted_Throws()
    {
        var e = Make();
        e.Complete();
        var act = () => e.AdvanceStep();
        act.Should().Throw<InvalidOperationException>();
    }
}
