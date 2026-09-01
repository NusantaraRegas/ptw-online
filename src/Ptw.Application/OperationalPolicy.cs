using Ptw.Contracts;

namespace Ptw.Application;

public static class PermitPolicyOperations
{
    public const string CreateDraft = "CreateDraft";
    public const string UpdateDraft = "UpdateDraft";
    public const string Submit = "Submit";

    public static readonly IReadOnlyList<string> Required = [CreateDraft, UpdateDraft, Submit];
}

public sealed class OperationalPolicySettings
{
    public bool EnforceMasterAuthorization { get; init; }
    public string PolicyVersion { get; init; } = string.Empty;
    public Dictionary<string, string> AcceptedDecisionReferences { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> PermitActionCodes { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed record PolicyAuthorizationEvidence(
    string PolicyVersion,
    string Operation,
    string ActionCode,
    Guid LocationMasterId,
    IReadOnlyList<Guid> AssignmentIds,
    IReadOnlyList<string> VerifiedCompetencyCodes,
    IReadOnlyDictionary<string, string> DecisionReferences,
    DateTimeOffset EvaluatedAt);

public interface IOperationalPolicyGate
{
    Task<OperationalPolicyReadinessResponse> GetReadinessAsync(CancellationToken cancellationToken);

    Task<PolicyAuthorizationEvidence?> AuthorizePermitCommandAsync(
        Actor actor,
        string operation,
        string locationCode,
        CancellationToken cancellationToken);
}

public sealed class OperationalPolicyGate(
    OperationalPolicySettings settings,
    ILocationMasterStore locationStore,
    IUserAuthorizationStore authorizationStore,
    IAuthorizationAssignmentResolver authorizationResolver,
    IPolicyUatStore policyUatStore,
    IClock clock) : IOperationalPolicyGate
{
    private static readonly string[] RequiredDecisions = ["OPN-001", "OPN-002"];

    public async Task<OperationalPolicyReadinessResponse> GetReadinessAsync(
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var requirements = new List<PolicyRequirementResponse>();

        AddRequirement(
            requirements,
            "policy.version",
            "Versi policy",
            !string.IsNullOrWhiteSpace(settings.PolicyVersion),
            "Versi konfigurasi policy wajib dicatat sebelum aktivasi.");

        PolicyUatRunSummaryResponse? passingUatRun = null;
        if (!string.IsNullOrWhiteSpace(settings.PolicyVersion))
        {
            passingUatRun = await policyUatStore.FindLatestPassingRunAsync(
                settings.PolicyVersion,
                cancellationToken);
        }

        AddRequirement(
            requirements,
            "uat.passing_run",
            "Bukti UAT policy",
            passingUatRun is not null,
            passingUatRun is null
                ? "Belum ada UAT lulus untuk versi policy yang dikonfigurasi."
                : $"Run {passingUatRun.Id} lulus pada {passingUatRun.ExecutedAt:O}; hash laporan {passingUatRun.ReportHash}.");

        foreach (var decision in RequiredDecisions)
        {
            var configured = TryGetNonEmpty(settings.AcceptedDecisionReferences, decision, out var reference);
            AddRequirement(
                requirements,
                $"decision.{decision.ToLowerInvariant()}",
                $"Pengesahan {decision}",
                configured,
                configured
                    ? $"Referensi: {reference}"
                    : $"Referensi keputusan {decision} yang telah disahkan belum dikonfigurasi.");
        }

        foreach (var operation in PermitPolicyOperations.Required)
        {
            var configured = TryGetNonEmpty(settings.PermitActionCodes, operation, out var actionCode);
            AddRequirement(
                requirements,
                $"action.{operation.ToLowerInvariant()}",
                $"Action untuk {operation}",
                configured,
                configured
                    ? $"Action: {actionCode}"
                    : $"Mapping action untuk command {operation} belum dikonfigurasi.");
        }

        var approvedLocations = await locationStore.CountApprovedEffectiveAsync(now, cancellationToken);
        AddRequirement(
            requirements,
            "location.approved_effective",
            "Master lokasi efektif",
            approvedLocations > 0,
            approvedLocations > 0
                ? $"{approvedLocations} lokasi disetujui sedang efektif."
                : "Belum ada lokasi disetujui yang efektif.");

        var approvedAssignments = await authorizationStore.CountApprovedEffectiveAsync(now, cancellationToken);
        AddRequirement(
            requirements,
            "authorization.approved_effective",
            "Assignment efektif",
            approvedAssignments > 0,
            approvedAssignments > 0
                ? $"{approvedAssignments} assignment disetujui sedang efektif."
                : "Belum ada assignment disetujui yang efektif.");

        return new OperationalPolicyReadinessResponse(
            settings.EnforceMasterAuthorization,
            requirements.All(item => item.Satisfied),
            settings.EnforceMasterAuthorization ? "MASTER_AUTHORIZATION" : "PREPARATION",
            settings.PolicyVersion,
            requirements,
            now);
    }

    public async Task<PolicyAuthorizationEvidence?> AuthorizePermitCommandAsync(
        Actor actor,
        string operation,
        string locationCode,
        CancellationToken cancellationToken)
    {
        if (!settings.EnforceMasterAuthorization)
        {
            return null;
        }

        var readiness = await GetReadinessAsync(cancellationToken);
        if (!readiness.ReadyForActivation)
        {
            throw new PolicyActivationException(
                "Master authorization diaktifkan, tetapi konfigurasi belum memenuhi seluruh prasyarat OPN-001/002.");
        }

        if (!TryGetNonEmpty(settings.PermitActionCodes, operation, out var actionCode))
        {
            throw new PolicyActivationException($"Mapping action untuk command {operation} tidak tersedia.");
        }

        var now = clock.UtcNow;
        var locations = await locationStore.FindApprovedEffectiveByCodeAsync(
            locationCode,
            now,
            cancellationToken);
        if (locations.Count == 0)
        {
            throw new PolicyAuthorizationDeniedException(
                "authorization.location_not_effective",
                "Kode lokasi PTW tidak memiliki satu master lokasi disetujui yang efektif.");
        }

        if (locations.Count > 1)
        {
            throw new PolicyAuthorizationDeniedException(
                "authorization.location_ambiguous",
                "Kode lokasi PTW memiliki periode master yang overlap dan tidak dapat ditentukan secara aman.");
        }

        var locationId = locations[0].Entry.Id;
        var resolution = await authorizationResolver.ResolveAsync(
            actor.Id,
            actionCode,
            locationId,
            now,
            cancellationToken);
        if (!resolution.IsResolved)
        {
            throw new PolicyAuthorizationDeniedException(
                resolution.Code,
                "Assignment aktif yang tidak ambigu tidak tersedia untuk actor, action, dan lokasi ini.");
        }

        var missingCompetencies = resolution.RequiredCompetencyCodes
            .Where(code => !actor.CompetencyCodes.Contains(code))
            .ToArray();
        if (missingCompetencies.Length > 0)
        {
            throw new PolicyAuthorizationDeniedException(
                "authorization.competency_missing",
                $"Bukti kompetensi aktif tidak tersedia untuk: {string.Join(", ", missingCompetencies)}.");
        }

        return new PolicyAuthorizationEvidence(
            settings.PolicyVersion,
            operation,
            actionCode,
            locationId,
            resolution.AssignmentIds,
            resolution.RequiredCompetencyCodes,
            RequiredDecisions.ToDictionary(
                decision => decision,
                decision => settings.AcceptedDecisionReferences[decision],
                StringComparer.OrdinalIgnoreCase),
            now);
    }

    private static bool TryGetNonEmpty(
        Dictionary<string, string> values,
        string key,
        out string value)
    {
        if (values.TryGetValue(key, out var configured) && !string.IsNullOrWhiteSpace(configured))
        {
            value = configured.Trim();
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static void AddRequirement(
        List<PolicyRequirementResponse> requirements,
        string code,
        string label,
        bool satisfied,
        string detail) => requirements.Add(new PolicyRequirementResponse(code, label, satisfied, detail));
}

public sealed class OperationalPolicyService(
    IOperationalPolicyGate gate,
    IActorContext actorContext)
{
    public Task<OperationalPolicyReadinessResponse> GetReadinessAsync(CancellationToken cancellationToken)
    {
        if (!actorContext.Current.Roles.Contains("Administrator"))
        {
            throw new UnauthorizedAccessException("Peran Administrator diperlukan untuk melihat kesiapan policy.");
        }

        return gate.GetReadinessAsync(cancellationToken);
    }
}
