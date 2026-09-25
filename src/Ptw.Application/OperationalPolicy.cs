namespace Ptw.Application;

public static class PermitPolicyOperations
{
    public const string CreateDraft = "CreateDraft";
    public const string UpdateDraft = "UpdateDraft";
    public const string RequestRenewal = "RequestRenewal";
    public const string ApproveRenewal = "ApproveRenewal";
    public const string RequestRenewalEvidenceReplacement = "RequestRenewalEvidenceReplacement";
    public const string RejectRenewal = "RejectRenewal";
    public const string Submit = "Submit";
    public const string ValidateSubmission = "ValidateSubmission";
    public const string EscalateValidation = "EscalateValidation";
    public const string ReviewAreaOperations = "ReviewAreaOperations";
    public const string ApproveAndIssue = "ApproveAndIssuePermit";
    public const string RequestRevision = "RequestRevision";
    public const string Reject = "Reject";
    public const string Suspend = "SuspendPermit";
    public const string ResolveSuspension = "ResolveSuspension";
    public const string RequestClosure = "RequestClosure";
    public const string RequestClosureEvidenceReplacement = "RequestClosureEvidenceReplacement";
    public const string ResubmitClosure = "ResubmitClosure";
    public const string Close = "ClosePermit";
    public const string Cancel = "CancelPermit";
    public const string Expire = "ExpirePermit";

    public static readonly IReadOnlyList<string> Required =
    [
        CreateDraft,
        UpdateDraft,
        RequestRenewal,
        ApproveRenewal,
        RequestRenewalEvidenceReplacement,
        RejectRenewal,
        Submit,
        ValidateSubmission,
        EscalateValidation,
        ReviewAreaOperations,
        ApproveAndIssue,
        RequestRevision,
        Reject,
        Suspend,
        ResolveSuspension,
        RequestClosure,
        RequestClosureEvidenceReplacement,
        ResubmitClosure,
        Close,
        Cancel,
        Expire
    ];
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

public sealed class LocationReleaseSettings
{
    public Dictionary<string, string> AreaOwnerDepartments { get; init; } = [];

    public bool TryGetAreaOwnerDepartment(string locationCode, out string department)
    {
        foreach (var route in AreaOwnerDepartments)
        {
            if (string.Equals(route.Key, locationCode, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(route.Value))
            {
                department = route.Value.Trim();
                return true;
            }
        }

        department = string.Empty;
        return false;
    }
}

public sealed class IssuancePolicySettings
{
    public bool Approved { get; init; }
    public string RuleVersion { get; init; } = string.Empty;
    public string PrintTemplateVersion { get; init; } = string.Empty;
    public string CampaignAssetVersion { get; init; } = string.Empty;

    public bool IsReady => Approved
        && !string.IsNullOrWhiteSpace(RuleVersion)
        && !string.IsNullOrWhiteSpace(PrintTemplateVersion)
        && !string.IsNullOrWhiteSpace(CampaignAssetVersion);
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
    IClock clock) : IOperationalPolicyGate
{
    private static readonly string[] RequiredDecisions = ["OPN-001", "OPN-002"];

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

        var now = clock.UtcNow;
        var configurationComplete = !string.IsNullOrWhiteSpace(settings.PolicyVersion)
            && RequiredDecisions.All(decision =>
                TryGetNonEmpty(settings.AcceptedDecisionReferences, decision, out _))
            && PermitPolicyOperations.Required.All(requiredOperation =>
                TryGetNonEmpty(settings.PermitActionCodes, requiredOperation, out _))
            && await locationStore.CountApprovedEffectiveAsync(now, cancellationToken) > 0
            && await authorizationStore.CountApprovedEffectiveAsync(now, cancellationToken) > 0;
        if (!configurationComplete)
        {
            throw new PolicyActivationException(
                "Master authorization diaktifkan, tetapi konfigurasi belum memenuhi seluruh prasyarat OPN-001/002.");
        }

        if (!TryGetNonEmpty(settings.PermitActionCodes, operation, out var actionCode))
        {
            throw new PolicyActivationException($"Mapping action untuk command {operation} tidak tersedia.");
        }

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
}
