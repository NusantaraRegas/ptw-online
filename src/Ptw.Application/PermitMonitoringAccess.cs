namespace Ptw.Application;

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
