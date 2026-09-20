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
            permit.ValidateSubmission(
                "sponsor.demo",
                "Valid.",
                ["SAFETY_FIRE_EXTINGUISHER"],
                Now.AddMinutes(2)));

        Assert.Equal("permit.validation.self_validation_forbidden", error.Code);
        Assert.Equal(PermitStatus.UnderValidation, permit.Status);
    }

    [Fact]
    public void HseValidationCreatesAreaApprovalGateForSameVersion()
    {
        var permit = SubmittedPermit();
        permit.ValidateSubmission(
            "hse.validator",
            "JSA dan requirement konsisten.",
            ["SAFETY_FIRE_EXTINGUISHER", "SAFETY_LOTO"],
            Now.AddMinutes(2));

        Assert.Equal(PermitStatus.AwaitingAreaApproval, permit.Status);
        Assert.Equal("hse.validator", permit.HseValidation?.ActorId);
        Assert.Equal(
            ["SAFETY_FIRE_EXTINGUISHER", "SAFETY_LOTO"],
            permit.HseValidation?.SafetyEquipmentCodes);
        Assert.Empty(permit.Draft.SafetyEquipmentCodes ?? []);
        Assert.Equal(1, permit.Version);
    }

    [Theory]
    [InlineData(null, "permit.safety_equipment_required")]
    [InlineData("SAFETY_NOT_IN_TEMPLATE", "permit.safety_equipment_invalid")]
    public void HseValidationRejectsMissingOrUnknownSafetyEquipment(
        string? safetyEquipmentCode,
        string expectedCode)
    {
        var permit = SubmittedPermit();
        string[] selected = safetyEquipmentCode is null ? [] : [safetyEquipmentCode];

        var error = Assert.Throws<DomainRuleViolationException>(() =>
            permit.ValidateSubmission(
                "hse.validator",
                "JSA dan requirement konsisten.",
                selected,
                Now.AddMinutes(2)));

        Assert.Equal(expectedCode, error.Code);
        Assert.Equal(PermitStatus.UnderValidation, permit.Status);
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
    public void ApprovalRejectsLegacyHseEvidenceWithoutBagian5Selection()
    {
        var permit = Permit.Rehydrate(
            Guid.NewGuid(),
            "PTW-20260915-0001",
            PermitStatus.AwaitingAreaApproval,
            1,
            ValidDraft(),
            Now,
            Now.AddMinutes(2),
            hseValidation: new PermitValidationEvidence(
                "hse.validator",
                "Validasi legacy tanpa pilihan Bagian 5.",
                Now.AddMinutes(2)));

        var error = Assert.Throws<DomainRuleViolationException>(() =>
            permit.ApproveAndIssue(ManagerApproval(), Now.AddMinutes(3)));

        Assert.Equal("permit.safety_equipment_required", error.Code);
        Assert.Contains("Minta revisi", error.Message);
        Assert.Equal(PermitStatus.AwaitingAreaApproval, permit.Status);
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
    public void RenewalRequiresOwnerApprovalBeforeNewDraftAndCannotOverlap()
    {
        var source = IssuedPermit();
        var packageId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        var error = Assert.Throws<DomainRuleViolationException>(() => source.RequestRenewal(
            packageId,
            [attachmentId],
            "sponsor.demo",
            "Pekerjaan perlu dilanjutkan.",
            source.Draft.ValidUntil.AddMinutes(-1),
            source.Draft.ValidUntil.AddHours(1),
            Now.AddHours(1)));
        Assert.Equal("permit.renewal.validity_overlap", error.Code);

        source.RequestRenewal(
            packageId,
            [attachmentId],
            "sponsor.demo",
            "Pekerjaan perlu dilanjutkan.",
            source.Draft.ValidUntil,
            source.Draft.ValidUntil.AddHours(8),
            Now.AddHours(1));
        Assert.Null(source.RenewalPermitId);
        Assert.Equal(PermitRenewalReviewStatus.Pending, source.RenewalRequest?.Status);

        var renewal = Permit.CreateRenewal(source.Id, ValidDraft() with
        {
            ValidFrom = source.Draft.ValidUntil,
            ValidUntil = source.Draft.ValidUntil.AddHours(8)
        }, Now.AddHours(2));
        source.ApproveRenewal(renewal, "area.manager", "Hardcopy terverifikasi.", Now.AddHours(2));

        Assert.Equal(PermitStatus.Draft, renewal.Status);
        Assert.Equal(renewal.Id, source.RenewalPermitId);
        Assert.Equal(PermitRenewalReviewStatus.Approved, source.RenewalRequest?.Status);
        Assert.Null(renewal.PermitNumber);
        Assert.Null(renewal.HseValidation);
        Assert.Null(renewal.Approval);
        Assert.Null(renewal.ClosureRequest);
    }

    [Fact]
    public void ClosureAndPendingRenewalAreMutuallyExclusive()
    {
        var permit = IssuedPermit();
        permit.RequestRenewal(
            Guid.NewGuid(),
            [Guid.NewGuid()],
            "sponsor.demo",
            "Pekerjaan perlu dilanjutkan.",
            permit.Draft.ValidUntil,
            permit.Draft.ValidUntil.AddHours(4),
            Now.AddHours(1));

        var error = Assert.Throws<DomainRuleViolationException>(() => permit.RequestClosure(
            Guid.NewGuid(),
            [Guid.NewGuid()],
            "sponsor.demo",
            "Pekerjaan selesai.",
            Now.AddHours(2)));
        Assert.Equal("permit.closure.renewal_conflict", error.Code);
    }

    [Fact]
    public void AreaOwnerCanRequestNewRenewalEvidenceAndSponsorCanResubmit()
    {
        var permit = IssuedPermit();
        var originalAttachmentId = Guid.NewGuid();
        permit.RequestRenewal(
            Guid.NewGuid(),
            [originalAttachmentId],
            "sponsor.demo",
            "Pekerjaan perlu dilanjutkan.",
            permit.Draft.ValidUntil,
            permit.Draft.ValidUntil.AddHours(4),
            Now.AddHours(1));

        permit.RequestRenewalEvidenceReplacement(
            "area.manager",
            "Halaman verifikasi lapangan belum terbaca.",
            Now.AddHours(2));
        Assert.Equal(PermitRenewalReviewStatus.RevisionRequired, permit.RenewalRequest?.Status);

        var unchanged = Assert.Throws<DomainRuleViolationException>(() => permit.RequestRenewal(
            Guid.NewGuid(),
            [originalAttachmentId],
            "sponsor.demo",
            "Evidence lama dikirim ulang.",
            permit.Draft.ValidUntil,
            permit.Draft.ValidUntil.AddHours(4),
            Now.AddHours(3)));
        Assert.Equal("permit.renewal.evidence_not_replaced", unchanged.Code);

        permit.RequestRenewal(
            Guid.NewGuid(),
            [Guid.NewGuid()],
            "sponsor.demo",
            "Evidence telah diperbarui.",
            permit.Draft.ValidUntil,
            permit.Draft.ValidUntil.AddHours(4),
            Now.AddHours(3));
        Assert.Equal(PermitRenewalReviewStatus.Pending, permit.RenewalRequest?.Status);
        Assert.Equal(2, permit.RenewalRequest?.Revision);
    }

    [Fact]
    public void RejectedRenewalDoesNotCreateSuccessorPermit()
    {
        var permit = IssuedPermit();
        permit.RequestRenewal(
            Guid.NewGuid(),
            [Guid.NewGuid()],
            "sponsor.demo",
            "Pekerjaan perlu dilanjutkan.",
            permit.Draft.ValidUntil,
            permit.Draft.ValidUntil.AddHours(4),
            Now.AddHours(1));
        permit.RejectRenewal("area.manager", "Perpanjangan tidak dapat diberikan.", Now.AddHours(2));

        Assert.Equal(PermitRenewalReviewStatus.Rejected, permit.RenewalRequest?.Status);
        Assert.Null(permit.RenewalPermitId);
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
    public void DraftNormalizesV16PlanningFieldsWithoutAssigningHseSafetyEquipment()
    {
        var permit = Permit.CreateDraft(ValidDraft() with
        {
            SubmitterType = " contractor ",
            WorkTypeCode = null,
            WorkTypeCodes = [" hot_welding ", "HOT_GRINDING", "HOT_WELDING"],
            EquipmentTag = " P-101 ",
            EquipmentName = " Gas inlet separator ",
            WorkOrderNumber = " WO-2026-001 ",
            AdditionalHazardReference = " Potensi akses licin di sisi utara ",
            PlantArea = " Process Area ",
            SimopsDeclaration = " Tidak ada SIMOPS ",
            IsolationPrecautionCodes = ["LOTO"],
            JsaDocumentNumber = " JSA-001 ",
            JsaRevision = " 2 ",
            JsaDate = Now
        }, Now);

        Assert.Equal("CONTRACTOR", permit.Draft.SubmitterType);
        Assert.Equal("HOT_GRINDING", permit.Draft.WorkTypeCode);
        Assert.Equal(["HOT_GRINDING", "HOT_WELDING"], permit.Draft.WorkTypeCodes);
        Assert.Empty(permit.Draft.SafetyEquipmentCodes ?? []);
        Assert.Equal("Gas inlet separator", permit.Draft.EquipmentName);
        Assert.Equal("WO-2026-001", permit.Draft.WorkOrderNumber);
        Assert.Equal("Potensi akses licin di sisi utara", permit.Draft.AdditionalHazardReference);
        Assert.Equal("JSA-001", permit.Draft.JsaDocumentNumber);
    }

    [Theory]
    [InlineData("equipment", 101, "permit.equipment_name_too_long")]
    [InlineData("work-order", 61, "permit.work_order_number_too_long")]
    [InlineData("hazard-reference", 161, "permit.additional_hazard_reference_too_long")]
    public void DraftRejectsPlanningReferenceFieldsThatExceedThePrintedSpace(
        string field,
        int length,
        string expectedCode)
    {
        var value = new string('X', length);
        var draft = field switch
        {
            "equipment" => ValidDraft() with { EquipmentName = value },
            "work-order" => ValidDraft() with { WorkOrderNumber = value },
            _ => ValidDraft() with { AdditionalHazardReference = value }
        };

        var error = Assert.Throws<DomainRuleViolationException>(() => Permit.CreateDraft(draft, Now));

        Assert.Equal(expectedCode, error.Code);
    }

    [Fact]
    public void HotWorkHeaderClassificationAcceptsBothControlledCheckboxes()
    {
        var permit = Permit.CreateDraft(ValidDraft() with
        {
            HeaderClassificationCodes = [" Percikan Api ", "HOT_OPEN_FLAME"]
        }, Now);

        Assert.Equal(
            ["HOT_OPEN_FLAME", "HOT_SPARK"],
            permit.Draft.HeaderClassificationCodes);
    }

    [Fact]
    public void ColdWorkHeaderClassificationRequiresOneChoiceAndSynchronizesLegacyRisk()
    {
        var permit = Permit.CreateDraft(ValidDraft() with
        {
            PermitClass = PermitClass.ColdWork,
            WorkTypeCodes = ["COLD_MECHANICAL"],
            RiskLevel = RiskLevel.Extreme,
            HeaderClassificationCodes = ["COLD_LOW_RISK"]
        }, Now);

        Assert.Equal(RiskLevel.Low, permit.Draft.RiskLevel);
        var error = Assert.Throws<DomainRuleViolationException>(() => Permit.CreateDraft(
            ValidDraft() with
            {
                PermitClass = PermitClass.ColdWork,
                WorkTypeCodes = ["COLD_MECHANICAL"],
                HeaderClassificationCodes = ["COLD_LOW_RISK", "COLD_HIGH_RISK"]
            },
            Now));
        Assert.Equal("permit.header_classification_single_required", error.Code);
    }

    [Fact]
    public void LegacyDraftWithoutHeaderClassificationCanBeReadButCannotEnterWorkflow()
    {
        var permit = Permit.Rehydrate(
            Guid.NewGuid(),
            null,
            PermitStatus.Draft,
            1,
            ValidDraft() with { HeaderClassificationCodes = [] },
            Now,
            Now);

        var error = Assert.Throws<DomainRuleViolationException>(() =>
            permit.Submit("PTW-LEGACY-HEADER", ReadyToSubmit(), Now.AddMinutes(1)));

        Assert.Equal("permit.header_classification_required", error.Code);
    }

    [Fact]
    public void SponsorDraftCannotAssignHseOwnedSafetyEquipment()
    {
        var error = Assert.Throws<DomainRuleViolationException>(() =>
            Permit.CreateDraft(
                ValidDraft() with { SafetyEquipmentCodes = ["SAFETY_FIRE_EXTINGUISHER"] },
                Now));

        Assert.Equal("permit.safety_equipment.hse_owned", error.Code);
    }

    [Fact]
    public void DraftRejectsUnknownSubmitterType()
    {
        var error = Assert.Throws<DomainRuleViolationException>(() =>
            Permit.CreateDraft(ValidDraft() with { SubmitterType = "INTERNAL_VENDOR" }, Now));

        Assert.Equal("permit.submitter_type_invalid", error.Code);
    }

    [Fact]
    public void DraftRejectsWorkTypeFromAnotherPermitClass()
    {
        var error = Assert.Throws<DomainRuleViolationException>(() =>
            Permit.CreateDraft(ValidDraft() with { WorkTypeCodes = ["COLD_PAINTING"] }, Now));

        Assert.Equal("permit.work_type_invalid", error.Code);
    }

    [Fact]
    public void DraftRequiresAtLeastOneControlledWorkType()
    {
        var error = Assert.Throws<DomainRuleViolationException>(() =>
            Permit.CreateDraft(ValidDraft() with { WorkTypeCode = null, WorkTypeCodes = [] }, Now));

        Assert.Equal("permit.work_type_required", error.Code);
    }

    [Fact]
    public void DraftRequiresJsaAndNormalizesOptionalSupportingDocumentsInTemplateOrder()
    {
        var missingJsa = Assert.Throws<DomainRuleViolationException>(() =>
            Permit.CreateDraft(ValidDraft() with { RequiredDocumentCodes = ["MSDS"] }, Now));
        Assert.Equal("permit.supporting_document.jsa_required", missingJsa.Code);

        var permit = Permit.CreateDraft(ValidDraft() with
        {
            RequiredDocumentCodes = ["MSDS", "JSA", "Lifting Plan", "MSDS"]
        }, Now);

        Assert.Equal(["JSA", "LIFTING_PLAN", "MSDS"], permit.Draft.RequiredDocumentCodes);
    }

    [Fact]
    public void MandatoryUploadCatalogKeepsNonTemplateEvidenceSeparateFromBagian4()
    {
        Assert.Equal(
            ["JSA", "ID", "BPJS_TK", "FTW", "ESIMI"],
            PermitMandatoryDocumentCatalog.Resolve().Select(option => option.Code));
        Assert.DoesNotContain(
            PermitSupportingDocumentCatalog.Resolve(),
            option => option.Code == PermitMandatoryDocumentCatalog.IdentityCode);
    }

    [Fact]
    public void LegacyDraftWithoutJsaCanBeReadButCannotEnterWorkflow()
    {
        var permit = Permit.Rehydrate(
            Guid.NewGuid(),
            null,
            PermitStatus.Draft,
            1,
            ValidDraft() with { RequiredDocumentCodes = [] },
            Now,
            Now);

        var error = Assert.Throws<DomainRuleViolationException>(() =>
            permit.Submit("PTW-LEGACY-002", ReadyToSubmit(), Now.AddMinutes(1)));

        Assert.Equal("permit.supporting_document.jsa_required", error.Code);
    }

    [Fact]
    public void DraftNormalizesOtherWorkTypeDescriptionWhenOtherIsSelected()
    {
        var permit = Permit.CreateDraft(ValidDraft() with
        {
            WorkTypeCodes = ["HOT_OTHER", "HOT_WELDING"],
            OtherWorkTypeDescription = "  Pemanasan bearing dengan induction heater  "
        }, Now);

        Assert.Equal("Pemanasan bearing dengan induction heater", permit.Draft.OtherWorkTypeDescription);
        Assert.Contains("HOT_OTHER", permit.Draft.WorkTypeCodes ?? []);
    }

    [Fact]
    public void DraftRejectsOtherSelectionWithoutDescription()
    {
        var error = Assert.Throws<DomainRuleViolationException>(() =>
            Permit.CreateDraft(ValidDraft() with { WorkTypeCodes = ["HOT_OTHER"] }, Now));

        Assert.Equal("permit.work_type_other_detail_required", error.Code);
    }

    [Fact]
    public void DraftRejectsOtherDescriptionWithoutOtherSelection()
    {
        var error = Assert.Throws<DomainRuleViolationException>(() =>
            Permit.CreateDraft(ValidDraft() with
            {
                OtherWorkTypeDescription = "Pekerjaan tidak terpilih"
            }, Now));

        Assert.Equal("permit.work_type_other_detail_without_selection", error.Code);
    }

    [Fact]
    public void DraftRejectsOtherDescriptionLongerThanOfficialFormCapacity()
    {
        var error = Assert.Throws<DomainRuleViolationException>(() =>
            Permit.CreateDraft(ValidDraft() with
            {
                WorkTypeCodes = ["HOT_OTHER"],
                OtherWorkTypeDescription = new string('A', 81)
            }, Now));

        Assert.Equal("permit.work_type_other_detail_too_long", error.Code);
    }

    [Fact]
    public void LegacyOtherDraftCanBeReadButCannotEnterWorkflowWithoutDescription()
    {
        var permit = Permit.Rehydrate(
            Guid.NewGuid(),
            null,
            PermitStatus.Draft,
            1,
            ValidDraft() with { WorkTypeCodes = ["HOT_OTHER"] },
            Now,
            Now);

        Assert.Null(permit.Draft.OtherWorkTypeDescription);
        var error = Assert.Throws<DomainRuleViolationException>(() =>
            permit.Submit("PTW-LEGACY-001", ReadyToSubmit(), Now.AddMinutes(1)));
        Assert.Equal("permit.work_type_other_detail_required", error.Code);
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
        permit.ValidateSubmission(
            "hse.validator",
            "JSA dan requirement konsisten.",
            ["SAFETY_FIRE_EXTINGUISHER"],
            Now.AddMinutes(2));
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
        ["JSA"],
        WorkTypeCodes: ["HOT_WELDING"],
        HeaderClassificationCodes: ["HOT_OPEN_FLAME"]);
}
