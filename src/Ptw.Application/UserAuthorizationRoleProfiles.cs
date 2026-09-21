namespace Ptw.Application;

public sealed class UserAuthorizationApprovalSettings
{
    public bool AllowAdministratorSelfApproval { get; init; }
}

public sealed class UserAuthorizationRoleProfileSettings
{
    public Dictionary<string, UserAuthorizationRoleProfile> Roles { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string roleCode, out KeyValuePair<string, UserAuthorizationRoleProfile> profile)
    {
        profile = Roles.FirstOrDefault(item =>
            string.Equals(item.Key, roleCode, StringComparison.OrdinalIgnoreCase));
        return !string.IsNullOrWhiteSpace(profile.Key);
    }
}

public sealed class UserAuthorizationRoleProfile
{
    public string Label { get; init; } = string.Empty;
    public bool LocationRequired { get; init; }
    public IReadOnlyList<string> ActionCodes { get; init; } = [];
    public IReadOnlyList<string> RequiredCompetencyCodes { get; init; } = [];
}
