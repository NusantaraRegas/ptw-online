using Ptw.Domain;

namespace Ptw.Domain.Tests;

public sealed class UserAuthorizationAssignmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SameSubjectCanHoldMultipleDifferentRoles()
    {
        var issuer = Create("operator.satu", "PTW_ISSUER", ["permit.issue"]);
        var gasTester = Create("operator.satu", "GAS_TESTER", ["gas-test.record"]);

        Assert.Equal(issuer.SubjectId, gasTester.SubjectId);
        Assert.NotEqual(issuer.RoleCode, gasTester.RoleCode);
        Assert.NotEqual(issuer.Id, gasTester.Id);
    }

    [Fact]
    public void MakerCannotApproveOwnAssignment()
    {
        var assignment = Create();
        assignment.SubmitForApproval("admin.maker", Now.AddMinutes(1));

        var exception = Assert.Throws<DomainRuleViolationException>(() =>
            assignment.Approve("admin.maker", Now.AddMinutes(2)));

        Assert.Equal("authorization.maker_checker_required", exception.Code);
        Assert.Equal(AuthorizationAssignmentStatus.PendingApproval, assignment.Status);
    }

    [Fact]
    public void ApprovedAssignmentIsEffectiveOnlyInsideItsPeriod()
    {
        var assignment = Create();
        assignment.SubmitForApproval("admin.maker", Now.AddMinutes(1));
        assignment.Approve("admin.checker", Now.AddMinutes(2));

        Assert.False(assignment.IsEffectiveAt(Now.AddMinutes(-1)));
        Assert.True(assignment.IsEffectiveAt(Now.AddHours(1)));
        Assert.False(assignment.IsEffectiveAt(Now.AddDays(2)));
    }

    [Fact]
    public void DelegationRequiresSourceAndFiniteEnd()
    {
        var sourceMissing = Assert.Throws<DomainRuleViolationException>(() =>
            Create(kind: AuthorizationAssignmentKind.Delegation));
        Assert.Equal("authorization.delegation_source_required", sourceMissing.Code);

        var endMissing = Assert.Throws<DomainRuleViolationException>(() =>
            UserAuthorizationAssignment.CreateDraft(
                "operator.satu",
                "PTW_ISSUER",
                ["permit.issue"],
                null,
                false,
                [],
                AuthorizationAssignmentKind.Delegation,
                Guid.NewGuid(),
                Now,
                null,
                "admin.maker",
                Now));
        Assert.Equal("authorization.delegation_end_required", endMissing.Code);
    }

    [Fact]
    public void ApprovedAssignmentCannotBeEditedInPlace()
    {
        var assignment = Create();
        assignment.SubmitForApproval("admin.maker", Now.AddMinutes(1));
        assignment.Approve("admin.checker", Now.AddMinutes(2));

        var exception = Assert.Throws<DomainRuleViolationException>(() => assignment.UpdateDraft(
            assignment.SubjectId,
            assignment.RoleCode,
            ["permit.close"],
            null,
            false,
            [],
            AuthorizationAssignmentKind.Direct,
            null,
            Now,
            Now.AddDays(1),
            "admin.editor",
            Now.AddMinutes(3)));

        Assert.Equal("authorization.invalid_transition", exception.Code);
    }

    private static UserAuthorizationAssignment Create(
        string subjectId = "operator.satu",
        string roleCode = "PTW_ISSUER",
        IReadOnlyList<string>? actionCodes = null,
        AuthorizationAssignmentKind kind = AuthorizationAssignmentKind.Direct,
        Guid? sourceAuthorizationId = null) =>
        UserAuthorizationAssignment.CreateDraft(
            subjectId,
            roleCode,
            actionCodes ?? ["permit.issue"],
            null,
            false,
            [],
            kind,
            sourceAuthorizationId,
            Now,
            Now.AddDays(1),
            "admin.maker",
            Now);
}
