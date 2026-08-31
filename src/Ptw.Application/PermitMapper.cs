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
            permit.Status.ToString().ToUpperInvariant(),
            permit.Version,
            draft.ToRequest(),
            permit.CreatedAt,
            permit.UpdatedAt,
            permit.ActiveWorkPeriodId,
            permit.SuspensionReason,
            stored.ETag);
    }

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
