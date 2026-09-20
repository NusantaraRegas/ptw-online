namespace Ptw.Domain;

/// <summary>
/// One controlled classification checkbox from the header of FM-001/002/003-B-002-NR-B220.
/// <see cref="TemplateIndex"/> preserves the vertical checkbox position in the official form.
/// </summary>
public sealed record PermitHeaderClassificationOption(
    string Code,
    string Label,
    int TemplateIndex);

public enum PermitHeaderClassificationSelectionMode
{
    None,
    Exclusive,
    Multiple
}

/// <summary>
/// Server-authoritative transcription of the classification boxes printed beside HOT/COLD/CSE.
/// HOT is a checklist, COLD is one mutually exclusive risk classification, and CSE has no box.
/// </summary>
public static class PermitHeaderClassificationCatalog
{
    private static readonly PermitHeaderClassificationOption[] HotWork =
    [
        new("HOT_OPEN_FLAME", "Api Terbuka", 0),
        new("HOT_SPARK", "Percikan Api", 1)
    ];

    private static readonly PermitHeaderClassificationOption[] ColdWork =
    [
        new("COLD_LOW_RISK", "Low Risk", 0),
        new("COLD_HIGH_RISK", "High Risk", 1)
    ];

    public static IReadOnlyList<PermitHeaderClassificationOption> Resolve(PermitClass permitClass) =>
        permitClass switch
        {
            PermitClass.HotWork => HotWork,
            PermitClass.ColdWork => ColdWork,
            PermitClass.ConfinedSpaceEntry => [],
            _ => throw new ArgumentOutOfRangeException(
                nameof(permitClass),
                permitClass,
                "Kelas PTW tidak dikenali.")
        };

    public static PermitHeaderClassificationSelectionMode SelectionMode(PermitClass permitClass) =>
        permitClass switch
        {
            PermitClass.HotWork => PermitHeaderClassificationSelectionMode.Multiple,
            PermitClass.ColdWork => PermitHeaderClassificationSelectionMode.Exclusive,
            PermitClass.ConfinedSpaceEntry => PermitHeaderClassificationSelectionMode.None,
            _ => throw new ArgumentOutOfRangeException(
                nameof(permitClass),
                permitClass,
                "Kelas PTW tidak dikenali.")
        };

    public static string[] NormalizeAndValidate(
        PermitClass permitClass,
        IReadOnlyList<string>? classificationCodes,
        bool allowMissing = false)
    {
        var submitted = classificationCodes?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToArray()
            ?? [];
        var options = Resolve(permitClass);
        var mode = SelectionMode(permitClass);

        if (mode == PermitHeaderClassificationSelectionMode.None)
        {
            if (submitted.Length > 0)
            {
                throw new DomainRuleViolationException(
                    "permit.header_classification_not_applicable",
                    "Formulir CSE tidak memiliki pilihan klasifikasi tambahan pada header.");
            }

            return [];
        }

        if (submitted.Length == 0)
        {
            if (allowMissing)
            {
                return [];
            }

            throw new DomainRuleViolationException(
                "permit.header_classification_required",
                $"Pilih klasifikasi header sesuai formulir {permitClass}.");
        }

        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var submittedValue in submitted)
        {
            var match = options.FirstOrDefault(option =>
                string.Equals(option.Code, submittedValue, StringComparison.OrdinalIgnoreCase)
                || string.Equals(option.Label, submittedValue, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                throw new DomainRuleViolationException(
                    "permit.header_classification_invalid",
                    $"Klasifikasi header '{submittedValue}' tidak tersedia untuk kelas izin {permitClass}.");
            }

            selected.Add(match.Code);
        }

        if (mode == PermitHeaderClassificationSelectionMode.Exclusive && selected.Count != 1)
        {
            throw new DomainRuleViolationException(
                "permit.header_classification_single_required",
                "Pilih tepat satu klasifikasi risiko pada formulir COLD.");
        }

        return options
            .Where(option => selected.Contains(option.Code))
            .Select(option => option.Code)
            .ToArray();
    }
}
