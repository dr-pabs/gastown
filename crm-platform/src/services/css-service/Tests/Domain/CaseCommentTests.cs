using CrmPlatform.CssService.Domain.Entities;
using CrmPlatform.CssService.Domain.Enums;
using FluentAssertions;

namespace CrmPlatform.CssService.Tests.Domain;

public sealed class CaseCommentTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CaseId   = Guid.NewGuid();
    private static readonly Guid StaffId  = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();

    [Fact]
    public void Staff_CanCreate_InternalComment()
    {
        var comment = CaseComment.Create(
            TenantId, CaseId, StaffId, CommentAuthorType.Staff, "Internal note", isInternal: true);

        comment.IsInternal.Should().BeTrue();
        comment.AuthorType.Should().Be(CommentAuthorType.Staff);
    }

    [Fact]
    public void Staff_CanCreate_PublicComment()
    {
        var comment = CaseComment.Create(
            TenantId, CaseId, StaffId, CommentAuthorType.Staff, "Public reply", isInternal: false);

        comment.IsInternal.Should().BeFalse();
    }

    [Fact]
    public void Customer_CanCreate_PublicComment()
    {
        var comment = CaseComment.Create(
            TenantId, CaseId, CustomerId, CommentAuthorType.Customer, "My reply", isInternal: false);

        comment.IsInternal.Should().BeFalse();
        comment.AuthorType.Should().Be(CommentAuthorType.Customer);
    }

    [Fact]
    public void Customer_CannotCreate_InternalComment()
    {
        var act = () => CaseComment.Create(
            TenantId, CaseId, CustomerId, CommentAuthorType.Customer, "Sneaky internal", isInternal: true);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*internal*");
    }

    [Fact]
    public void Create_ThrowsOnEmptyBody()
    {
        var act = () => CaseComment.Create(
            TenantId, CaseId, StaffId, CommentAuthorType.Staff, "  ", isInternal: false);

        act.Should().Throw<ArgumentException>();
    }
}
