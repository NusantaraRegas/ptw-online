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
            request.RequiredDocumentCodes);
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
            permit.ActiveWorkPeriodId,
            permit.SuspensionReason,
            permit.RenewedFromPermitId,
            permit.RenewalPermitId,
            new PermitWorkflowResponse(
                ToValidationResponse(
                    "HSSE",
                    "Validasi HSSE",
                    permit.HsseValidation),
                ToValidationResponse(
                    "GAS_DISTRIBUTION",
                    "Validasi operasional legacy (tidak berlaku untuk route baru)",
                    permit.GasDistributionValidation),
                permit.Approval?.ActorId,
                permit.Approval?.Statement,
                permit.Approval?.ApprovedAt,
                new PermitSuspensionResponse(
                    permit.Suspension is not null,
                    permit.Suspension?.RequestedBy,
                    permit.Suspension?.Reason,
                    permit.Suspension?.RequestedAt,
                    permit.Suspension?.ApprovedAt is not null,
                    permit.Suspension?.ApprovedBy,
                    permit.Suspension?.ApprovalStatement,
                    permit.Suspension?.ApprovedAt),
                new PermitCompletionResponse(
                    ToCompletionResponse(
                        "SPONSOR",
                        "Konfirmasi Sponsor",
                        permit.SponsorCompletion),
                    ToCompletionResponse(
                        "HSSE",
                        "Konfirmasi HSSE",
                        permit.HsseCompletion),
                    ToCompletionResponse(
                        "AREA_OWNER",
                        "Konfirmasi PIC pemilik area",
                        permit.AreaOwnerCompletion))),
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
        evidence?.ValidatedAt);

    private static PermitValidationResponse ToCompletionResponse(
        string code,
        string label,
        PermitCompletionEvidence? evidence) => new(
        code,
        label,
        evidence is not null,
        evidence?.ActorId,
        evidence?.Statement,
        evidence?.ConfirmedAt);

    private static string ToUpperSnakeCase(string value) => string.Concat(
        value.Select((character, index) =>
            index > 0 && char.IsUpper(character) ? $"_{character}" : character.ToString()))
        .ToUpperInvariant();

    public static PermitDraftRequest ToRequest(this PermitDraft draft) => new(
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
        draft.RequiredDocumentCodes);
}
