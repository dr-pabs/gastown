using CrmPlatform.CssService.Domain.Entities;
using CrmPlatform.CssService.Domain.Enums;
using CrmPlatform.CssService.Domain.Events;
using FluentAssertions;

namespace CrmPlatform.CssService.Tests.Domain;

public sealed class CaseTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId   = Guid.NewGuid();

    private static Case DefaultCase() =>
        Case.Create(TenantId, "Test Case", "A description", CasePriority.Medium,
            CaseChannel.Email, null, null, UserId);

    // ─── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public void Create_SetsNewStatus()
    {
        var c = DefaultCase();
        c.Status.Should().Be(CaseStatus.New);
    }

    [Fact]
    public void Create_RaisesCaseCreatedEvent()
    {
        var c = DefaultCase();
        c.DomainEvents.Should().ContainSingle(e => e is CaseCreatedEvent);
    }

    // ─── Status machine ───────────────────────────────────────────────────────

    [Fact]
    public void Open_FromNew_SetsOpenAndDeadline()
    {
        var c       = DefaultCase();
        var deadline = DateTime.UtcNow.AddHours(4);
        c.Open(deadline);

        c.Status.Should().Be(CaseStatus.Open);
        c.SlaDeadline.Should().BeCloseTo(deadline, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Open_FromOpen_Throws()
    {
        var c = DefaultCase();
        c.Open(DateTime.UtcNow.AddHours(4));
        var act = () => c.Open(DateTime.UtcNow.AddHours(8));
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SetPending_FromOpen_ChangesPendingStatus()
    {
        var c = DefaultCase();
        c.Open(DateTime.UtcNow.AddHours(4));
        c.SetPending();
        c.Status.Should().Be(CaseStatus.Pending);
    }

    [Fact]
    public void Resume_FromPending_SetsOpen()
    {
        var c = DefaultCase();
        c.Open(DateTime.UtcNow.AddHours(4));
        c.SetPending();
        c.Resume();
        c.Status.Should().Be(CaseStatus.Open);
    }

    [Fact]
    public void Resolve_FromOpen_SetsResolved()
    {
        var c = DefaultCase();
        c.Open(DateTime.UtcNow.AddHours(4));
        c.Resolve();
        c.Status.Should().Be(CaseStatus.Resolved);
        c.DomainEvents.Should().Contain(e => e is CaseResolvedEvent);
    }

    [Fact]
    public void Close_FromResolved_SetsClosed()
    {
        var c = DefaultCase();
        c.Open(DateTime.UtcNow.AddHours(4));
        c.Resolve();
        c.Close();
        c.Status.Should().Be(CaseStatus.Closed);
        c.DomainEvents.Should().Contain(e => e is CaseClosedEvent);
    }

    [Fact]
    public void Close_FromOpen_Throws()
    {
        var c = DefaultCase();
        c.Open(DateTime.UtcNow.AddHours(4));
        var act = () => c.Close();
        act.Should().Throw<InvalidOperationException>();
    }

    // ─── Closed case is immutable ─────────────────────────────────────────────

    [Fact]
    public void Assign_OnClosedCase_Throws()
    {
        var c = DefaultCase();
        c.Open(DateTime.UtcNow.AddHours(4));
        c.Resolve();
        c.Close();
        var act = () => c.Assign(Guid.NewGuid(), UserId);
        act.Should().Throw<InvalidOperationException>().WithMessage("*closed*");
    }

    [Fact]
    public void Escalate_OnClosedCase_Throws()
    {
        var c = DefaultCase();
        c.Open(DateTime.UtcNow.AddHours(4));
        c.Resolve();
        c.Close();
        var act = () => c.Escalate(UserId, "Urgent", null);
        act.Should().Throw<InvalidOperationException>().WithMessage("*closed*");
    }

    // ─── Escalation ───────────────────────────────────────────────────────────

    [Fact]
    public void Escalate_FromOpen_SetsEscalatedAndReturnsRecord()
    {
        var c      = DefaultCase();
        c.Open(DateTime.UtcNow.AddHours(4));
        var record = c.Escalate(UserId, "Customer complained", Guid.NewGuid());

        c.Status.Should().Be(CaseStatus.Escalated);
        record.Should().NotBeNull();
        record.Reason.Should().Be("Customer complained");
        c.DomainEvents.Should().Contain(e => e is CaseEscalatedEvent);
    }
}
