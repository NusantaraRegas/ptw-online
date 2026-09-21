namespace Ptw.Domain;

public sealed class UserAccount
{
    private UserAccount(
        string subjectId,
        string userName,
        string displayName,
        string? position,
        string? department,
        bool isActive,
        int version,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        SubjectId = Required(subjectId, "Subject ID", 200);
        UserName = Required(userName, "Username", 100).ToLowerInvariant();
        DisplayName = Required(displayName, "Nama pengguna", 200);
        Position = Optional(position, 200);
        Department = Optional(department, 200);
        IsActive = isActive;
        Version = version;
        CreatedAt = createdAt.ToUniversalTime();
        UpdatedAt = updatedAt.ToUniversalTime();
    }

    public string SubjectId { get; }
    public string UserName { get; private set; }
    public string DisplayName { get; private set; }
    public string? Position { get; private set; }
    public string? Department { get; private set; }
    public bool IsActive { get; private set; }
    public int Version { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static UserAccount Create(
        string subjectId,
        string userName,
        string displayName,
        string? position,
        string? department,
        DateTimeOffset now) =>
        new(subjectId, userName, displayName, position, department, true, 1, now, now);

    public static UserAccount Rehydrate(
        string subjectId,
        string userName,
        string displayName,
        string? position,
        string? department,
        bool isActive,
        int version,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt) =>
        new(subjectId, userName, displayName, position, department, isActive, version, createdAt, updatedAt);

    public void Update(string displayName, string? position, string? department, DateTimeOffset now)
    {
        DisplayName = Required(displayName, "Nama pengguna", 200);
        Position = Optional(position, 200);
        Department = Optional(department, 200);
        Touch(now);
    }

    public void SetActive(bool isActive, DateTimeOffset now)
    {
        if (IsActive == isActive)
        {
            return;
        }

        IsActive = isActive;
        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        Version++;
        UpdatedAt = now.ToUniversalTime();
    }

    private static string Required(string? value, string label, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new DomainRuleViolationException("user.required", $"{label} wajib diisi.");
        }

        if (normalized.Length > maxLength)
        {
            throw new DomainRuleViolationException("user.too_long", $"{label} maksimum {maxLength} karakter.");
        }

        return normalized;
    }

    private static string? Optional(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        if (normalized.Length > maxLength)
        {
            throw new DomainRuleViolationException("user.too_long", $"Nilai maksimum {maxLength} karakter.");
        }

        return normalized;
    }
}
