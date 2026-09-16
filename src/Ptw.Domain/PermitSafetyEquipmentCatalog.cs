namespace Ptw.Domain;

/// <summary>
/// One controlled APD/safety-equipment item from Bagian 5 of the official PTW form.
/// <see cref="TemplateColumn"/> and <see cref="TemplateIndex"/> preserve its printed checkbox position.
/// </summary>
public sealed record PermitSafetyEquipmentOption(
    string Code,
    string Label,
    int TemplateColumn,
    int TemplateIndex);

/// <summary>
/// Server-authoritative Bagian 5 catalogue transcribed from FM-001/002/003-B-002-NR-B220.
/// The HSE validator selects these codes as part of validation; Sponsors cannot provide free text.
/// </summary>
public static class PermitSafetyEquipmentCatalog
{
    private static readonly PermitSafetyEquipmentOption[] Common =
    [
        new("SAFETY_RESPIRATOR", "Respirator", 0, 0),
        new("SAFETY_SCAFFOLDING", "Scaffolding", 0, 1),
        new("SAFETY_BODY_HARNESS", "Body harness", 0, 2),
        new("SAFETY_LIFE_LINE", "Life line", 0, 3),
        new("SAFETY_CSE_TALLY_BOARD", "Papan nama CSE (Tally Board)", 0, 4),
        new("SAFETY_ADEQUATE_LIGHTING", "Lampu penerangan yg memadai", 0, 5),
        new("SAFETY_FIRE_EXTINGUISHER", "APAR", 0, 6),
        new("SAFETY_BREATHING_APPARATUS", "Breathing Apparatus", 0, 7),
        new("SAFETY_PORTABLE_GAS_MONITOR", "Gas Monitor Portable", 0, 8),
        new("SAFETY_EMERGENCY_RESPONSE_TEAM", "Emergency Response team", 0, 9),
        new("SAFETY_NON_SPARK_TOOL", "Non-spark tool", 1, 0),
        new("SAFETY_LIFE_JACKET_WORK_VEST", "Life jacket / work vest", 1, 1),
        new("SAFETY_K3_SIGN", "Rambu K3", 1, 2),
        new("SAFETY_FACE_SHIELD", "Face Shield", 1, 3),
        new("SAFETY_WATER_CURTAIN", "Water curtain", 1, 4),
        new("SAFETY_PRESSURISED_HABITAT", "Pressurised Habitat", 1, 5),
        new("SAFETY_FIRE_BLANKET", "Fire Blanket", 1, 6),
        new("SAFETY_RADIO_COMMUNICATION", "Radio Komunikasi", 1, 7),
        new("SAFETY_BUND_WALL_DRIP_PAN", "Bund wall / drip pan", 1, 8),
        new("SAFETY_BARRICADE", "Barikade", 1, 9),
        new("SAFETY_LOTO", "LOTO", 1, 10)
    ];

    private static readonly PermitSafetyEquipmentOption[] HotAndCold =
    [
        .. Common,
        new("SAFETY_WHIPCHECK", "Whipcheck sambungan hose", 0, 10)
    ];

    private static readonly PermitSafetyEquipmentOption[] ConfinedSpaceEntry =
    [
        .. Common,
        new("SAFETY_WATCHMAN", "Watch man", 0, 10),
        new("SAFETY_VENTILATOR", "Ventilator", 1, 11)
    ];

    public static IReadOnlyList<PermitSafetyEquipmentOption> Resolve(PermitClass permitClass) =>
        permitClass switch
        {
            PermitClass.HotWork => HotAndCold,
            PermitClass.ColdWork => HotAndCold,
            PermitClass.ConfinedSpaceEntry => ConfinedSpaceEntry,
            _ => throw new ArgumentOutOfRangeException(
                nameof(permitClass),
                permitClass,
                "Kelas PTW tidak dikenali.")
        };

    public static string[] NormalizeAndValidate(
        PermitClass permitClass,
        IReadOnlyList<string>? safetyEquipmentCodes)
    {
        var submitted = safetyEquipmentCodes?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToArray()
            ?? [];
        if (submitted.Length == 0)
        {
            throw new DomainRuleViolationException(
                "permit.safety_equipment_required",
                "PIC HSE wajib memilih minimal satu APD/perlengkapan safety dari Bagian 5.");
        }

        var options = Resolve(permitClass);
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var submittedValue in submitted)
        {
            var match = options.FirstOrDefault(option =>
                string.Equals(option.Code, submittedValue, StringComparison.OrdinalIgnoreCase)
                || string.Equals(option.Label, submittedValue, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                throw new DomainRuleViolationException(
                    "permit.safety_equipment_invalid",
                    $"APD/perlengkapan safety '{submittedValue}' tidak tersedia pada Bagian 5 untuk kelas izin {permitClass}.");
            }

            selected.Add(match.Code);
        }

        return options
            .OrderBy(option => option.TemplateColumn)
            .ThenBy(option => option.TemplateIndex)
            .Where(option => selected.Contains(option.Code))
            .Select(option => option.Code)
            .ToArray();
    }
}
