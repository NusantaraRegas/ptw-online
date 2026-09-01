using Ptw.Contracts;

namespace Ptw.Application;

public sealed class PolicySimulationService(
    OperationalPolicySettings settings,
    ILocationMasterStore locationStore,
    IUserAuthorizationStore authorizationStore,
    IAuthorizationAssignmentResolver authorizationResolver,
    IActorContext actorContext,
    IClock clock)
{
    public async Task<PolicySimulationResponse> SimulateAsync(
        PolicySimulationRequest request,
        CancellationToken cancellationToken)
    {
        EnsureAdministrator();
        var input = Normalize(request);
        var evaluatedAt = (input.EvaluatedAt ?? clock.UtcNow).ToUniversalTime();
        var checks = new List<PolicySimulationCheckResponse>();

        var locations = await locationStore.FindApprovedEffectiveByCodeAsync(
            input.LocationCode,
            evaluatedAt,
            cancellationToken);
        if (locations.Count == 0)
        {
            checks.Add(new PolicySimulationCheckResponse(
                "location.effective",
                "Master lokasi efektif",
                false,
                "Tidak ada master lokasi disetujui yang efektif untuk kode dan waktu simulasi."));
            return Denied(
                "authorization.location_not_effective",
                "Simulasi ditolak karena lokasi tidak efektif.",
                evaluatedAt,
                null,
                [],
                [],
                [],
                checks);
        }

        if (locations.Count > 1)
        {
            checks.Add(new PolicySimulationCheckResponse(
                "location.unambiguous",
                "Master lokasi tunggal",
                false,
                "Lebih dari satu periode lokasi disetujui sedang efektif untuk kode yang sama."));
            return Denied(
                "authorization.location_ambiguous",
                "Simulasi ditolak karena periode lokasi overlap.",
                evaluatedAt,
                null,
                [],
                [],
                [],
                checks);
        }

        var location = locations[0].Entry;
        var locationResponse = new PolicySimulationLocationResponse(
            location.Id,
            location.Code,
            location.Name,
            location.ParentId);
        checks.Add(new PolicySimulationCheckResponse(
            "location.effective",
            "Master lokasi efektif",
            true,
            $"{location.Code} — {location.Name} efektif pada waktu simulasi."));

        var resolution = await authorizationResolver.ResolveAsync(
            input.SubjectId,
            input.ActionCode,
            location.Id,
            evaluatedAt,
            cancellationToken);
        if (!resolution.IsResolved)
        {
            checks.Add(new PolicySimulationCheckResponse(
                "authorization.assignment",
                "Assignment aktif dan tidak ambigu",
                false,
                ResolutionDetail(resolution.Code)));
            return Denied(
                resolution.Code,
                "Simulasi ditolak oleh resolver assignment.",
                evaluatedAt,
                locationResponse,
                [],
                [],
                [],
                checks);
        }

        var assignments = new List<PolicySimulationAssignmentResponse>();
        foreach (var assignmentId in resolution.AssignmentIds)
        {
            var stored = await authorizationStore.FindAsync(assignmentId, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Assignment hasil resolver '{assignmentId}' tidak ditemukan.");
            var entry = stored.Entry;
            assignments.Add(new PolicySimulationAssignmentResponse(
                entry.Id,
                entry.RoleCode,
                entry.Kind.ToString().ToUpperInvariant(),
                entry.LocationId,
                entry.IncludeDescendants,
                entry.RequiredCompetencyCodes,
                entry.EffectiveFrom,
                entry.EffectiveUntil));
        }

        checks.Add(new PolicySimulationCheckResponse(
            "authorization.assignment",
            "Assignment aktif dan tidak ambigu",
            true,
            $"{assignments.Count} assignment pada {assignments.Select(item => item.RoleCode).Distinct(StringComparer.OrdinalIgnoreCase).Count()} role cocok."));

        var providedCompetencies = input.CompetencyCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingCompetencies = resolution.RequiredCompetencyCodes
            .Where(code => !providedCompetencies.Contains(code))
            .ToArray();
        if (missingCompetencies.Length > 0)
        {
            checks.Add(new PolicySimulationCheckResponse(
                "authorization.competency",
                "Kompetensi wajib",
                false,
                $"Kompetensi belum tersedia: {string.Join(", ", missingCompetencies)}."));
            return Denied(
                "authorization.competency_missing",
                "Simulasi ditolak karena kompetensi belum lengkap.",
                evaluatedAt,
                locationResponse,
                assignments,
                resolution.RequiredCompetencyCodes,
                missingCompetencies,
                checks);
        }

        checks.Add(new PolicySimulationCheckResponse(
            "authorization.competency",
            "Kompetensi wajib",
            true,
            resolution.RequiredCompetencyCodes.Count == 0
                ? "Assignment tidak mensyaratkan kompetensi tambahan."
                : "Seluruh kompetensi wajib tersedia pada input simulasi."));

        return new PolicySimulationResponse(
            true,
            "ALLOW",
            "authorization.simulation_allowed",
            "Actor memenuhi assignment, lokasi, periode, action, dan kompetensi pada skenario ini.",
            false,
            settings.EnforceMasterAuthorization,
            settings.PolicyVersion,
            evaluatedAt,
            locationResponse,
            assignments,
            resolution.RequiredCompetencyCodes,
            [],
            checks);
    }

    private PolicySimulationResponse Denied(
        string code,
        string summary,
        DateTimeOffset evaluatedAt,
        PolicySimulationLocationResponse? location,
        IReadOnlyList<PolicySimulationAssignmentResponse> assignments,
        IReadOnlyList<string> requiredCompetencies,
        IReadOnlyList<string> missingCompetencies,
        IReadOnlyList<PolicySimulationCheckResponse> checks) => new(
        false,
        "DENY",
        code,
        summary,
        false,
        settings.EnforceMasterAuthorization,
        settings.PolicyVersion,
        evaluatedAt,
        location,
        assignments,
        requiredCompetencies,
        missingCompetencies,
        checks);

    private static PolicySimulationRequest Normalize(PolicySimulationRequest request)
    {
        var subjectId = RequiredValue(request.SubjectId, "subjectId", 200);
        var actionCode = RequiredValue(request.ActionCode, "actionCode", 100);
        var locationCode = RequiredValue(request.LocationCode, "locationCode", 100);
        var competencyCodes = request.CompetencyCodes ?? [];
        if (competencyCodes.Count > 100)
        {
            throw new InvalidRequestException(
                "policy.simulation_invalid",
                "Maksimum 100 competency code dapat disimulasikan sekaligus.");
        }

        var competencies = competencyCodes
            .Select(code => RequiredValue(code, "competencyCode", 100))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return request with
        {
            SubjectId = subjectId,
            ActionCode = actionCode,
            LocationCode = locationCode,
            CompetencyCodes = competencies
        };
    }

    private static string RequiredValue(string value, string field, int maximumLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > maximumLength)
        {
            throw new InvalidRequestException(
                "policy.simulation_invalid",
                $"{field} wajib diisi dan maksimum {maximumLength} karakter.");
        }

        return normalized;
    }

    private static string ResolutionDetail(string code) => code switch
    {
        "authorization.assignment_ambiguous" =>
            "Lebih dari satu assignment efektif ditemukan untuk role, action, dan konteks yang sama.",
        _ => "Assignment disetujui yang efektif tidak tersedia untuk subject, action, dan lokasi."
    };

    private void EnsureAdministrator()
    {
        if (!actorContext.Current.Roles.Contains("Administrator"))
        {
            throw new UnauthorizedAccessException("Peran Administrator diperlukan untuk menjalankan simulasi policy.");
        }
    }
}
