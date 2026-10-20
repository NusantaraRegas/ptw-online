using Ptw.Domain;

namespace Ptw.Application;

/// <summary>Ownership rules shared by the permit and attachment services.</summary>
public static class PermitAccess
{
    public const string ContractorRole = "Contractor";

    /// <summary>A Contractor account that holds no Sponsor or Administrator role.</summary>
    public static bool IsContractorOnly(Actor actor) =>
        actor.Roles.Contains(ContractorRole) && !actor.Roles.Overlaps(["Sponsor", "Administrator"]);

    public static bool IsInitiator(Actor actor, Permit permit) =>
        permit.Initiation is not null
        && string.Equals(permit.Initiation.ContractorId, actor.Id, StringComparison.OrdinalIgnoreCase);

    /// <summary>The Contractor may change the initial draft until it hands it to the Sponsor.</summary>
    public static bool ContractorMayEditDraft(Actor actor, Permit permit) =>
        IsInitiator(actor, permit)
        && permit.Status == PermitStatus.Draft
        && permit.Initiation!.Status is PermitInitiationStatus.Drafting or PermitInitiationStatus.Returned;
}

public static class PermitMonitoringAccess
{
    private static readonly IReadOnlySet<string> AllLocations = new HashSet<string>(
        ["*"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlySet<string> AreaOwnerRoles = new HashSet<string>(
        ["AreaOwnerSeniorOfficer", "AreaOwnerManager"],
        StringComparer.OrdinalIgnoreCase);

    public static bool CanMonitorAllLocations(Actor actor) =>
        actor.LocationScopes.Contains("ORF") && actor.Roles.Overlaps(AreaOwnerRoles);

    public static IReadOnlySet<string> ReadLocationScopes(Actor actor) =>
        CanMonitorAllLocations(actor) ? AllLocations : actor.LocationScopes;

    public static bool CanReadLocation(Actor actor, string locationId) =>
        CanMonitorAllLocations(actor)
        || actor.LocationScopes.Contains("*")
        || actor.LocationScopes.Contains(locationId);
}
