using Ptw.Contracts;
using Ptw.Domain;

namespace Ptw.Application;

internal static class PermitMapper
{
    public static PermitDraft ToDomain(this PermitDraftRequest request)
    {
        if (!Enum.TryParse<PermitClass>(request.PermitClass, true, out var permitClass))
        {
            throw new InvalidRequestException("permit.invalid_class", "Kelas PTW tidak dikenali.");
        }

        if (!Enum.TryParse<RiskLevel>(request.RiskLevel, true, out var riskLevel))
        {
            throw new InvalidRequestException("permit.invalid_risk", "Tingkat risiko tidak dikenali.");
        }

        return new PermitDraft(
            request.Title,
            request.Description,
            request.LocationId,
            request.SponsorId,
            request.PerformingAuthority,
            request.Company,
            permitClass,
            riskLevel,
            request.ValidFrom,
            request.ValidUntil,
            request.ESimiExternalId,
            request.ESimiNumber,
            request.Hazards,
            request.Controls,
            request.RequiredDocumentCodes,
            request.SubmitterType,
            request.WorkTypeCode,
            request.EquipmentTag,
            request.PlantArea,
            request.ClsrApplicable,
            request.SimopsDeclaration,
            request.SafetyEquipmentCodes,
            request.IsolationPrecautionCodes,
            request.JsaDocumentNumber,
            request.JsaRevision,
            request.JsaDate,
            request.WorkTypeCodes,
            request.OtherWorkTypeDescription,
            request.EquipmentName,
            request.WorkOrderNumber,
            request.AdditionalHazardReference,
            request.HeaderClassificationCodes);
    }

    public static PermitResponse ToResponse(this StoredPermit stored)
    {
        var permit = stored.Permit;
        var draft = permit.Draft;
        return new PermitResponse(
            permit.Id,
            permit.PermitNumber,
            ToUpperSnakeCase(permit.Status.ToString()),
            permit.Version,
            draft.ToRequest(),
            permit.CreatedAt,
            permit.UpdatedAt,
            permit.SuspensionReason,
            permit.RenewedFromPermitId,
            permit.RenewalPermitId,
            new PermitWorkflowResponse(
                ToValidationResponse(
                    "HSE",
                    "Validasi PIC HSE",
                    permit.HseValidation),
                new PermitApprovalResponse(
                    permit.Approval is not null,
                    permit.Approval?.ActorId,
                    permit.Approval?.ActorPosition,
                    permit.Approval is null ? null : ToUpperSnakeCase(permit.Approval.Capacity.ToString()),
                    permit.Approval?.PrincipalManagerUserId,
                    permit.Approval?.PrincipalPosition,
                    permit.Approval?.AuthorizationId,
                    permit.Approval?.ActingAssignmentId,
                    permit.Approval?.Statement,
                    permit.Approval?.ApprovedAt),
                new PermitSuspensionResponse(
                    permit.Suspension is not null,
                    permit.Suspension?.SuspendedBy,
                    permit.Suspension?.Reason,
                    permit.Suspension?.SuspendedAt,
                    permit.Suspension?.ResolvedBy,
                    permit.Suspension?.Resolution,
                    permit.Suspension?.ResolvedAt),
                new PermitRenewalRequestResponse(
                    permit.RenewalRequest is not null,
                    permit.RenewalRequest is null
                        ? null
                        : ToUpperSnakeCase(permit.RenewalRequest.Status.ToString()),
                    permit.RenewalRequest?.PrintPackageId,
                    permit.RenewalRequest?.AttachmentIds ?? [],
                    permit.RenewalRequest?.RequestedBy,
                    permit.RenewalRequest?.ContinuationStatement,
                    permit.RenewalRequest?.ValidFrom,
                    permit.RenewalRequest?.ValidUntil,
                    permit.RenewalRequest?.RequestedAt,
                    permit.RenewalRequest?.Revision ?? 0,
                    permit.RenewalRequest?.ReplacementReason,
                    permit.RenewalRequest?.DecidedBy,
                    permit.RenewalRequest?.DecisionStatement,
                    permit.RenewalRequest?.DecidedAt),
                new PermitClosureResponse(
                    permit.ClosureRequest is not null,
                    permit.ClosureRequest?.PrintPackageId,
                    permit.ClosureRequest?.AttachmentIds ?? [],
                    permit.ClosureRequest?.RequestedBy,
                    permit.ClosureRequest?.CompletionStatement,
                    permit.ClosureRequest?.RequestedAt,
                    permit.ClosureRequest?.Revision ?? 0,
                    permit.ClosureRequest?.ReplacementReason,
                    permit.ClosureDecision is not null,
                    permit.ClosureDecision?.ActorId,
                    permit.ClosureDecision?.Statement,
                    permit.ClosureDecision?.ClosedAt)),
            stored.ETag);
    }

    private static PermitValidationResponse ToValidationResponse(
        string code,
        string label,
        PermitValidationEvidence? evidence) => new(
        code,
        label,
        evidence is not null,
        evidence?.ActorId,
        evidence?.Statement,
        evidence?.ValidatedAt,
        evidence?.SafetyEquipmentCodes ?? []);

    private static string ToUpperSnakeCase(string value) => string.Concat(
        value.Select((character, index) =>
            index > 0 && char.IsUpper(character) ? $"_{character}" : character.ToString()))
        .ToUpperInvariant();

    public static PermitDraftRequest ToRequest(this PermitDraft draft)
    {
        var workTypeCodes = PermitWorkTypeCatalog.NormalizeAndValidate(
            draft.PermitClass,
            draft.WorkTypeCodes,
            draft.WorkTypeCode);
        return new PermitDraftRequest(
            draft.Title,
            draft.Description,
            draft.LocationId,
            draft.SponsorId,
            draft.PerformingAuthority,
            draft.Company,
            draft.PermitClass.ToString(),
            draft.RiskLevel.ToString(),
            draft.ValidFrom,
            draft.ValidUntil,
            draft.ESimiExternalId,
            draft.ESimiNumber,
            draft.Hazards,
            draft.Controls,
            draft.RequiredDocumentCodes,
            draft.SubmitterType,
            workTypeCodes[0],
            draft.EquipmentTag,
            draft.PlantArea,
            draft.ClsrApplicable,
            draft.SimopsDeclaration,
            draft.SafetyEquipmentCodes,
            draft.IsolationPrecautionCodes,
            draft.JsaDocumentNumber,
            draft.JsaRevision,
            draft.JsaDate,
            workTypeCodes,
            draft.OtherWorkTypeDescription,
            draft.EquipmentName,
            draft.WorkOrderNumber,
            draft.AdditionalHazardReference,
            draft.HeaderClassificationCodes);
    }
}
