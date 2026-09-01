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
            permit.OpenWorkPeriod(notReady, "issuer.user", Now.AddHours(1)));

        Assert.Equal("permit.issue.guards_failed", error.Code);
        Assert.Equal(PermitStatus.Approved, permit.Status);
    }

    [Fact]
    public void SuspendStopsActivePeriodAndResolutionRequiresNewOpen()
    {
        var permit = ApprovedPermit();
        permit.OpenWorkPeriod(ReadyForField(), "issuer.user", Now.AddHours(1));

        permit.Suspend("Perubahan kondisi SIMOPS", Now.AddHours(2));

        Assert.Equal(PermitStatus.Suspended, permit.Status);
        Assert.Null(permit.ActiveWorkPeriodId);

        permit.ResolveSuspension("SIMOPS selesai dan lokasi diverifikasi ulang", Now.AddHours(3));

        Assert.Equal(PermitStatus.ReadyForIssue, permit.Status);
        Assert.Null(permit.ActiveWorkPeriodId);
    }

    [Fact]
    public void ClosedIsTerminal()
    {
        var permit = ApprovedPermit();
        permit.OpenWorkPeriod(ReadyForField(), "issuer.user", Now.AddHours(1));
        permit.CloseWorkPeriod(false, Now.AddHours(2));
        permit.AcceptHandback(new HandbackReadiness(true, true, true, true, true), Now.AddHours(3));

        Assert.Equal(PermitStatus.Closed, permit.Status);
        var error = Assert.Throws<DomainRuleViolationException>(() => permit.MarkReadyForIssue(Now.AddHours(4)));
        Assert.Equal("permit.invalid_transition", error.Code);
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
    public void ParallelValidationsRequireBothDisciplinesBeforeApproval()
    {
        var permit = SubmittedForReview();

        permit.EndorseValidation(
            PermitValidationKind.GasDistribution,
            "gas.validator",
            "Kontrol operasional telah diverifikasi.",
            Now.AddMinutes(3));

        Assert.Equal(PermitStatus.UnderReview, permit.Status);
        Assert.Null(permit.HsseValidation);
        Assert.NotNull(permit.GasDistributionValidation);
        var earlyApproval = Assert.Throws<DomainRuleViolationException>(() =>
            permit.Approve("area.owner", "Disetujui pemilik area.", Now.AddMinutes(4)));
        Assert.Equal("permit.invalid_transition", earlyApproval.Code);

        permit.EndorseValidation(
            PermitValidationKind.Hsse,
            "hsse.validator",
            "Persyaratan HSSE telah diverifikasi.",
            Now.AddMinutes(5));

        Assert.Equal(PermitStatus.AwaitingApproval, permit.Status);
        permit.Approve("area.owner", "Disetujui pemilik area.", Now.AddMinutes(6));
        Assert.Equal(PermitStatus.Approved, permit.Status);
        Assert.Null(permit.ActiveWorkPeriodId);
    }

    [Fact]
    public void DuplicateValidationIsRejected()
    {
        var permit = SubmittedForReview();
        permit.EndorseValidation(
            PermitValidationKind.Hsse,
            "hsse.validator",
            "Validasi pertama.",
            Now.AddMinutes(3));

        var duplicate = Assert.Throws<DomainRuleViolationException>(() => permit.EndorseValidation(
            PermitValidationKind.Hsse,
            "hsse.validator.other",
            "Validasi duplikat.",
            Now.AddMinutes(4)));

        Assert.Equal("permit.validation.already_completed", duplicate.Code);
        Assert.Equal(PermitStatus.UnderReview, permit.Status);
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
        permit.EndorseValidation(
            PermitValidationKind.GasDistribution,
            "gas.validator",
            "Kontrol operasional telah diverifikasi.",
            Now.AddMinutes(4));
        permit.Approve("area.owner", "Disetujui pemilik area.", Now.AddMinutes(5));
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
