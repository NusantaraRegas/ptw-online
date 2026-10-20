using Ptw.Domain;

namespace Ptw.Application;

/// <summary>Ownership rules shared by the permit and attachment services.</summary>
public static class PermitAccess
{
    /// <summary>
    /// Role that authors PTWs. Any account may hold it alongside other roles; Contractor accounts
    /// hold only this role (UAT-2026-10-12 no. 14, clarified 20 Oct 2026).
    /// </summary>
    public const string RequesterRole = "Requester";

    private static readonly string[] WiderRoles =
        ["Sponsor", "Administrator", "HSEValidator", "AreaOwnerSeniorOfficer", "AreaOwnerManager", "Auditor"];

    public static bool IsRequester(Actor actor) => actor.Roles.Contains(RequesterRole);

    /// <summary>A Requester account with no other role; it only ever sees the PTWs it initiated.</summary>
    public static bool IsRequesterOnly(Actor actor) =>
        IsRequester(actor) && !actor.Roles.Overlaps(WiderRoles);

    public static bool IsInitiator(Actor actor, Permit permit) =>
        permit.Initiation is not null
        && string.Equals(permit.Initiation.RequesterId, actor.Id, StringComparison.OrdinalIgnoreCase);

    /// <summary>The initiating Requester may change the draft until it hands it to the Sponsor.</summary>
    public static bool RequesterMayEditDraft(Actor actor, Permit permit) =>
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
