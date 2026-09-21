namespace Ptw.Domain;

/// <summary>
/// One controlled operational-condition item from Bagian 7 of FM-001/002/003-B-002-NR-B220.
/// Parent codes represent the six printed checkboxes; child codes represent choices printed inside
/// the Isolasi and Bilas rows.
/// </summary>
public sealed record PermitOperationalConditionOption(
    string Code,
    string Label,
    int TemplateIndex,
    string? ParentCode = null,
    bool RequiresDetail = false);

public static class PermitOperationalConditionCatalog
{
    public const string Isolation = "OPS_ISOLATION";
    public const string IsolationClosedLockValves = "OPS_ISOLATION_CLOSED_LOCK_VALVES";
    public const string IsolationBlind = "OPS_ISOLATION_BLIND";
    public const string IsolationDisconnect = "OPS_ISOLATION_DISCONNECT";
    public const string Depressurized = "OPS_DEPRESSURIZED";
    public const string Drained = "OPS_DRAINED";
    public const string Ventilated = "OPS_VENTILATED";
    public const string Flushing = "OPS_FLUSHING";
    public const string FlushingN2Purge = "OPS_FLUSHING_N2_PURGE";
    public const string FlushingWater = "OPS_FLUSHING_WATER";
    public const string Other = "OPS_OTHER";

    private static readonly PermitOperationalConditionOption[] Options =
    [
        new(Isolation, "Isolasi", 0),
        new(IsolationClosedLockValves, "Closed / Lock Valves", 0, Isolation),
        new(IsolationBlind, "Blind", 1, Isolation),
        new(IsolationDisconnect, "Disconnect", 2, Isolation),
        new(Depressurized, "Depressurized", 1),
        new(Drained, "Drained", 2),
        new(Ventilated, "Ventilated", 3),
        new(Flushing, "Bilas", 4),
        new(FlushingN2Purge, "N2 Purge", 3, Flushing),
        new(FlushingWater, "Water", 4, Flushing),
        new(Other, "Lainnya (Jelaskan)", 5, RequiresDetail: true)
    ];

    public static IReadOnlyList<PermitOperationalConditionOption> Resolve() => Options;

    public static string[] NormalizeAndValidate(
        IReadOnlyList<string>? conditionCodes,
        string? otherConditionDetail)
    {
        var submitted = conditionCodes?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToArray()
            ?? [];
        var known = Options.ToDictionary(option => option.Code, StringComparer.OrdinalIgnoreCase);
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in submitted)
        {
            if (!known.TryGetValue(code, out var option))
            {
                throw new DomainRuleViolationException(
                    "permit.area_operations.condition_invalid",
                    $"Kondisi operasi Bagian 7 '{code}' tidak dikenal.");
            }

            selected.Add(option.Code);
        }

        EnsureChildSelection(selected, Isolation,
            [IsolationClosedLockValves, IsolationBlind, IsolationDisconnect]);
        EnsureChildSelection(selected, Flushing, [FlushingN2Purge, FlushingWater]);

        var detail = otherConditionDetail?.Trim();
        if (selected.Contains(Other) && string.IsNullOrWhiteSpace(detail))
        {
            throw new DomainRuleViolationException(
                "permit.area_operations.other_detail_required",
                "Penjelasan kondisi lainnya wajib diisi ketika Lainnya dipilih.");
        }

        if (!selected.Contains(Other) && !string.IsNullOrWhiteSpace(detail))
        {
            throw new DomainRuleViolationException(
                "permit.area_operations.other_detail_without_selection",
                "Penjelasan kondisi lainnya hanya boleh diisi ketika Lainnya dipilih.");
        }

        if (detail?.Length > 200)
        {
            throw new DomainRuleViolationException(
                "permit.area_operations.other_detail_too_long",
                "Penjelasan kondisi lainnya maksimum 200 karakter.");
        }

        return Options.Where(option => selected.Contains(option.Code)).Select(option => option.Code).ToArray();
    }

    private static void EnsureChildSelection(
        HashSet<string> selected,
        string parent,
        IReadOnlyList<string> children)
    {
        var parentSelected = selected.Contains(parent);
        var selectedChildren = children.Count(selected.Contains);
        if (parentSelected && selectedChildren == 0)
        {
            throw new DomainRuleViolationException(
                "permit.area_operations.subcondition_required",
                $"Pilih minimal satu rincian untuk {Options.Single(option => option.Code == parent).Label}.");
        }

        if (!parentSelected && selectedChildren > 0)
        {
            throw new DomainRuleViolationException(
                "permit.area_operations.parent_condition_required",
                $"Pilihan {Options.Single(option => option.Code == parent).Label} wajib dipilih sebelum rinciannya.");
        }
    }
}
