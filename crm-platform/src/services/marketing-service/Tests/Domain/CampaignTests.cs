using CrmPlatform.MarketingService.Domain.Entities;
using CrmPlatform.MarketingService.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace CrmPlatform.MarketingService.Tests.Domain;

public sealed class CampaignTests
{
    private static Campaign MakeDraft()
        => Campaign.Create(
            Guid.NewGuid(), "Test Campaign", "Description",
            CampaignChannel.Email, Guid.NewGuid());

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public void Create_SetsStatusDraft()
    {
        var c = MakeDraft();
        c.Status.Should().Be(CampaignStatus.Draft);
    }

    [Fact]
    public void Create_EmptyName_Throws()
    {
        var act = () => Campaign.Create(Guid.NewGuid(), "", "desc", CampaignChannel.Email, Guid.NewGuid());
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_RaisesCampaignCreatedEvent()
    {
        var c = MakeDraft();
        c.DomainEvents.Should().ContainSingle(e => e.EventType == "campaign.created");
    }

    // ── Schedule ──────────────────────────────────────────────────────────────

    [Fact]
    public void Schedule_FromDraft_SetsScheduled()
    {
        var c = MakeDraft();
        c.Schedule(DateTime.UtcNow.AddDays(1));
        c.Status.Should().Be(CampaignStatus.Scheduled);
    }

    [Fact]
    public void Schedule_PastDate_Throws()
    {
        var c = MakeDraft();
        var act = () => c.Schedule(DateTime.UtcNow.AddMinutes(-1));
        act.Should().Throw<InvalidOperationException>().WithMessage("*future*");
    }

    [Fact]
    public void Schedule_FromActive_Throws()
    {
        var c = MakeDraft();
        c.Activate();
        var act = () => c.Schedule(DateTime.UtcNow.AddDays(1));
        act.Should().Throw<InvalidOperationException>();
    }

    // ── Activate ──────────────────────────────────────────────────────────────

    [Fact]
    public void Activate_FromDraft_SetsActive()
    {
        var c = MakeDraft();
        c.Activate();
        c.Status.Should().Be(CampaignStatus.Active);
        c.StartedAt.Should().NotBeNull();
    }

    [Fact]
    public void Activate_FromScheduled_SetsActive()
    {
        var c = MakeDraft();
        c.Schedule(DateTime.UtcNow.AddDays(1));
        c.Activate();
        c.Status.Should().Be(CampaignStatus.Active);
    }

    [Fact]
    public void Activate_FromCompleted_Throws()
    {
        var c = MakeDraft();
        c.Activate();
        c.Complete();
        var act = () => c.Activate();
        act.Should().Throw<InvalidOperationException>();
    }

    // ── Pause ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Pause_FromActive_SetsPaused()
    {
        var c = MakeDraft();
        c.Activate();
        c.Pause();
        c.Status.Should().Be(CampaignStatus.Paused);
    }

    [Fact]
    public void Pause_FromDraft_Throws()
    {
        var c = MakeDraft();
        var act = () => c.Pause();
        act.Should().Throw<InvalidOperationException>();
    }

    // ── Cancel ────────────────────────────────────────────────────────────────

    [Fact]
    public void Cancel_FromActive_SetsCancelled()
    {
        var c = MakeDraft();
        c.Activate();
        c.Cancel();
        c.Status.Should().Be(CampaignStatus.Cancelled);
        c.EndedAt.Should().NotBeNull();
    }

    [Fact]
    public void Cancel_AlreadyCancelled_Throws()
    {
        var c = MakeDraft();
        c.Cancel();
        var act = () => c.Cancel();
        act.Should().Throw<InvalidOperationException>();
    }

    // ── UpdateDetails ─────────────────────────────────────────────────────────

    [Fact]
    public void UpdateDetails_WhileActive_Throws()
    {
        var c = MakeDraft();
        c.Activate();
        var act = () => c.UpdateDetails("New Name", "New Desc");
        act.Should().Throw<InvalidOperationException>().WithMessage("*Draft or Scheduled*");
    }
}
