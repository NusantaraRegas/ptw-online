using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ptw.Contracts;
using Ptw.Domain;
using Ptw.Infrastructure.Persistence;

namespace Ptw.Api.IntegrationTests;

[Collection(PtwApiTestGroup.Name)]
public sealed class PermitApiTests(PtwApiFactory factory)
{
    [Fact]
    public async Task HeaderClassificationReferenceDataMatchesControlledTemplate()
    {
        using var client = factory.CreateClient();

        var catalog = Required(await client.GetFromJsonAsync<PermitHeaderClassificationCatalogResponse[]>(
            "/api/v1/reference-data/header-classifications"));

        var hot = Assert.Single(catalog, item => item.PermitClass == "HotWork");
        Assert.Equal("MULTIPLE", hot.SelectionMode);
        Assert.Equal(["Api Terbuka", "Percikan Api"], hot.Options.Select(option => option.Label));
        var cold = Assert.Single(catalog, item => item.PermitClass == "ColdWork");
        Assert.Equal("SINGLE", cold.SelectionMode);
        Assert.Equal(["Low Risk", "High Risk"], cold.Options.Select(option => option.Label));
        var cse = Assert.Single(catalog, item => item.PermitClass == "ConfinedSpaceEntry");
        Assert.Equal("NONE", cse.SelectionMode);
        Assert.Empty(cse.Options);
    }

    [Fact]
    public async Task DraftNormalizesPlanningReferencesAndIgnoresUnsupportedSponsorFields()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        var request = Draft(sponsorId, "ORF") with
        {
            EquipmentName = " Gas inlet separator ",
            WorkOrderNumber = " WO-2026-001 ",
            AdditionalHazardReference = " Akses sisi utara licin saat hujan ",
            ClsrApplicable = true,
            IsolationPrecautionCodes = ["LOTO"]
        };

        using var response = await sponsor.PostAsJsonAsync("/api/v1/permits", request);

        response.EnsureSuccessStatusCode();
        var permit = Required(await response.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("Gas inlet separator", permit.Draft.EquipmentName);
        Assert.Equal("WO-2026-001", permit.Draft.WorkOrderNumber);
        Assert.Equal("Akses sisi utara licin saat hujan", permit.Draft.AdditionalHazardReference);
        Assert.False(permit.Draft.ClsrApplicable);
        Assert.Empty(permit.Draft.IsolationPrecautionCodes ?? []);
    }

    [Fact]
    public async Task PermitResponseUsesSponsorFullNameWithoutFallingBackToLoginIdentifier()
    {
        var sponsorId = Unique("sponsor");
        const string sponsorName = "Raka Pratama";
        using var admin = Client(Unique("admin"), "Administrator", "*");
        using var createSponsor = await admin.PostAsJsonAsync(
            "/api/v1/admin/users",
            new CreateUserRequest(
                sponsorId,
                sponsorId,
                sponsorName,
                "Sponsor Pekerjaan",
                "Operasi",
                "TestPassword123!"));
        createSponsor.EnsureSuccessStatusCode();

        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        var permit = await CreateAsync(sponsor, sponsorId, "ORF");

        Assert.Equal(sponsorName, permit.SponsorName);

        var sponsorWithoutProfileId = Unique("sponsor-without-profile");
        using var sponsorWithoutProfile = Client(sponsorWithoutProfileId, "Sponsor", "ORF");
        var permitWithoutProfile = await CreateAsync(
            sponsorWithoutProfile,
            sponsorWithoutProfileId,
            "ORF");

        Assert.Null(permitWithoutProfile.SponsorName);
    }

    [Fact]
    public async Task PermitListSearchesAcrossAllScopedPermits()
    {
        var sponsorId = Unique("sponsor");
        var marker = $"cari-{Guid.NewGuid():N}";
        using var sponsor = Client(sponsorId, "Sponsor", "*");
        using var createOrf = await sponsor.PostAsJsonAsync(
            "/api/v1/permits",
            Draft(sponsorId, "ORF") with { Title = $"Pengelasan {marker}" });
        createOrf.EnsureSuccessStatusCode();
        using var createWaterBased = await sponsor.PostAsJsonAsync(
            "/api/v1/permits",
            Draft(sponsorId, "WATER_BASED") with { Title = $"Inspeksi {marker}" });
        createWaterBased.EnsureSuccessStatusCode();

        using var areaOwner = Client(Unique("area-owner"), "AreaOwnerManager", "ORF");
        var result = Required(await areaOwner.GetFromJsonAsync<PagedResponse<PermitResponse>>(
            $"/api/v1/permits?search={Uri.EscapeDataString(marker)}"));

        var permit = Assert.Single(result.Items);
        Assert.Equal("ORF", permit.Draft.LocationId);
        Assert.Contains(marker, permit.Draft.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DraftSavesAndMandatoryUploadsKeepTheInitialBusinessVersion()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        var draft = await CreateAsync(sponsor, sponsorId, "ORF");

        using var updateResponse = await sponsor.SendAsync(Command(
            HttpMethod.Patch,
            $"/api/v1/permits/{draft.Id}/draft",
            draft.ETag,
            draft.Draft with { Title = "Draft akhir versi pertama" },
            idempotencyKey: null));
        updateResponse.EnsureSuccessStatusCode();
        draft = Required(await updateResponse.Content.ReadFromJsonAsync<PermitResponse>());
        draft = await UploadMandatoryDocumentsAsync(sponsor, draft);

        Assert.Equal(1, draft.Version);
        var versions = Required(await sponsor.GetFromJsonAsync<PagedResponse<PermitVersionResponse>>(
            $"/api/v1/permits/{draft.Id}/versions"));
        var version = Assert.Single(versions.Items);
        Assert.Equal(1, version.Version);
        Assert.Equal("Draft akhir versi pertama", version.Snapshot.Title);
    }

    [Fact]
    public async Task SupportingDocumentReferenceDataMatchesTemplateAndOnlyRequiresJsa()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/reference-data/supporting-documents");
        response.EnsureSuccessStatusCode();
        var options = Required(
            await response.Content.ReadFromJsonAsync<PermitSupportingDocumentOptionResponse[]>());

        Assert.Equal(15, options.Length);
        var required = Assert.Single(options, option => option.Required);
        Assert.Equal("JSA", required.Code);
        Assert.True(required.RequiresMetadata);
        Assert.Contains(options, option => option.Code == "MSDS" && !option.Required);
    }

    [Fact]
    public async Task MandatoryDocumentReferenceDataRequiresWorkProcedureAndBaseEvidence()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/reference-data/mandatory-documents");
        response.EnsureSuccessStatusCode();
        var options = Required(
            await response.Content.ReadFromJsonAsync<PermitMandatoryDocumentOptionResponse[]>());

        Assert.Equal(
            ["JSA", "WORK_PROCEDURE", "ID", "BPJS_TK", "FTW", "ESIMI"],
            options.Select(x => x.Code));
        Assert.Equal("JSA", options[0].UploadCategory);
        Assert.All(options.Skip(1), option => Assert.Equal("SUPPORTING", option.UploadCategory));
    }

    [Fact]
    public async Task OperationalConditionReferenceDataMatchesBagian7Hierarchy()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/reference-data/operational-conditions");
        response.EnsureSuccessStatusCode();
        var options = Required(
            await response.Content.ReadFromJsonAsync<PermitOperationalConditionOptionResponse[]>());

        Assert.Equal(11, options.Length);
        Assert.Contains(options, option => option.Code == PermitOperationalConditionCatalog.Isolation);
        Assert.Contains(options, option =>
            option.Code == PermitOperationalConditionCatalog.IsolationBlind
            && option.ParentCode == PermitOperationalConditionCatalog.Isolation);
        Assert.True(Assert.Single(options, option => option.Code == PermitOperationalConditionCatalog.Other).RequiresDetail);
    }

    [Fact]
    public async Task SponsorCannotAssignHseOwnedSafetyEquipmentInDraft()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        var request = Draft(sponsorId, "ORF") with
        {
            SafetyEquipmentCodes = ["SAFETY_FIRE_EXTINGUISHER"]
        };

        using var response = await sponsor.PostAsJsonAsync("/api/v1/permits", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("permit.safety_equipment.hse_owned", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task SubmitRejectsDraftWithoutControlledHeaderClassification()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        using var createResponse = await sponsor.PostAsJsonAsync(
            "/api/v1/permits",
            Draft(sponsorId, "ORF") with { HeaderClassificationCodes = [] });
        createResponse.EnsureSuccessStatusCode();
        var draft = Required(await createResponse.Content.ReadFromJsonAsync<PermitResponse>());
        draft = await UploadMandatoryDocumentsAsync(sponsor, draft);

        using var response = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/permits/{draft.Id}/submit",
            draft.ETag,
            ReadyToSubmit()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("permit.header_classification_required", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task WorkTypeReferenceDataMatchesControlledTemplateAndKeepsDuplicateRowsDistinct()
    {
        using var client = Client(Unique("sponsor"), "Sponsor", "ORF");

        var catalog = Required(await client.GetFromJsonAsync<PermitWorkTypeCatalogResponse[]>(
            "/api/v1/reference-data/work-types"));

        var hotWork = Assert.Single(catalog, item => item.PermitClass == "HotWork");
        Assert.Contains(hotWork.Options, option => option.Code == "HOT_WELDING" && option.Label == "Mengelas");
        var sandBlasting = hotWork.Options.Where(option => option.Label == "Sand Blasting").ToArray();
        Assert.Equal(2, sandBlasting.Length);
        Assert.Equal(2, sandBlasting.Select(option => option.Code).Distinct().Count());
        Assert.True(Assert.Single(hotWork.Options, option => option.Code == "HOT_OTHER").RequiresDetail);
        Assert.False(Assert.Single(hotWork.Options, option => option.Code == "HOT_WELDING").RequiresDetail);
    }

    [Fact]
    public async Task DraftRejectsOtherWorkTypeWithoutItsRequiredDescription()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        var request = Draft(sponsorId, "ORF") with { WorkTypeCodes = ["HOT_OTHER"] };

        using var response = await sponsor.PostAsJsonAsync("/api/v1/permits", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("permit.work_type_other_detail_required", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task SubmitRejectsJsaSelectionWithoutMatchingUploadedEvidence()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        var draft = await CreateAsync(sponsor, sponsorId, "ORF");

        using var response = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/permits/{draft.Id}/submit",
            draft.ETag,
            ReadyToSubmit()));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("permit.mandatory_document.evidence_required", await ProblemCodeAsync(response));
    }

    [Theory]
    [InlineData("JSA", "Job Safety Analisis")]
    [InlineData("WORK_PROCEDURE", "Prosedur Pekerjaan")]
    [InlineData("ID", "ID")]
    [InlineData("BPJS_TK", "BPJS TK")]
    [InlineData("FTW", "FTW")]
    [InlineData("ESIMI", "E-SIMI")]
    public async Task SubmitRejectsEachMissingMandatoryDocument(string omittedCode, string expectedLabel)
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        var draft = await CreateAsync(sponsor, sponsorId, "ORF");
        draft = await UploadMandatoryDocumentsAsync(sponsor, draft, omittedCode);

        using var response = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/permits/{draft.Id}/submit",
            draft.ETag,
            ReadyToSubmit()));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("permit.mandatory_document.evidence_required", await ProblemCodeAsync(response));
        Assert.Contains(expectedLabel, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SubmitAlsoRequiresEvidenceForEachOptionalBagian4Selection()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        using var createResponse = await sponsor.PostAsJsonAsync(
            "/api/v1/permits",
            Draft(sponsorId, "ORF") with { RequiredDocumentCodes = ["JSA", "MSDS"] });
        createResponse.EnsureSuccessStatusCode();
        var draft = Required(await createResponse.Content.ReadFromJsonAsync<PermitResponse>());
        draft = await UploadMandatoryDocumentsAsync(sponsor, draft);

        using var response = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/permits/{draft.Id}/submit",
            draft.ETag,
            ReadyToSubmit()));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("permit.supporting_document.evidence_required", await ProblemCodeAsync(response));
        Assert.Contains("MSDS", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SubmissionRejectsLocationOutsideTheReleasedRoutes()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "*");
        var draft = await CreateAsync(sponsor, sponsorId, "FSRU");

        using var response = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/permits/{draft.Id}/submit",
            draft.ETag,
            new SubmitPermitRequest(true, true, true, [])));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("permit.location.not_released", await ProblemCodeAsync(response));
    }

    [Theory]
    [InlineData("ORF", "SITE_OFFICE")]
    [InlineData("SITE_OFFICE", "ORF")]
    [InlineData("WATER_BASED", "ORF")]
    public async Task ReleasedLocationRoutesApprovalToManagerWithMatchingScope(
        string location,
        string wrongManagerScope)
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", location);
        using var validator = Client(Unique("hse"), "HSEValidator", "*");
        using var wrongManager = Client(Unique("wrong-manager"), "AreaOwnerManager", wrongManagerScope);
        using var correctManager = Client(Unique("correct-manager"), "AreaOwnerManager", location);
        var validated = await CreateAndValidateAsync(sponsor, validator, sponsorId, location);
        var approvalTask = await PendingTaskAsync(validated.Id, "AREA_APPROVE_AND_ISSUE");
        var request = new ApproveAndIssuePermitRequest("Disetujui oleh pemilik wilayah.", null);

        using var wrongResponse = await wrongManager.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{approvalTask.Id}/approve-and-issue",
            validated.ETag,
            request));
        Assert.Equal(HttpStatusCode.Forbidden, wrongResponse.StatusCode);

        using var correctResponse = await correctManager.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{approvalTask.Id}/approve-and-issue",
            validated.ETag,
            request));
        correctResponse.EnsureSuccessStatusCode();
        var issued = Required(await correctResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("ISSUED", issued.Status);
    }

    [Fact]
    public async Task SubmitCreatesExactlyOneHseTaskAndNoGasValidatorTask()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        var submitted = await CreateAndSubmitAsync(sponsor, sponsorId);

        Assert.Equal("UNDER_VALIDATION", submitted.Status);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
        var tasks = await db.PermitTasks.AsNoTracking()
            .Where(x => x.PermitId == submitted.Id && x.Status == "PENDING")
            .ToListAsync();
        var task = Assert.Single(tasks);
        Assert.Equal("HSE_VALIDATION", task.Type);
        Assert.Equal("HSEValidator", task.RequiredRole);
        Assert.DoesNotContain(tasks, x => x.Type.Contains("GAS", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SponsorCannotSelfValidateButAnotherHseValidatorCan()
    {
        var sponsorId = Unique("hse-sponsor");
        using var sponsor = Client(sponsorId, "Sponsor,HSEValidator", "ORF");
        var submitted = await CreateAndSubmitAsync(sponsor, sponsorId);
        var validationTask = await PendingTaskAsync(submitted.Id, "HSE_VALIDATION");

        using var selfResponse = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{validationTask.Id}/validate",
            submitted.ETag,
            new ValidateSubmissionRequest(
                "Saya memvalidasi sendiri.",
                ["SAFETY_FIRE_EXTINGUISHER"])));
        Assert.Equal(HttpStatusCode.Conflict, selfResponse.StatusCode);
        Assert.Equal("permit.validation.self_validation_forbidden", await ProblemCodeAsync(selfResponse));

        var validatorId = Unique("hse");
        const string validatorName = "Darsono";
        using var validator = Client(validatorId, "HSEValidator", "ORF", validatorName);
        using var validateResponse = await validator.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{validationTask.Id}/validate",
            submitted.ETag,
            new ValidateSubmissionRequest(
                "JSA dan requirement telah diverifikasi.",
                ["SAFETY_FIRE_EXTINGUISHER", "SAFETY_LOTO"])));
        validateResponse.EnsureSuccessStatusCode();
        var validated = Required(await validateResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("AWAITING_AREA_APPROVAL", validated.Status);
        Assert.Equal("HSE", validated.Workflow.Hse.Code);
        Assert.True(validated.Workflow.Hse.Completed);
        Assert.Equal(validatorId, validated.Workflow.Hse.ActorId);
        Assert.Equal(validatorName, validated.Workflow.Hse.ActorName);
        Assert.Equal(
            ["SAFETY_FIRE_EXTINGUISHER", "SAFETY_LOTO"],
            validated.Workflow.Hse.SafetyEquipmentCodes);
        Assert.Empty(validated.Draft.SafetyEquipmentCodes ?? []);

        var areaReviewTask = await PendingTaskAsync(submitted.Id, "AREA_OPERATION_REVIEW");
        Assert.Equal(submitted.Version, areaReviewTask.BusinessPermitVersion);
        Assert.Equal("AreaOwnerSeniorOfficer", areaReviewTask.RequiredRole);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
            var now = DateTimeOffset.UtcNow;
            db.UserAccounts.Add(new UserAccountRecord
            {
                SubjectId = validatorId,
                UserName = validatorId,
                NormalizedUserName = validatorId.ToUpperInvariant(),
                DisplayName = validatorName,
                Position = "Sr. Officer II Health & Safety",
                Department = "HSSE",
                IsActive = true,
                Version = 1,
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [ptw].[Permit]
                SET [WorkflowEvidenceJson] = JSON_MODIFY(
                    [WorkflowEvidenceJson], '$.hseValidation.actorName', NULL)
                WHERE [Id] = {submitted.Id}
                """);
        }

        var legacyResponse = Required(
            await sponsor.GetFromJsonAsync<PermitResponse>($"/api/v1/permits/{submitted.Id}"));
        Assert.Equal(validatorName, legacyResponse.Workflow.Hse.ActorName);
    }

    [Fact]
    public async Task SoOrOfficerReviewIsRequiredBeforeManagerTaskAndUsesUserProfileIdentity()
    {
        var sponsorId = Unique("sponsor");
        var reviewerId = Unique("area-officer");
        const string reviewerName = "Benny Sulistio";
        const string reviewerPosition = "Officer II Gas Delivery Operation";
        using var admin = Client(Unique("admin"), "Administrator", "*");
        using var createReviewer = await admin.PostAsJsonAsync(
            "/api/v1/admin/users",
            new CreateUserRequest(
                reviewerId,
                reviewerId,
                reviewerName,
                reviewerPosition,
                "Gas Distribution&ORF Management",
                "TestPassword123!"));
        createReviewer.EnsureSuccessStatusCode();
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        using var validator = Client(Unique("hse"), "HSEValidator", "ORF");
        using var reviewer = Client(reviewerId, "AreaOwnerSeniorOfficer", "ORF");
        using var manager = Client(Unique("manager"), "AreaOwnerManager", "ORF");
        var submitted = await CreateAndSubmitAsync(sponsor, sponsorId);
        var validationTask = await PendingTaskAsync(submitted.Id, "HSE_VALIDATION");
        using var validationResponse = await validator.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{validationTask.Id}/validate",
            submitted.ETag,
            new ValidateSubmissionRequest("Valid.", ["SAFETY_FIRE_EXTINGUISHER"])));
        validationResponse.EnsureSuccessStatusCode();
        var validated = Required(await validationResponse.Content.ReadFromJsonAsync<PermitResponse>());
        var areaTask = await PendingTaskAsync(validated.Id, "AREA_OPERATION_REVIEW");

        using var prematureIssue = await manager.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{areaTask.Id}/approve-and-issue",
            validated.ETag,
            new ApproveAndIssuePermitRequest("Belum ada review SO/Officer.", null)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, prematureIssue.StatusCode);
        Assert.Equal("task.type.invalid", await ProblemCodeAsync(prematureIssue));

        using var invalidReview = await reviewer.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{areaTask.Id}/review-area-operations",
            validated.ETag,
            new ReviewAreaOperationsRequest(
                "Isolasi diperiksa.",
                [PermitOperationalConditionCatalog.Isolation],
                null,
                true)));
        Assert.Equal(HttpStatusCode.Conflict, invalidReview.StatusCode);
        Assert.Equal("permit.area_operations.subcondition_required", await ProblemCodeAsync(invalidReview));

        using var reviewResponse = await reviewer.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{areaTask.Id}/review-area-operations",
            validated.ETag,
            AreaOperationsReview()));
        reviewResponse.EnsureSuccessStatusCode();
        var reviewed = Required(await reviewResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.True(reviewed.Workflow.AreaOperations.Completed);
        Assert.Equal("AWAITING_AREA_APPROVAL", reviewed.Status);
        Assert.Equal(reviewerId, reviewed.Workflow.AreaOperations.ActorId);
        Assert.Equal(reviewerName, reviewed.Workflow.AreaOperations.ActorName);
        Assert.Equal(reviewerPosition, reviewed.Workflow.AreaOperations.ActorPosition);

        var approvalTask = await PendingTaskAsync(reviewed.Id, "AREA_APPROVE_AND_ISSUE");
        Assert.Equal("AreaOwnerManager", approvalTask.RequiredRole);
    }

    [Fact]
    public async Task FirstSoOrOfficerToReviewWinsTheSingleAreaTask()
    {
        var sponsorId = Unique("sponsor");
        var firstReviewerId = Unique("area-reviewer-first");
        var secondReviewerId = Unique("area-reviewer-second");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        using var validator = Client(Unique("hse"), "HSEValidator", "ORF");
        using var firstReviewer = Client(firstReviewerId, "AreaOwnerSeniorOfficer", "ORF");
        using var secondReviewer = Client(secondReviewerId, "AreaOwnerSeniorOfficer", "ORF");
        var submitted = await CreateAndSubmitAsync(sponsor, sponsorId);
        var validationTask = await PendingTaskAsync(submitted.Id, "HSE_VALIDATION");
        using var validationResponse = await validator.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{validationTask.Id}/validate",
            submitted.ETag,
            new ValidateSubmissionRequest("Valid.", ["SAFETY_FIRE_EXTINGUISHER"])));
        validationResponse.EnsureSuccessStatusCode();
        var validated = Required(await validationResponse.Content.ReadFromJsonAsync<PermitResponse>());
        var areaTask = await PendingTaskAsync(validated.Id, "AREA_OPERATION_REVIEW");

        using var firstResponse = await firstReviewer.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{areaTask.Id}/review-area-operations",
            validated.ETag,
            AreaOperationsReview()));
        firstResponse.EnsureSuccessStatusCode();
        var reviewed = Required(await firstResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal(firstReviewerId, reviewed.Workflow.AreaOperations.ActorId);

        using var secondResponse = await secondReviewer.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{areaTask.Id}/review-area-operations",
            validated.ETag,
            AreaOperationsReview()));
        Assert.Equal(HttpStatusCode.NotFound, secondResponse.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
        Assert.Single(await db.PermitTasks.AsNoTracking().Where(
            x => x.PermitId == reviewed.Id
                && x.Type == "AREA_APPROVE_AND_ISSUE"
                && x.Status == "PENDING").ToListAsync());
    }

    [Fact]
    public async Task ApproveAndIssueCommitsDecisionStateAuditOutboxAndPrintSnapshotAtomically()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        using var validator = Client(Unique("hse"), "HSEValidator", "ORF");
        var managerId = Unique("manager");
        const string managerName = "Yosep Ismail Zulkarnain";
        const string managerPosition = "Manager Gas Distribution&ORF Management";
        using var admin = Client(Unique("admin"), "Administrator", "*");
        using var createManager = await admin.PostAsJsonAsync(
            "/api/v1/admin/users",
            new CreateUserRequest(
                managerId,
                managerId,
                managerName,
                managerPosition,
                "Gas Distribution&ORF Management",
                "TestPassword123!"));
        createManager.EnsureSuccessStatusCode();
        using var manager = Client(managerId, "AreaOwnerManager", "ORF");
        var validated = await CreateAndValidateAsync(sponsor, validator, sponsorId);
        var approvalTask = await PendingTaskAsync(validated.Id, "AREA_APPROVE_AND_ISSUE");
        var request = new ApproveAndIssuePermitRequest(
            "Saya menyetujui dan menerbitkan PTW ini.",
            null);
        var key = Guid.NewGuid().ToString("N");

        using var issueResponse = await manager.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{approvalTask.Id}/approve-and-issue",
            validated.ETag,
            request,
            key));
        Assert.True(issueResponse.IsSuccessStatusCode, await issueResponse.Content.ReadAsStringAsync());
        var issued = Required(await issueResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("ISSUED", issued.Status);
        Assert.Equal("MANAGER", issued.Workflow.Approval.Capacity);
        Assert.NotEqual(Guid.Empty, issued.Workflow.Approval.AuthorizationId);
        Assert.Equal(managerName, issued.Workflow.Approval.ActorName);
        Assert.Equal(managerPosition, issued.Workflow.Approval.ActorPosition);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
        var decision = await db.PermitDecisions.AsNoTracking().SingleAsync(
            x => x.PermitId == issued.Id && x.Decision == "APPROVE_AND_ISSUE");
        var operationsDecision = await db.PermitDecisions.AsNoTracking().SingleAsync(
            x => x.PermitId == issued.Id && x.Decision == "AREA_OPERATION_REVIEW");
        Assert.Equal("AREA_OPERATIONS_REVIEWER", operationsDecision.ApprovalCapacity);
        var snapshot = await db.PrintPackageSnapshots.AsNoTracking().SingleAsync(x => x.PermitId == issued.Id);
        Assert.Equal(issued.Version, decision.BusinessPermitVersion);
        Assert.Equal(issued.Version, operationsDecision.BusinessPermitVersion);
        Assert.Equal(issued.Version, snapshot.BusinessPermitVersion);
        Assert.True(snapshot.PermitVersion > snapshot.BusinessPermitVersion);
        var document = await db.GeneratedDocuments.AsNoTracking()
            .SingleAsync(x => x.PrintPackageSnapshotId == snapshot.Id);
        Assert.Equal(decision.Id, snapshot.DecisionId);
        Assert.Equal("PENDING", snapshot.RenderStatus);
        Assert.Equal("PENDING", document.RenderStatus);
        Assert.True(await db.AuditEvents.AsNoTracking().AnyAsync(
            x => x.PermitId == issued.Id && x.EventType == "permit_issued"));
        Assert.True(await db.OutboxMessages.AsNoTracking().AnyAsync(
            x => x.AggregateId == issued.Id && x.EventType == "permit_issued"));

        using var replay = await manager.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{approvalTask.Id}/approve-and-issue",
            validated.ETag,
            request,
            key));
        replay.EnsureSuccessStatusCode();
        Assert.Equal(2, await db.PermitDecisions.AsNoTracking().CountAsync(x => x.PermitId == issued.Id));

        using var mismatch = await manager.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{approvalTask.Id}/approve-and-issue",
            validated.ETag,
            request with { Statement = "Payload berbeda." },
            key));
        Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
        Assert.Equal("idempotency.payload_mismatch", await ProblemCodeAsync(mismatch));
    }

    [Fact]
    public async Task RevisionCancelsAreaTaskAndResubmitCreatesFreshHseTaskForNewVersion()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        using var validator = Client(Unique("hse"), "HSEValidator", "ORF");
        using var manager = Client(Unique("manager"), "AreaOwnerManager", "ORF");
        var validated = await CreateAndValidateAsync(sponsor, validator, sponsorId);
        var areaTask = await PendingTaskAsync(validated.Id, "AREA_APPROVE_AND_ISSUE");

        using var revisionResponse = await manager.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{areaTask.Id}/revision",
            validated.ETag,
            new PermitReasonRequest("JSA harus diperbarui.")));
        revisionResponse.EnsureSuccessStatusCode();
        var revision = Required(await revisionResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("REVISION_REQUIRED", revision.Status);
        Assert.False(revision.Workflow.Hse.Completed);

        var sponsorNotifications = Required(
            await sponsor.GetFromJsonAsync<PagedResponse<PermitTaskResponse>>("/api/v1/tasks"));
        var revisionNotification = Assert.Single(
            sponsorNotifications.Items,
            task => task.PermitId == revision.Id && task.Type == "SPONSOR_REVISION");
        Assert.Equal("Perbaiki dan ajukan ulang PTW", revisionNotification.Label);
        Assert.Equal("Sponsor", revisionNotification.RequiredRole);
        using var sponsorWithChangedRole = Client(sponsorId, "Administrator", "ORF");
        var directlyAssignedNotifications = Required(
            await sponsorWithChangedRole.GetFromJsonAsync<PagedResponse<PermitTaskResponse>>("/api/v1/tasks"));
        Assert.Contains(
            directlyAssignedNotifications.Items,
            task => task.Id == revisionNotification.Id);
        var managerNotifications = Required(
            await manager.GetFromJsonAsync<PagedResponse<PermitTaskResponse>>("/api/v1/tasks"));
        Assert.DoesNotContain(
            managerNotifications.Items,
            task => task.PermitId == revision.Id && task.Type == "SPONSOR_REVISION");

        using var patchResponse = await sponsor.SendAsync(Command(
            HttpMethod.Patch,
            $"/api/v1/permits/{revision.Id}/draft",
            revision.ETag,
            revision.Draft with { Title = "Versi material baru" },
            idempotencyKey: null));
        patchResponse.EnsureSuccessStatusCode();
        var updated = Required(await patchResponse.Content.ReadFromJsonAsync<PermitResponse>());
        using var submitResponse = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/permits/{updated.Id}/submit",
            updated.ETag,
            ReadyToSubmit()));
        submitResponse.EnsureSuccessStatusCode();
        var resubmitted = Required(await submitResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal(updated.Version + 1, resubmitted.Version);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
        Assert.Equal("CANCELLED", (await db.PermitTasks.SingleAsync(x => x.Id == areaTask.Id)).Status);
        var completedRevisionNotification = await db.PermitTasks.SingleAsync(
            x => x.Id == revisionNotification.Id);
        Assert.Equal("COMPLETED", completedRevisionNotification.Status);
        Assert.Equal(sponsorId, completedRevisionNotification.CompletedBy);
        Assert.Equal(updated.Version, completedRevisionNotification.BusinessPermitVersion);
        Assert.Single(await db.PermitTasks.Where(x => x.PermitId == updated.Id
            && x.BusinessPermitVersion == resubmitted.Version
            && x.Type == "HSE_VALIDATION"
            && x.Status == "PENDING").ToListAsync());
    }

    [Fact]
    public async Task RevisionResubmitWithoutDraftMutationCreatesFreshHseTaskForNewVersion()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        using var validator = Client(Unique("hse"), "HSEValidator", "ORF");
        var submitted = await CreateAndSubmitAsync(sponsor, sponsorId);
        var originalTask = await PendingTaskAsync(submitted.Id, "HSE_VALIDATION");

        using var revisionResponse = await validator.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{originalTask.Id}/revision",
            submitted.ETag,
            new PermitReasonRequest("Konfirmasi ulang dokumen pendukung.")));
        revisionResponse.EnsureSuccessStatusCode();
        var revision = Required(await revisionResponse.Content.ReadFromJsonAsync<PermitResponse>());

        using var submitResponse = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/permits/{revision.Id}/submit",
            revision.ETag,
            ReadyToSubmit()));
        submitResponse.EnsureSuccessStatusCode();
        var resubmitted = Required(await submitResponse.Content.ReadFromJsonAsync<PermitResponse>());

        Assert.Equal(revision.Version + 1, resubmitted.Version);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
        Assert.Equal("CANCELLED", (await db.PermitTasks.SingleAsync(x => x.Id == originalTask.Id)).Status);
        Assert.Single(await db.PermitTasks.Where(x => x.PermitId == resubmitted.Id
            && x.BusinessPermitVersion == resubmitted.Version
            && x.Type == "HSE_VALIDATION"
            && x.Status == "PENDING").ToListAsync());
    }

    [Fact]
    public async Task StaleIfMatchReturnsConflictAndIdempotencyPayloadMismatchIsRejected()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        var draft = await CreateAsync(sponsor, sponsorId, "ORF");
        using var updateResponse = await sponsor.SendAsync(Command(
            HttpMethod.Patch,
            $"/api/v1/permits/{draft.Id}/draft",
            draft.ETag,
            draft.Draft with { Title = "Versi kedua" },
            null));
        updateResponse.EnsureSuccessStatusCode();

        using var stale = await sponsor.SendAsync(Command(
            HttpMethod.Patch,
            $"/api/v1/permits/{draft.Id}/draft",
            draft.ETag,
            draft.Draft with { Title = "Versi stale" },
            null));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("concurrency.conflict", await ProblemCodeAsync(stale));
    }

    [Fact]
    public async Task DevelopmentAttachmentUploadAcceptsVerifiedPdfJpegAndPngSignaturesAsCleanEvidence()
    {
        var sponsorId = Unique("sponsor");
        const string sponsorDisplayName = "Siti Sponsor Lampiran";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
            var now = DateTimeOffset.UtcNow;
            db.UserAccounts.Add(new UserAccountRecord
            {
                SubjectId = sponsorId,
                UserName = sponsorId,
                NormalizedUserName = sponsorId.ToUpperInvariant(),
                DisplayName = sponsorDisplayName,
                IsActive = true,
                Version = 1,
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync();
        }

        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        var permit = await CreateAsync(sponsor, sponsorId, "ORF");
        var files = new[]
        {
            ("jsa.pdf", "application/pdf", Encoding.ASCII.GetBytes("%PDF-1.7\n")),
            ("photo.jpg", "image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }),
            ("scan.png", "image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })
        };
        PermitAttachmentResponse? firstAttachment = null;

        foreach (var (fileName, mediaType, bytes) in files)
        {
            using var content = new MultipartFormDataContent();
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
            content.Add(file, "file", fileName);
            content.Add(new StringContent("SUPPORTING"), "category");
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/v1/permits/{permit.Id}/attachments")
            {
                Content = content
            };
            request.Headers.TryAddWithoutValidation("If-Match", permit.ETag);
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            using var response = await sponsor.SendAsync(request);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var mutation = Required(await response.Content.ReadFromJsonAsync<PermitAttachmentMutationResponse>());
            Assert.Equal("CLEAN", mutation.Attachment.ScanStatus);
            Assert.StartsWith(
                "development-trusted-upload:",
                Assert.IsType<string>(mutation.Attachment.ScanEvidenceReference));
            Assert.NotNull(mutation.Attachment.ScannedAt);
            Assert.Equal("SUPPORTING", mutation.Attachment.Category);
            Assert.Equal(mediaType, mutation.Attachment.MediaType);
            Assert.Equal(64, mutation.Attachment.Sha256.Length);
            Assert.Equal(sponsorId, mutation.Attachment.UploadedBy);
            Assert.Equal(sponsorDisplayName, mutation.Attachment.UploadedByName);
            firstAttachment ??= mutation.Attachment;
            permit = permit with { ETag = mutation.ETag, Version = mutation.PermitVersion };
        }

        using var listResponse = await sponsor.GetAsync($"/api/v1/permits/{permit.Id}/attachments");
        listResponse.EnsureSuccessStatusCode();
        var listedAttachments = Required(
            await listResponse.Content.ReadFromJsonAsync<List<PermitAttachmentResponse>>());
        Assert.All(listedAttachments, attachment =>
        {
            Assert.Equal(sponsorId, attachment.UploadedBy);
            Assert.Equal(sponsorDisplayName, attachment.UploadedByName);
        });

        using var cleanDownload = await sponsor.GetAsync(
            $"/api/v1/permits/{permit.Id}/attachments/{Required(firstAttachment).Id}/content");
        Assert.Equal(HttpStatusCode.OK, cleanDownload.StatusCode);

        using var otherSponsor = Client(Unique("other-sponsor"), "Sponsor", "ORF");
        using var crossSponsorList = await otherSponsor.GetAsync($"/api/v1/permits/{permit.Id}/attachments");
        Assert.Equal(HttpStatusCode.Forbidden, crossSponsorList.StatusCode);
        Assert.Equal("authorization.denied", await ProblemCodeAsync(crossSponsorList));
    }

    [Fact]
    public async Task AttachmentUploadRejectsExtensionAndSignatureMismatch()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        var permit = await CreateAsync(sponsor, sponsorId, "ORF");
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "file", "forged.pdf");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/permits/{permit.Id}/attachments")
        {
            Content = content
        };
        request.Headers.TryAddWithoutValidation("If-Match", permit.ETag);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using var response = await sponsor.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("attachment.media_type_invalid", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task ClosureRequiresCleanEvidenceAndCompleteAreaOwnerSection10Verification()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        using var validator = Client(Unique("hse"), "HSEValidator", "ORF");
        using var seniorOfficer = Client(Unique("senior-officer"), "AreaOwnerSeniorOfficer", "ORF");
        using var wrongScopeOfficer = Client(
            Unique("wrong-scope-officer"),
            "AreaOwnerSeniorOfficer",
            "SITE_OFFICE");
        using var manager = Client(Unique("manager"), "AreaOwnerManager", "ORF");
        var validated = await CreateAndValidateAsync(sponsor, validator, sponsorId);
        var approvalTask = await PendingTaskAsync(validated.Id, "AREA_APPROVE_AND_ISSUE");
        using var issueResponse = await manager.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{approvalTask.Id}/approve-and-issue",
            validated.ETag,
            new ApproveAndIssuePermitRequest("Disetujui dan diterbitkan.", null)));
        issueResponse.EnsureSuccessStatusCode();
        var issued = Required(await issueResponse.Content.ReadFromJsonAsync<PermitResponse>());

        Guid printPackageId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
            var package = await db.PrintPackageSnapshots.SingleAsync(x => x.PermitId == issued.Id);
            package.RenderStatus = "READY";
            printPackageId = package.Id;
            await db.SaveChangesAsync();
        }

        using var uploadContent = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7\n"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        uploadContent.Add(file, "file", "signed-field-copy.pdf");
        uploadContent.Add(new StringContent("SIGNED_FIELD_COPY"), "category");
        uploadContent.Add(new StringContent("SFC-001"), "documentNumber");
        uploadContent.Add(new StringContent("1"), "documentRevision");
        uploadContent.Add(new StringContent(DateTimeOffset.UtcNow.ToString("O")), "documentDate");
        uploadContent.Add(new StringContent(printPackageId.ToString()), "printPackageId");
        using var uploadRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/permits/{issued.Id}/attachments")
        {
            Content = uploadContent
        };
        uploadRequest.Headers.TryAddWithoutValidation("If-Match", issued.ETag);
        uploadRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using var uploadResponse = await sponsor.SendAsync(uploadRequest);
        Assert.True(uploadResponse.IsSuccessStatusCode, await uploadResponse.Content.ReadAsStringAsync());
        var upload = Required(await uploadResponse.Content.ReadFromJsonAsync<PermitAttachmentMutationResponse>());
        Assert.Equal("CLEAN", upload.Attachment.ScanStatus);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
            var attachment = await db.PermitAttachments.SingleAsync(x => x.Id == upload.Attachment.Id);
            attachment.ScanStatus = "PENDING";
            attachment.ScanEvidenceReference = null;
            attachment.ScannedAt = null;
            await db.SaveChangesAsync();
        }

        using var closureResponse = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/permits/{issued.Id}/closure-requests",
            upload.ETag,
            new RequestClosureRequest(
                printPackageId,
                [upload.Attachment.Id],
                "Pekerjaan dan handback pada hardcopy telah selesai.",
                true,
                true)));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, closureResponse.StatusCode);
        Assert.Equal("permit.closure.evidence_invalid", await ProblemCodeAsync(closureResponse));
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<PtwDbContext>();
        Assert.Empty(await verificationDb.PermitTasks.Where(x =>
            x.PermitId == issued.Id && x.Type == "AREA_CLOSE_VERIFICATION").ToListAsync());

        var restoredAttachment = await verificationDb.PermitAttachments.SingleAsync(
            x => x.Id == upload.Attachment.Id);
        restoredAttachment.ScanStatus = "CLEAN";
        restoredAttachment.ScanEvidenceReference = "integration-test-clean-evidence";
        restoredAttachment.ScannedAt = DateTimeOffset.UtcNow;
        await verificationDb.SaveChangesAsync();

        using var validClosureResponse = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/permits/{issued.Id}/closure-requests",
            upload.ETag,
            new RequestClosureRequest(
                printPackageId,
                [upload.Attachment.Id],
                "Pekerjaan dan handback pada hardcopy telah selesai.",
                true,
                true)));
        validClosureResponse.EnsureSuccessStatusCode();
        var closureRequested = Required(await validClosureResponse.Content.ReadFromJsonAsync<PermitResponse>());
        var closureTask = await PendingTaskAsync(issued.Id, "AREA_CLOSE_VERIFICATION");
        var seniorOfficerTasks = Required(
            await seniorOfficer.GetFromJsonAsync<PagedResponse<PermitTaskResponse>>("/api/v1/tasks"));
        var managerTasks = Required(
            await manager.GetFromJsonAsync<PagedResponse<PermitTaskResponse>>("/api/v1/tasks"));
        var wrongScopeOfficerTasks = Required(
            await wrongScopeOfficer.GetFromJsonAsync<PagedResponse<PermitTaskResponse>>("/api/v1/tasks"));
        Assert.Contains(seniorOfficerTasks.Items, x => x.Id == closureTask.Id);
        Assert.Contains(managerTasks.Items, x => x.Id == closureTask.Id);
        Assert.DoesNotContain(wrongScopeOfficerTasks.Items, x => x.Id == closureTask.Id);

        var completeSection10 = new ClosePermitRequest(
            "Bagian 10 dan hardcopy telah diverifikasi.",
            "Officer Operasi",
            true,
            true,
            true,
            true,
            true,
            true);
        using var sponsorClose = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/closure-tasks/{closureTask.Id}/close",
            closureRequested.ETag,
            completeSection10));
        Assert.Equal(HttpStatusCode.Forbidden, sponsorClose.StatusCode);

        using var wrongScopeClose = await wrongScopeOfficer.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/closure-tasks/{closureTask.Id}/close",
            closureRequested.ETag,
            completeSection10));
        Assert.Equal(HttpStatusCode.Forbidden, wrongScopeClose.StatusCode);

        using var incompleteClose = await manager.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/closure-tasks/{closureTask.Id}/close",
            closureRequested.ETag,
            completeSection10 with { InhibitedSystemsRestored = false }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, incompleteClose.StatusCode);
        Assert.Equal("permit.closure.verification_incomplete", await ProblemCodeAsync(incompleteClose));

        using var requestFollowUp = await manager.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/closure-tasks/{closureTask.Id}/request-evidence",
            closureRequested.ETag,
            new PermitReasonRequest(
                "Pekerjaan belum selesai - Officer Operasi: flange masih harus dipasang.")));
        requestFollowUp.EnsureSuccessStatusCode();
        var needsReplacement = Required(await requestFollowUp.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("CLOSURE_REQUESTED", needsReplacement.Status);
        Assert.Contains("flange", needsReplacement.Workflow.Closure.ReplacementReason);

        using var closeWhileReplacementPending = await manager.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/closure-tasks/{closureTask.Id}/close",
            needsReplacement.ETag,
            completeSection10));
        Assert.Equal(HttpStatusCode.Conflict, closeWhileReplacementPending.StatusCode);
        Assert.Equal(
            "permit.closure.evidence_replacement_pending",
            await ProblemCodeAsync(closeWhileReplacementPending));

        using var managerResubmit = await manager.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/permits/{issued.Id}/closure-requests/resubmit",
            needsReplacement.ETag,
            new RequestClosureRequest(
                printPackageId,
                [upload.Attachment.Id],
                "Percobaan pengajuan ulang oleh Pemilik Wilayah.",
                true,
                true)));
        Assert.Equal(HttpStatusCode.Forbidden, managerResubmit.StatusCode);

        using var replacementUploadContent = new MultipartFormDataContent();
        var replacementFile = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7\nreplacement\n"));
        replacementFile.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        replacementUploadContent.Add(replacementFile, "file", "signed-field-copy-latest.pdf");
        replacementUploadContent.Add(new StringContent("SIGNED_FIELD_COPY"), "category");
        replacementUploadContent.Add(new StringContent("SFC-001"), "documentNumber");
        replacementUploadContent.Add(new StringContent("2"), "documentRevision");
        replacementUploadContent.Add(new StringContent(DateTimeOffset.UtcNow.ToString("O")), "documentDate");
        replacementUploadContent.Add(new StringContent(printPackageId.ToString()), "printPackageId");
        using var replacementUploadRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/permits/{issued.Id}/attachments")
        {
            Content = replacementUploadContent
        };
        replacementUploadRequest.Headers.TryAddWithoutValidation("If-Match", needsReplacement.ETag);
        replacementUploadRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using var replacementUploadResponse = await sponsor.SendAsync(replacementUploadRequest);
        Assert.True(
            replacementUploadResponse.IsSuccessStatusCode,
            await replacementUploadResponse.Content.ReadAsStringAsync());
        var replacementUpload = Required(
            await replacementUploadResponse.Content.ReadFromJsonAsync<PermitAttachmentMutationResponse>());

        using var resubmitResponse = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/permits/{issued.Id}/closure-requests/resubmit",
            replacementUpload.ETag,
            new RequestClosureRequest(
                printPackageId,
                [replacementUpload.Attachment.Id],
                "Pekerjaan telah selesai setelah tindak lanjut dan hardcopy diperbarui.",
                true,
                true)));
        resubmitResponse.EnsureSuccessStatusCode();
        var resubmitted = Required(await resubmitResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("CLOSURE_REQUESTED", resubmitted.Status);
        Assert.Null(resubmitted.Workflow.Closure.ReplacementReason);
        Assert.Equal(
            [replacementUpload.Attachment.Id],
            resubmitted.Workflow.Closure.SignedFieldCopyAttachmentIds);

        var refreshedClosureTask = await PendingTaskAsync(issued.Id, "AREA_CLOSE_VERIFICATION");
        Assert.Equal(closureTask.Id, refreshedClosureTask.Id);
        Assert.Equal(resubmitted.Version, refreshedClosureTask.BusinessPermitVersion);

        using var ownerClose = await seniorOfficer.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/closure-tasks/{closureTask.Id}/close",
            resubmitted.ETag,
            completeSection10));
        ownerClose.EnsureSuccessStatusCode();
        var closed = Required(await ownerClose.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("CLOSED", closed.Status);
        Assert.Equal("Officer Operasi", closed.Workflow.Closure.OfficerName);
        Assert.True(closed.Workflow.Closure.WorkAreaInspectedAndClean);
        Assert.True(closed.Workflow.Closure.ManagerAgreesWorkCompleted);
        Assert.True(closed.Workflow.Closure.InhibitedSystemsRestored);
        Assert.True(closed.Workflow.Closure.AreaHandedBackAndSafeguardsRestored);
        Assert.True(closed.Workflow.Closure.EvidenceReadable);
    }

    [Fact]
    public async Task RenewalCreatesSuccessorDraftOnlyAfterAreaOwnerApprovesVerifiedFieldCopy()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        using var validator = Client(Unique("hse"), "HSEValidator", "ORF");
        using var manager = Client(Unique("manager"), "AreaOwnerManager", "ORF");
        var validated = await CreateAndValidateAsync(sponsor, validator, sponsorId);
        var approvalTask = await PendingTaskAsync(validated.Id, "AREA_APPROVE_AND_ISSUE");
        using var issueResponse = await manager.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{approvalTask.Id}/approve-and-issue",
            validated.ETag,
            new ApproveAndIssuePermitRequest("Disetujui dan diterbitkan.", null)));
        issueResponse.EnsureSuccessStatusCode();
        var issued = Required(await issueResponse.Content.ReadFromJsonAsync<PermitResponse>());

        Guid printPackageId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
            var package = await db.PrintPackageSnapshots.SingleAsync(x => x.PermitId == issued.Id);
            package.RenderStatus = "READY";
            printPackageId = package.Id;
            await db.SaveChangesAsync();
        }

        using var uploadContent = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7\n"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        uploadContent.Add(file, "file", "latest-field-copy.pdf");
        uploadContent.Add(new StringContent("SIGNED_FIELD_COPY"), "category");
        uploadContent.Add(new StringContent("SFC-RENEW-001"), "documentNumber");
        uploadContent.Add(new StringContent("1"), "documentRevision");
        uploadContent.Add(new StringContent(DateTimeOffset.UtcNow.ToString("O")), "documentDate");
        uploadContent.Add(new StringContent(printPackageId.ToString()), "printPackageId");
        using var uploadRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/permits/{issued.Id}/attachments")
        {
            Content = uploadContent
        };
        uploadRequest.Headers.TryAddWithoutValidation("If-Match", issued.ETag);
        uploadRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using var uploadResponse = await sponsor.SendAsync(uploadRequest);
        uploadResponse.EnsureSuccessStatusCode();
        var upload = Required(await uploadResponse.Content.ReadFromJsonAsync<PermitAttachmentMutationResponse>());

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
            var attachment = await db.PermitAttachments.SingleAsync(x => x.Id == upload.Attachment.Id);
            attachment.ScanStatus = "CLEAN";
            attachment.ScanEvidenceReference = "integration-test-clean-evidence";
            attachment.ScannedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        using var requestResponse = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/permits/{issued.Id}/renew",
            upload.ETag,
            new RequestPermitRenewalRequest(
                issued.Draft.ValidUntil,
                issued.Draft.ValidUntil.AddDays(1),
                printPackageId,
                [upload.Attachment.Id],
                "Pekerjaan belum selesai dan perlu dilanjutkan.",
                true,
                true)));
        requestResponse.EnsureSuccessStatusCode();
        var requested = Required(await requestResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Null(requested.RenewalPermitId);
        Assert.Equal("PENDING", requested.Workflow.Renewal.Status);

        var renewalTask = await PendingTaskAsync(issued.Id, "AREA_RENEWAL_REVIEW");
        using var sponsorApproval = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/renewal-tasks/{renewalTask.Id}/approve",
            requested.ETag,
            new ApproveRenewalRequest("Tidak berwenang.", true, true)));
        Assert.Equal(HttpStatusCode.Forbidden, sponsorApproval.StatusCode);

        using var ownerApproval = await manager.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/renewal-tasks/{renewalTask.Id}/approve",
            requested.ETag,
            new ApproveRenewalRequest("Hardcopy lapangan telah diverifikasi.", true, true)));
        ownerApproval.EnsureSuccessStatusCode();
        var result = Required(await ownerApproval.Content.ReadFromJsonAsync<PermitRenewalResponse>());
        Assert.Equal("DRAFT", result.Renewal.Status);
        Assert.Equal(issued.Id, result.Renewal.RenewedFromPermitId);
        Assert.Equal(result.Renewal.Id, (await sponsor.GetFromJsonAsync<PermitResponse>(
            $"/api/v1/permits/{issued.Id}"))?.RenewalPermitId);
    }

    private async Task<PermitResponse> CreateAndValidateAsync(
        HttpClient sponsor,
        HttpClient validator,
        string sponsorId,
        string location = "ORF")
    {
        var submitted = await CreateAndSubmitAsync(sponsor, sponsorId, location);
        var task = await PendingTaskAsync(submitted.Id, "HSE_VALIDATION");
        using var response = await validator.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{task.Id}/validate",
            submitted.ETag,
            new ValidateSubmissionRequest(
                "JSA dan requirement telah diverifikasi.",
                ["SAFETY_FIRE_EXTINGUISHER", "SAFETY_LOTO"])));
        response.EnsureSuccessStatusCode();
        var validated = Required(await response.Content.ReadFromJsonAsync<PermitResponse>());

        using var seniorOfficer = Client(Unique("senior-officer"), "AreaOwnerSeniorOfficer", location);
        var areaTask = await PendingTaskAsync(validated.Id, "AREA_OPERATION_REVIEW");
        using var reviewResponse = await seniorOfficer.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{areaTask.Id}/review-area-operations",
            validated.ETag,
            AreaOperationsReview()));
        reviewResponse.EnsureSuccessStatusCode();
        return Required(await reviewResponse.Content.ReadFromJsonAsync<PermitResponse>());
    }

    private static ReviewAreaOperationsRequest AreaOperationsReview() => new(
        "Seluruh kondisi operasi yang relevan telah diperiksa.",
        [
            PermitOperationalConditionCatalog.Isolation,
            PermitOperationalConditionCatalog.IsolationClosedLockValves,
            PermitOperationalConditionCatalog.Depressurized
        ],
        null,
        true);

    private static async Task<PermitResponse> CreateAndSubmitAsync(
        HttpClient sponsor,
        string sponsorId,
        string location = "ORF")
    {
        var draft = await CreateAsync(sponsor, sponsorId, location);
        draft = await UploadMandatoryDocumentsAsync(sponsor, draft);
        using var response = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/permits/{draft.Id}/submit",
            draft.ETag,
            ReadyToSubmit()));
        response.EnsureSuccessStatusCode();
        return Required(await response.Content.ReadFromJsonAsync<PermitResponse>());
    }

    private static async Task<PermitResponse> UploadMandatoryDocumentsAsync(
        HttpClient client,
        PermitResponse permit,
        string? omittedCode = null)
    {
        if (!string.Equals(omittedCode, "JSA", StringComparison.OrdinalIgnoreCase))
        {
            permit = await UploadJsaAsync(client, permit);
        }

        foreach (var code in new[] { "WORK_PROCEDURE", "ID", "BPJS_TK", "FTW", "ESIMI" })
        {
            if (string.Equals(omittedCode, code, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            permit = await UploadSupportingDocumentAsync(client, permit, code);
        }

        return permit;
    }

    private static async Task<PermitResponse> UploadJsaAsync(HttpClient client, PermitResponse permit)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7\n%%EOF\n"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "file", "jsa.pdf");
        content.Add(new StringContent("JSA"), "category");
        content.Add(new StringContent("JSA"), "supportingDocumentCode");
        content.Add(new StringContent(Required(permit.Draft.JsaDocumentNumber)), "documentNumber");
        content.Add(new StringContent(Required(permit.Draft.JsaRevision)), "documentRevision");
        content.Add(new StringContent(permit.Draft.JsaDate!.Value.ToString("O")), "documentDate");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/permits/{permit.Id}/attachments")
        {
            Content = content
        };
        request.Headers.TryAddWithoutValidation("If-Match", permit.ETag);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var mutation = Required(
            await response.Content.ReadFromJsonAsync<PermitAttachmentMutationResponse>());
        return permit with { ETag = mutation.ETag, Version = mutation.PermitVersion };
    }

    private static async Task<PermitResponse> UploadSupportingDocumentAsync(
        HttpClient client,
        PermitResponse permit,
        string documentCode)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7\n%%EOF\n"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "file", $"{documentCode.ToLowerInvariant()}.pdf");
        content.Add(new StringContent("SUPPORTING"), "category");
        content.Add(new StringContent(documentCode), "supportingDocumentCode");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/permits/{permit.Id}/attachments")
        {
            Content = content
        };
        request.Headers.TryAddWithoutValidation("If-Match", permit.ETag);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var mutation = Required(
            await response.Content.ReadFromJsonAsync<PermitAttachmentMutationResponse>());
        return permit with { ETag = mutation.ETag, Version = mutation.PermitVersion };
    }

    private static async Task<PermitResponse> CreateAsync(HttpClient client, string sponsorId, string location)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/permits", Draft(sponsorId, location));
        response.EnsureSuccessStatusCode();
        return Required(await response.Content.ReadFromJsonAsync<PermitResponse>());
    }

    private async Task<PermitTaskRecord> PendingTaskAsync(Guid permitId, string type)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
        return await db.PermitTasks.AsNoTracking().SingleAsync(
            x => x.PermitId == permitId && x.Type == type && x.Status == "PENDING");
    }

    private HttpClient Client(
        string userId,
        string roles,
        string locations,
        string? displayName = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", userId);
        client.DefaultRequestHeaders.Add("X-Dev-Name", displayName ?? userId);
        client.DefaultRequestHeaders.Add("X-Dev-Roles", roles);
        client.DefaultRequestHeaders.Add("X-Dev-Locations", locations);
        return client;
    }

    private static HttpRequestMessage Command<T>(
        HttpMethod method,
        string path,
        string etag,
        T body,
        string? idempotencyKey = "auto")
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        if (idempotencyKey is not null)
        {
            request.Headers.Add(
                "Idempotency-Key",
                idempotencyKey == "auto" ? Guid.NewGuid().ToString("N") : idempotencyKey);
        }

        return request;
    }

    private static SubmitPermitRequest ReadyToSubmit() => new(true, true, true, []);

    private static PermitDraftRequest Draft(string sponsorId, string location)
    {
        var now = DateTimeOffset.UtcNow;
        return new PermitDraftRequest(
            $"Pekerjaan {Guid.NewGuid():N}",
            "Pekerjaan sesuai JSA.",
            location,
            sponsorId,
            "Pelaksana Uji",
            "PT Mitra Uji",
            "HotWork",
            "High",
            now.AddHours(1),
            now.AddDays(1),
            "esimi-test",
            $"ESM-{Guid.NewGuid():N}",
            [],
            [],
            ["JSA"],
            JsaDocumentNumber: "JSA-TEST-001",
            JsaRevision: "1",
            JsaDate: now,
            WorkTypeCodes: ["HOT_WELDING"],
            HeaderClassificationCodes: ["HOT_OPEN_FLAME"]);
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static string Unique(string prefix) => $"{prefix}.{Guid.NewGuid():N}";

    private static T Required<T>(T? value) where T : class => Assert.IsType<T>(value);
}
