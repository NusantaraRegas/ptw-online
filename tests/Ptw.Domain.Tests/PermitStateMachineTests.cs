using Ptw.Domain;

namespace Ptw.Domain.Tests;

public sealed class PermitStateMachineTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateRejectsValidityLongerThanSevenDays()
    {
        var draft = ValidDraft() with { ValidUntil = Now.AddDays(7).AddSeconds(1) };

        var error = Assert.Throws<DomainRuleViolationException>(() => Permit.CreateDraft(draft, Now));

        Assert.Equal("permit.validity_exceeds_seven_days", error.Code);
    }

    [Fact]
    public void SubmitFailsClosedWhenRequirementsAreIncomplete()
    {
        var permit = CreatePermit();
        var readiness = new SubmissionReadiness(true, false, true, ["JSA"]);

        var error = Assert.Throws<DomainRuleViolationException>(() => permit.Submit("PTW-20260826-0001", readiness, Now));

        Assert.Equal("permit.submit.requirements_incomplete", error.Code);
        Assert.Equal(PermitStatus.Draft, permit.Status);
        Assert.Null(permit.PermitNumber);
    }

    [Fact]
    public void AttachmentChangesCreateNewDraftVersions()
    {
        var permit = CreatePermit();
        var attachmentId = Guid.NewGuid();

        permit.AddAttachment(attachmentId, Now.AddMinutes(1));
        permit.RemoveAttachment(attachmentId, Now.AddMinutes(2));

        Assert.Equal(3, permit.Version);
        Assert.Contains(permit.Events, x => x.Type == "permit_attachment_added");
        Assert.Contains(permit.Events, x => x.Type == "permit_attachment_removed");
    }

    [Fact]
    public void AttachmentCannotChangeAfterSubmit()
    {
        var permit = SubmittedForReview();

        var addError = Assert.Throws<DomainRuleViolationException>(() =>
            permit.AddAttachment(Guid.NewGuid(), Now.AddMinutes(3)));
        var removeError = Assert.Throws<DomainRuleViolationException>(() =>
            permit.RemoveAttachment(Guid.NewGuid(), Now.AddMinutes(3)));

        Assert.Equal("permit.invalid_transition", addError.Code);
        Assert.Equal("permit.invalid_transition", removeError.Code);
        Assert.Equal(1, permit.Version);
    }

    [Fact]
    public void OpenPermitCanRequestNonOverlappingRenewalDraft()
    {
        var source = ApprovedPermit();
        source.OpenWorkPeriod(ReadyForField(), "area.owner", Now.AddHours(1));
        var renewal = Permit.CreateRenewal(
            source.Id,
            ValidDraft() with
            {
                ValidFrom = source.Draft.ValidUntil,
                ValidUntil = source.Draft.ValidUntil.AddHours(8)
            },
            Now.AddHours(2));

        source.RequestRenewal(renewal, Now.AddHours(2));

        Assert.Equal(renewal.Id, source.RenewalPermitId);
        Assert.Equal(source.Id, renewal.RenewedFromPermitId);
        Assert.Equal(PermitStatus.Draft, renewal.Status);
        Assert.Null(renewal.PermitNumber);
        Assert.Equal(2, source.Version);
        Assert.Contains(source.Events, x => x.Type == "permit_renewal_requested");
        Assert.Contains(renewal.Events, x => x.Type == "permit_renewal_draft_created");
    }

    [Fact]
    public void RenewalRejectsOverlapAndDuplicateRequest()
    {
        var source = ApprovedPermit();
        source.OpenWorkPeriod(ReadyForField(), "area.owner", Now.AddHours(1));
        var overlapping = Permit.CreateRenewal(
            source.Id,
            ValidDraft() with
            {
                ValidFrom = source.Draft.ValidUntil.AddMinutes(-1),
                ValidUntil = source.Draft.ValidUntil.AddHours(8)
            },
            Now.AddHours(2));

        var overlap = Assert.Throws<DomainRuleViolationException>(() =>
            source.RequestRenewal(overlapping, Now.AddHours(2)));
        Assert.Equal("permit.renewal.validity_overlap", overlap.Code);

        var valid = Permit.CreateRenewal(
            source.Id,
            ValidDraft() with
            {
                ValidFrom = source.Draft.ValidUntil,
                ValidUntil = source.Draft.ValidUntil.AddHours(8)
            },
            Now.AddHours(2));
        source.RequestRenewal(valid, Now.AddHours(2));
        var duplicate = Permit.CreateRenewal(
            source.Id,
            valid.Draft,
            Now.AddHours(3));

        var duplicateError = Assert.Throws<DomainRuleViolationException>(() =>
            source.RequestRenewal(duplicate, Now.AddHours(3)));
        Assert.Equal("permit.renewal.already_requested", duplicateError.Code);
    }

    [Fact]
    public void ApprovalDoesNotOpenThePermit()
    {
        var permit = ApprovedPermit();

        Assert.Equal(PermitStatus.Approved, permit.Status);
        Assert.Null(permit.ActiveWorkPeriodId);
    }

    [Fact]
    public void OpenRechecksEveryFieldGuard()
    {
        var permit = ApprovedPermit();
        var notReady = ReadyForField() with { GasTestSatisfied = false };

        var error = Assert.Throws<DomainRuleViolationException>(() =>
            permit.OpenWorkPeriod(notReady, "area.owner", Now.AddHours(1)));

        Assert.Equal("permit.issue.guards_failed", error.Code);
        Assert.Equal(PermitStatus.Approved, permit.Status);
    }

    [Fact]
    public void SponsorSuspensionRequestStopsWorkBeforeAreaOwnerApproval()
    {
        var permit = ApprovedPermit();
        permit.OpenWorkPeriod(ReadyForField(), "area.owner", Now.AddHours(1));

        permit.RequestSuspension("sponsor.demo", "Perubahan kondisi SIMOPS", Now.AddHours(2));

        Assert.Equal(PermitStatus.SuspensionRequested, permit.Status);
        Assert.Null(permit.ActiveWorkPeriodId);
        permit.ApproveSuspension(
            "area.owner",
            "Penangguhan dikonfirmasi pemilik area.",
            Now.AddHours(2).AddMinutes(5));
        Assert.Equal(PermitStatus.Suspended, permit.Status);

        permit.ResolveSuspension("SIMOPS selesai dan lokasi diverifikasi ulang", Now.AddHours(3));

        Assert.Equal(PermitStatus.ReadyForIssue, permit.Status);
        Assert.Null(permit.ActiveWorkPeriodId);
    }

    [Fact]
    public void NonSponsorCannotRequestSuspensionAndWorkRemainsActive()
    {
        var permit = ApprovedPermit();
        var workPeriodId = permit.OpenWorkPeriod(ReadyForField(), "area.owner", Now.AddHours(1));

        var error = Assert.Throws<DomainRuleViolationException>(() => permit.RequestSuspension(
            "sponsor.other",
            "Kondisi berubah.",
            Now.AddHours(2)));

        Assert.Equal("permit.suspension.sponsor_mismatch", error.Code);
        Assert.Equal(PermitStatus.Open, permit.Status);
        Assert.Equal(workPeriodId, permit.ActiveWorkPeriodId);
    }

    [Fact]
    public void ClosedIsTerminal()
    {
        var permit = ApprovedPermit();
        permit.OpenWorkPeriod(ReadyForField(), "area.owner", Now.AddHours(1));
        permit.DeclareCompletion(
            "sponsor.demo",
            "Pekerjaan dinyatakan selesai.",
            Now.AddHours(2));
        permit.ConfirmCompletion(
            PermitCompletionKind.Hsse,
            "hsse.validator",
            "Kondisi akhir aman.",
            Now.AddHours(2).AddMinutes(10));
        permit.ConfirmCompletion(
            PermitCompletionKind.AreaOwner,
            "area.owner",
            "Area diterima kembali.",
            Now.AddHours(2).AddMinutes(20));
        permit.Close("area.owner", "PTW ditutup.", Now.AddHours(3));

        Assert.Equal(PermitStatus.Closed, permit.Status);
        var error = Assert.Throws<DomainRuleViolationException>(() => permit.MarkReadyForIssue(Now.AddHours(4)));
        Assert.Equal("permit.invalid_transition", error.Code);
    }

    [Fact]
    public void CompletionConfirmationsCanRunInParallelButBothAreRequired()
    {
        var permit = ApprovedPermit();
        permit.OpenWorkPeriod(ReadyForField(), "area.owner", Now.AddHours(1));
        permit.DeclareCompletion(
            "sponsor.demo",
            "Pekerjaan dinyatakan selesai.",
            Now.AddHours(2));

        permit.ConfirmCompletion(
            PermitCompletionKind.AreaOwner,
            "area.owner",
            "Area selesai diperiksa.",
            Now.AddHours(2).AddMinutes(10));

        Assert.Equal(PermitStatus.CompletionConfirmationPending, permit.Status);
        Assert.NotNull(permit.AreaOwnerCompletion);
        Assert.Null(permit.HsseCompletion);
        var earlyClose = Assert.Throws<DomainRuleViolationException>(() => permit.Close(
            "area.owner",
            "Tutup terlalu awal.",
            Now.AddHours(2).AddMinutes(15)));
        Assert.Equal("permit.invalid_transition", earlyClose.Code);

        permit.ConfirmCompletion(
            PermitCompletionKind.Hsse,
            "hsse.validator",
            "Kondisi akhir aman.",
            Now.AddHours(2).AddMinutes(20));

        Assert.Equal(PermitStatus.WorkCompleted, permit.Status);
    }

    [Fact]
    public void UpdatingADraftCreatesANewVersion()
    {
        var permit = CreatePermit();

        permit.UpdateDraft(ValidDraft() with { Title = "Versi kedua" }, Now.AddMinutes(5));

        Assert.Equal(2, permit.Version);
        Assert.Equal("Versi kedua", permit.Draft.Title);
    }

    [Fact]
    public void HsseValidationAloneMovesPermitToAreaApproval()
    {
        var permit = SubmittedForReview();

        var earlyApproval = Assert.Throws<DomainRuleViolationException>(() =>
            permit.Approve("area.owner", "Disetujui pemilik area.", Now.AddMinutes(3)));
        Assert.Equal("permit.invalid_transition", earlyApproval.Code);

        permit.EndorseValidation(
            PermitValidationKind.Hsse,
            "hsse.validator",
            "Persyaratan HSSE telah diverifikasi.",
            Now.AddMinutes(4));

        Assert.Equal(PermitStatus.AwaitingApproval, permit.Status);
        Assert.Null(permit.GasDistributionValidation);
        permit.Approve("area.owner", "Disetujui pemilik area.", Now.AddMinutes(5));
        Assert.Equal(PermitStatus.Approved, permit.Status);
        Assert.Null(permit.ActiveWorkPeriodId);
    }

    [Fact]
    public void RetiredGasValidationIsRejectedFailSafe()
    {
        var permit = SubmittedForReview();

        var retired = Assert.Throws<DomainRuleViolationException>(() => permit.EndorseValidation(
            PermitValidationKind.GasDistribution,
            "gas.validator",
            "Validasi yang sudah tidak berlaku.",
            Now.AddMinutes(3)));

        Assert.Equal("permit.validation.retired", retired.Code);
        Assert.Equal(PermitStatus.UnderReview, permit.Status);
    }

    [Fact]
    public void OnlyTheApprovingAreaOwnerCanIssueThePermit()
    {
        var permit = ApprovedPermit();

        var error = Assert.Throws<DomainRuleViolationException>(() =>
            permit.OpenWorkPeriod(ReadyForField(), "area.owner.other", Now.AddHours(1)));

        Assert.Equal("permit.issue.area_owner_mismatch", error.Code);
        Assert.Equal(PermitStatus.Approved, permit.Status);
        Assert.Null(permit.ActiveWorkPeriodId);
    }

    [Fact]
    public void MaterialRevisionInvalidatesBothValidationsAndRequiresFreshReviewForNewVersion()
    {
        var permit = SubmittedForReview();
        permit.EndorseValidation(
            PermitValidationKind.Hsse,
            "multi.validator",
            "Validasi HSSE versi pertama.",
            Now.AddMinutes(3));

        permit.RequestRevision("Kontrol pekerjaan berubah material.", Now.AddMinutes(4));

        Assert.Equal(PermitStatus.RevisionRequired, permit.Status);
        Assert.Null(permit.HsseValidation);
        Assert.Null(permit.GasDistributionValidation);
        permit.UpdateDraft(ValidDraft() with { Controls = ["Isolasi energi tambahan"] }, Now.AddMinutes(5));
        permit.Submit(
            "IGNORED-BECAUSE-NUMBER-ALREADY-ALLOCATED",
            new SubmissionReadiness(true, true, true, []),
            Now.AddMinutes(6));
        permit.StartReview(Now.AddMinutes(7));

        Assert.Equal(2, permit.Version);
        Assert.Equal(PermitStatus.UnderReview, permit.Status);
        Assert.Null(permit.HsseValidation);
        Assert.Null(permit.GasDistributionValidation);
        Assert.Contains(permit.Events, x => x.Type == "review_started");
    }

    [Fact]
    public void RejectRequiresReasonAndProducesTerminalState()
    {
        var permit = SubmittedForReview();

        var missingReason = Assert.Throws<DomainRuleViolationException>(() =>
            permit.Reject(" ", Now.AddMinutes(3)));

        Assert.Equal("permit.reason_required", missingReason.Code);
        Assert.Equal(PermitStatus.UnderReview, permit.Status);
        permit.Reject("Risiko residual tidak dapat diterima.", Now.AddMinutes(4));
        Assert.Equal(PermitStatus.Rejected, permit.Status);
        var terminal = Assert.Throws<DomainRuleViolationException>(() =>
            permit.RequestRevision("Coba buka kembali.", Now.AddMinutes(5)));
        Assert.Equal("permit.invalid_transition", terminal.Code);
    }

    private static Permit CreatePermit() => Permit.CreateDraft(ValidDraft(), Now);

    private static Permit ApprovedPermit()
    {
        var permit = SubmittedForReview();
        permit.EndorseValidation(
            PermitValidationKind.Hsse,
            "hsse.validator",
            "Persyaratan HSSE telah diverifikasi.",
            Now.AddMinutes(3));
        permit.Approve("area.owner", "Disetujui pemilik area.", Now.AddMinutes(4));
        return permit;
    }

    private static Permit SubmittedForReview()
    {
        var permit = CreatePermit();
        permit.Submit(
            "PTW-20260826-0001",
            new SubmissionReadiness(true, true, true, []),
            Now.AddMinutes(1));
        permit.StartReview(Now.AddMinutes(2));
        return permit;
    }

    private static FieldIssueReadiness ReadyForField() =>
        new(true, true, true, true, true, true, true, true, false);

    private static PermitDraft ValidDraft() => new(
        "Pengelasan support pipa",
        "Pengelasan support pada process area sesuai JSA",
        "PROCESS-AREA-A",
        "sponsor.demo",
        "Budi Pelaksana",
        "PT Mitra Kerja",
        PermitClass.HotWork,
        RiskLevel.High,
        Now,
        Now.AddDays(1),
        "esimi-123",
        "ESM-2026-00123",
        ["Api terbuka"],
        ["Fire watch", "APAR"],
        ["JSA"]);
}
