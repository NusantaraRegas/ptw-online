using Ptw.Domain;

namespace Ptw.Domain.Tests;

public sealed class PermitStateMachineTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateRejectsValidityLongerThanSevenDays()
    {
        var error = Assert.Throws<DomainRuleViolationException>(() =>
            Permit.CreateDraft(ValidDraft() with { ValidUntil = Now.AddDays(7).AddTicks(1) }, Now));

        Assert.Equal("permit.validity_exceeds_seven_days", error.Code);
    }

    [Fact]
    public void SubmitMovesDirectlyToSingleHseValidationStage()
    {
        var permit = CreatePermit();
        permit.Submit("PTW-20260915-0001", ReadyToSubmit(), Now.AddMinutes(1));

        Assert.Equal(PermitStatus.UnderValidation, permit.Status);
        Assert.Contains(permit.Events, x => x.Type == "permit_submitted");
    }

    [Fact]
    public void SubmitFailsClosedWhenRequirementsAreIncomplete()
    {
        var permit = CreatePermit();
        var error = Assert.Throws<DomainRuleViolationException>(() =>
            permit.Submit("PTW-20260915-0001", new(true, false, true, ["JSA"]), Now));

        Assert.Equal("permit.submit.requirements_incomplete", error.Code);
        Assert.Equal(PermitStatus.Draft, permit.Status);
    }

    [Fact]
    public void SponsorCannotValidateOwnPermit()
    {
        var permit = SubmittedPermit();
        var error = Assert.Throws<DomainRuleViolationException>(() =>
            permit.ValidateSubmission("sponsor.demo", "Valid.", Now.AddMinutes(2)));

        Assert.Equal("permit.validation.self_validation_forbidden", error.Code);
        Assert.Equal(PermitStatus.UnderValidation, permit.Status);
    }

    [Fact]
    public void HseValidationCreatesAreaApprovalGateForSameVersion()
    {
        var permit = SubmittedPermit();
        permit.ValidateSubmission("hse.validator", "JSA dan requirement konsisten.", Now.AddMinutes(2));

        Assert.Equal(PermitStatus.AwaitingAreaApproval, permit.Status);
        Assert.Equal("hse.validator", permit.HseValidation?.ActorId);
        Assert.Equal(1, permit.Version);
    }

    [Fact]
    public void ApproveAndIssueIsOneTransitionAndRequiresAuthorizationSnapshot()
    {
        var permit = ValidatedPermit();
        permit.ApproveAndIssue(ManagerApproval(), Now.AddMinutes(3));

        Assert.Equal(PermitStatus.Issued, permit.Status);
        Assert.Equal(ApprovalCapacity.Manager, permit.Approval?.Capacity);
        Assert.Contains(permit.Events, x => x.Type == "permit_issued");
    }

    [Fact]
    public void ActingManagerRequiresFormalAssignmentId()
    {
        var permit = ValidatedPermit();
        var evidence = ManagerApproval() with
        {
            Capacity = ApprovalCapacity.ActingForManager,
            ActingAssignmentId = null
        };

        var error = Assert.Throws<DomainRuleViolationException>(() =>
            permit.ApproveAndIssue(evidence, Now.AddMinutes(3)));

        Assert.Equal("permit.approval.authorization_evidence_required", error.Code);
        Assert.Equal(PermitStatus.AwaitingAreaApproval, permit.Status);
    }

    [Fact]
    public void SponsorOrHseValidatorCannotApproveAndIssue()
    {
        var sponsorPermit = ValidatedPermit();
        var sponsorError = Assert.Throws<DomainRuleViolationException>(() =>
            sponsorPermit.ApproveAndIssue(ManagerApproval() with { ActorId = "sponsor.demo" }, Now.AddMinutes(3)));
        Assert.Equal("permit.approval.separation_of_duty", sponsorError.Code);

        var validatorPermit = ValidatedPermit();
        var validatorError = Assert.Throws<DomainRuleViolationException>(() =>
            validatorPermit.ApproveAndIssue(ManagerApproval() with { ActorId = "hse.validator" }, Now.AddMinutes(3)));
        Assert.Equal("permit.approval.separation_of_duty", validatorError.Code);
    }

    [Fact]
    public void MaterialRevisionInvalidatesHseValidation()
    {
        var permit = ValidatedPermit();
        permit.RequestRevision("JSA berubah material.", Now.AddMinutes(3));
        permit.UpdateDraft(ValidDraft() with { Title = "Versi kedua" }, Now.AddMinutes(4));
        permit.Submit("IGNORED", ReadyToSubmit(), Now.AddMinutes(5));

        Assert.Equal(PermitStatus.UnderValidation, permit.Status);
        Assert.Equal(2, permit.Version);
        Assert.Null(permit.HseValidation);
    }

    [Fact]
    public void SuspendIsImmediateAndResolveReturnsToIssued()
    {
        var permit = IssuedPermit();
        permit.Suspend("area.supervisor", "Kondisi tidak aman.", Now.AddHours(1));
        Assert.Equal(PermitStatus.Suspended, permit.Status);
        permit.ResolveSuspension("area.manager", "Kondisi dipulihkan.", Now.AddHours(2));

        Assert.Equal(PermitStatus.Issued, permit.Status);
        Assert.Null(permit.SuspensionReason);
        Assert.Contains(permit.Events, x => x.Type == "permit_suspension_resolved");
    }

    [Fact]
    public void ClosureRequiresSponsorAndHardcopyReferences()
    {
        var permit = IssuedPermit();
        var nonSponsor = Assert.Throws<DomainRuleViolationException>(() => permit.RequestClosure(
            Guid.NewGuid(), [Guid.NewGuid()], "other.user", "Lengkap.", Now.AddHours(1)));
        Assert.Equal("permit.closure.sponsor_mismatch", nonSponsor.Code);

        var missing = Assert.Throws<DomainRuleViolationException>(() => permit.RequestClosure(
            Guid.Empty, [], "sponsor.demo", "Lengkap.", Now.AddHours(1)));
        Assert.Equal("permit.closure.evidence_required", missing.Code);
    }

    [Fact]
    public void OwnerCanRequestReplacementWithoutDeletingPriorEvidenceThenClose()
    {
        var permit = IssuedPermit();
        var packageId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        permit.RequestClosure(packageId, [attachmentId], "sponsor.demo", "Semua halaman lengkap.", Now.AddHours(1));
        permit.RequestClosureEvidenceReplacement("area.manager", "Halaman handback tidak terbaca.", Now.AddHours(2));

        Assert.Equal(PermitStatus.ClosureRequested, permit.Status);
        Assert.Equal(2, permit.ClosureRequest?.Revision);
        Assert.Contains(attachmentId, permit.ClosureRequest!.AttachmentIds);
        permit.Close("area.manager", "Evidence dan handback terverifikasi.", Now.AddHours(3));
        Assert.Equal(PermitStatus.Closed, permit.Status);
    }

    [Fact]
    public void RenewalIsNewPermitWithoutWorkflowEvidenceAndCannotOverlap()
    {
        var source = IssuedPermit();
        var overlap = Permit.CreateRenewal(source.Id, ValidDraft() with
        {
            ValidFrom = source.Draft.ValidUntil.AddMinutes(-1),
            ValidUntil = source.Draft.ValidUntil.AddHours(1)
        }, Now.AddHours(1));
        var error = Assert.Throws<DomainRuleViolationException>(() => source.RequestRenewal(overlap, Now.AddHours(1)));
        Assert.Equal("permit.renewal.validity_overlap", error.Code);

        var renewal = Permit.CreateRenewal(source.Id, ValidDraft() with
        {
            ValidFrom = source.Draft.ValidUntil,
            ValidUntil = source.Draft.ValidUntil.AddHours(8)
        }, Now.AddHours(1));
        source.RequestRenewal(renewal, Now.AddHours(1));

        Assert.Equal(PermitStatus.Draft, renewal.Status);
        Assert.Null(renewal.PermitNumber);
        Assert.Null(renewal.HseValidation);
        Assert.Null(renewal.Approval);
        Assert.Null(renewal.ClosureRequest);
    }

    [Fact]
    public void ExpiredPermitCannotReturnToIssuedButCanRequestClosure()
    {
        var permit = IssuedPermit();
        permit.Expire(permit.Draft.ValidUntil);
        Assert.Equal(PermitStatus.Expired, permit.Status);

        var resume = Assert.Throws<DomainRuleViolationException>(() =>
            permit.ResolveSuspension("area.manager", "Tidak boleh.", permit.Draft.ValidUntil.AddMinutes(1)));
        Assert.Equal("permit.invalid_transition", resume.Code);

        permit.RequestClosure(
            Guid.NewGuid(), [Guid.NewGuid()], "sponsor.demo", "Hardcopy lengkap.", permit.Draft.ValidUntil.AddMinutes(2));
        Assert.Equal(PermitStatus.ClosureRequested, permit.Status);
    }

    [Fact]
    public void TerminalStatesCannotBeReopened()
    {
        var permit = IssuedPermit();
        permit.RequestClosure(Guid.NewGuid(), [Guid.NewGuid()], "sponsor.demo", "Lengkap.", Now.AddHours(1));
        permit.Close("area.manager", "Ditutup.", Now.AddHours(2));

        var error = Assert.Throws<DomainRuleViolationException>(() =>
            permit.Suspend("area.manager", "Tidak boleh.", Now.AddHours(3)));
        Assert.Equal("permit.invalid_transition", error.Code);
    }

    [Fact]
    public void DraftNormalizesV16PlanningFieldsWithoutInventingSafetyRules()
    {
        var permit = Permit.CreateDraft(ValidDraft() with
        {
            SubmitterType = " contractor ",
            WorkTypeCode = "  WELDING ",
            EquipmentTag = " P-101 ",
            PlantArea = " Process Area ",
            SimopsDeclaration = " Tidak ada SIMOPS ",
            SafetyEquipmentCodes = ["APAR", " APAR ", "FIRE_WATCH"],
            IsolationPrecautionCodes = ["LOTO"],
            JsaDocumentNumber = " JSA-001 ",
            JsaRevision = " 2 ",
            JsaDate = Now
        }, Now);

        Assert.Equal("CONTRACTOR", permit.Draft.SubmitterType);
        Assert.Equal("WELDING", permit.Draft.WorkTypeCode);
        Assert.Equal(["APAR", "FIRE_WATCH"], permit.Draft.SafetyEquipmentCodes);
        Assert.Equal("JSA-001", permit.Draft.JsaDocumentNumber);
    }

    [Fact]
    public void DraftRejectsUnknownSubmitterType()
    {
        var error = Assert.Throws<DomainRuleViolationException>(() =>
            Permit.CreateDraft(ValidDraft() with { SubmitterType = "INTERNAL_VENDOR" }, Now));

        Assert.Equal("permit.submitter_type_invalid", error.Code);
    }

    private static Permit CreatePermit() => Permit.CreateDraft(ValidDraft(), Now);

    private static Permit SubmittedPermit()
    {
        var permit = CreatePermit();
        permit.Submit("PTW-20260915-0001", ReadyToSubmit(), Now.AddMinutes(1));
        return permit;
    }

    private static Permit ValidatedPermit()
    {
        var permit = SubmittedPermit();
        permit.ValidateSubmission("hse.validator", "JSA dan requirement konsisten.", Now.AddMinutes(2));
        return permit;
    }

    private static Permit IssuedPermit()
    {
        var permit = ValidatedPermit();
        permit.ApproveAndIssue(ManagerApproval(), Now.AddMinutes(3));
        return permit;
    }

    private static SubmissionReadiness ReadyToSubmit() => new(true, true, true, []);

    private static PermitApprovalEvidence ManagerApproval() => new(
        "area.manager",
        "Manager Distribusi Gas & Pengelolaan ORF",
        ApprovalCapacity.Manager,
        "area.manager",
        "Manager Distribusi Gas & Pengelolaan ORF",
        Guid.NewGuid(),
        null,
        "rules-test-v1",
        "print-test-v1",
        "campaign-test-v1",
        "Saya menyetujui dan menerbitkan PTW ini.",
        Now);

    private static PermitDraft ValidDraft() => new(
        "Pengelasan support pipa",
        "Pengelasan support pada process area sesuai JSA",
        "ORF",
        "sponsor.demo",
        "Budi Pelaksana",
        "PT Mitra Kerja",
        PermitClass.HotWork,
        RiskLevel.High,
        Now,
        Now.AddDays(1),
        "esimi-123",
        "ESM-2026-00123",
        [],
        [],
        ["JSA"]);
}
