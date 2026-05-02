using CrmPlatform.MarketingService.Application.Journeys;
using CrmPlatform.MarketingService.Domain.Entities;
using CrmPlatform.MarketingService.Domain.Enums;
using CrmPlatform.MarketingService.Infrastructure.Data;
using CrmPlatform.ServiceTemplate.Domain;
using CrmPlatform.ServiceTemplate.Infrastructure.Messaging;
using CrmPlatform.ServiceTemplate.Infrastructure.MultiTenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace CrmPlatform.MarketingService.Tests.Application;

public sealed class EnrollContactHandlerTests
{
    private static MarketingDbContext CreateContext(string db, Guid tenantId)
    {
        var opts = new DbContextOptionsBuilder<MarketingDbContext>()
            .UseInMemoryDatabase(db).Options;
        var accessor = new Mock<ITenantContextAccessor>();
        accessor.Setup(a => a.TenantId).Returns(tenantId);
        return new MarketingDbContext(opts, accessor.Object);
    }

    private static Mock<ITenantContext> MakeCtx(Guid tenantId) =>
        new Mock<ITenantContext>().Also(m => m.Setup(c => c.TenantId).Returns(tenantId));

    private static Journey PublishedJourney(Guid tenantId, Guid campaignId)
    {
        var j = Journey.Create(tenantId, campaignId, "J", "desc", Guid.NewGuid());
        j.SetSteps("[{}]", 1);
        j.Publish();
        return j;
    }

    [Fact]
    public async Task Enroll_HappyPath_ReturnsEnrollmentId()
    {
        var tenantId   = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        using var db   = CreateContext("enroll-happy", tenantId);

        var journey = PublishedJourney(tenantId, campaignId);
        db.Journeys.Add(journey);
        await db.SaveChangesAsync();

        var handler = new EnrollContactHandler(
            db, MakeCtx(tenantId).Object,
            new Mock<ServiceBusEventPublisher>().Object,
            new Mock<Microsoft.Extensions.Logging.ILogger<EnrollContactHandler>>().Object);

        var result = await handler.HandleAsync(
            new EnrollContactCommand(journey.Id, Guid.NewGuid()));

        result.IsSuccess.Should().BeTrue();
        result.Value!.EnrollmentId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Enroll_UnpublishedJourney_ReturnsValidationError()
    {
        var tenantId   = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        using var db   = CreateContext("enroll-unpublished", tenantId);

        var journey = Journey.Create(tenantId, campaignId, "J", "desc", Guid.NewGuid());
        db.Journeys.Add(journey);
        await db.SaveChangesAsync();

        var handler = new EnrollContactHandler(
            db, MakeCtx(tenantId).Object,
            new Mock<ServiceBusEventPublisher>().Object,
            new Mock<Microsoft.Extensions.Logging.ILogger<EnrollContactHandler>>().Object);

        var result = await handler.HandleAsync(
            new EnrollContactCommand(journey.Id, Guid.NewGuid()));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCode.ValidationError);
    }

    [Fact]
    public async Task Enroll_Duplicate_ReturnsConflict()
    {
        var tenantId   = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        var contactId  = Guid.NewGuid();
        using var db   = CreateContext("enroll-duplicate", tenantId);

        var journey = PublishedJourney(tenantId, campaignId);
        db.Journeys.Add(journey);
        var existing = JourneyEnrollment.Create(tenantId, journey.Id, contactId);
        db.Enrollments.Add(existing);
        await db.SaveChangesAsync();

        var handler = new EnrollContactHandler(
            db, MakeCtx(tenantId).Object,
            new Mock<ServiceBusEventPublisher>().Object,
            new Mock<Microsoft.Extensions.Logging.ILogger<EnrollContactHandler>>().Object);

        var result = await handler.HandleAsync(new EnrollContactCommand(journey.Id, contactId));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCode.Conflict);
    }

    [Fact]
    public async Task Enroll_JourneyNotFound_ReturnsNotFound()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext("enroll-notfound", tenantId);

        var handler = new EnrollContactHandler(
            db, MakeCtx(tenantId).Object,
            new Mock<ServiceBusEventPublisher>().Object,
            new Mock<Microsoft.Extensions.Logging.ILogger<EnrollContactHandler>>().Object);

        var result = await handler.HandleAsync(
            new EnrollContactCommand(Guid.NewGuid(), Guid.NewGuid()));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCode.NotFound);
    }
}

// Extension helper — keeps test setup concise
file static class MockExtensions
{
    public static T Also<T>(this T obj, Action<T> setup) { setup(obj); return obj; }
}
