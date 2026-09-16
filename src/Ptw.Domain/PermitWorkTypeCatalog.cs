namespace Ptw.Domain;

/// <summary>
/// One selectable item from Bagian 1 of the controlled PTW forms. <see cref="TemplateIndex"/> preserves
/// the exact checkbox position, including intentional duplicate labels in the source document.
/// </summary>
public sealed record PermitWorkTypeOption(
    string Code,
    string Label,
    int TemplateIndex,
    bool RequiresDetail = false);

/// <summary>
/// Server-authoritative work-type catalogue transcribed from FM-001/002/003-B-002-NR-B220. Codes are
/// stable domain identifiers; labels remain verbatim so the UI and printed form use the controlled text.
/// </summary>
public static class PermitWorkTypeCatalog
{
    private static readonly PermitWorkTypeOption[] HotWork =
    [
        new("HOT_MACHINE_NON_EX", "Memakai mesin non-EX", 0),
        new("HOT_TEMPERATURE_ABOVE_200_C", "Pekerjaan dgn suhu >200 C", 1),
        new("HOT_GRINDING", "Menggerinda", 2),
        new("HOT_LIVE_JUNCTION_BOX", "Membuka junction box hidup", 3),
        new("HOT_SAND_BLASTING_PRIMARY", "Sand Blasting", 4),
        new("HOT_FLAME_CUTTING", "Pemotongan Memakai Api", 5),
        new("HOT_VEHICLE_ENTRY", "Kendaraan masuk", 6),
        new("HOT_WELDING", "Mengelas", 7),
        new("HOT_EXPLOSIVE_MATERIAL", "Pekerjaan memakai bahan peledak", 8),
        new("HOT_OTHER", "Lain - Lain :", 9, RequiresDetail: true),
        new("HOT_NON_EX_EQUIPMENT", "Bekerja dengan peralatan non-Ex", 10),
        new("HOT_POWER_TOOL", "Pemakaian Power Tool", 11),
        new("HOT_GOUGING", "Gauging", 12),
        new("HOT_PYROPHORIC", "Pekerjaan berpotensi kerak pyrophoric", 13),
        new("HOT_PWHT_PREHEATING", "PWHT (Pre-heating)", 15),
        new("HOT_PHOTOGRAPHY_NON_EX", "Photography Non-Ex", 16),
        new("HOT_SAND_BLASTING_SECONDARY", "Sand Blasting", 17),
        new("HOT_RADIOGRAPHY", "Radiography", 18)
    ];

    private static readonly PermitWorkTypeOption[] ColdWork =
    [
        new("COLD_CRANE_LIFTING", "Pengangkatan memakai Crane", 0),
        new("COLD_SCAFFOLDING", "Scaffolding / Perancah", 1),
        new("COLD_LEAK_TEST", "Leak Test", 2),
        new("COLD_VIBRATION_TEST", "Tes Vibrasi", 3),
        new("COLD_SPADING_BLIND", "Spading, blind", 4),
        new("COLD_INDOOR_ELECTRICAL", "Listrik didalam gedung", 5),
        new("COLD_MANUAL_LIFTING", "Pengangkatan Manual", 6),
        new("COLD_WORK_AT_HEIGHT", "Bekerja di Ketinggian", 7),
        new("COLD_PAINTING", "Pengecatan", 8),
        new("COLD_OIL_CHANGE_GREASING", "Ganti oli, greasing", 9),
        new("COLD_HOUSEKEEPING", "Housekeeping", 10),
        new("COLD_PURGING_FLUSHING", "Purging, flushing", 11),
        new("COLD_HEAVY_EQUIPMENT_EXCAVATION", "Penggalian memakai Alat Berat", 12),
        new("COLD_INSULATION", "Bongkar / pasang insulation", 13),
        new("COLD_DIVING", "Diving", 14),
        new("COLD_GRATING", "Bongkar/pasang grating", 15),
        new("COLD_MANUAL_WORK", "Manual Pekerjaan", 16),
        new("COLD_OTHER", "Lain - Lain :", 17, RequiresDetail: true),
        new("COLD_MANUAL_EXCAVATION", "Penggalian Manual", 18),
        new("COLD_INSTRUMENTATION", "Instrumentasi", 19),
        new("COLD_LAB_SAMPLING", "Lab. Sampling", 20),
        new("COLD_MECHANICAL", "Mekanikal", 21),
        new("COLD_BLOWING", "Blowing", 22)
    ];

    private static readonly PermitWorkTypeOption[] ConfinedSpaceEntry =
    [
        new("CSE_INSTRUMENTATION", "Instrumentasi", 0),
        new("CSE_SAND_BLASTING", "Sand blasting", 1),
        new("CSE_PAINTING", "Pengecatan", 2),
        new("CSE_NON_EX_EQUIPMENT", "Pemakaian Peralatan Non-Ex", 3),
        new("CSE_TEMPERATURE_ABOVE_200_C", "Pekerjaan dengan suhu >200 C", 4),
        new("CSE_INSPECTION", "Inspeksi", 5),
        new("CSE_ELECTRICAL_WORK", "Pekerjaan Listrik", 6),
        new("CSE_PATCHING", "Patching", 7),
        new("CSE_VISUAL_INSPECTION", "Inspeksi Visual", 8),
        new("CSE_PWHT_PREHEATING", "PWHT / Pre-heating", 9),
        new("CSE_POWER_TOOL", "Pemakaian Power Tool", 10),
        new("CSE_OTHER", "Lainnya", 11, RequiresDetail: true),
        new("CSE_WELDING", "Pengelasan", 12),
        new("CSE_FLAME_CUTTING", "Pemotongan memakai api", 13),
        new("CSE_CLEANING", "Pembersihan", 14),
        new("CSE_PYROPHORIC", "Pekerjaan berpotensi pyrophoric", 15),
        new("CSE_PHOTOGRAPHY_NON_EX", "Potografi Non-Ex", 16),
        new("CSE_GRINDING", "Penggerindaan", 18),
        new("CSE_RADIOGRAPHY", "Radiography", 19),
        new("CSE_MANUAL_WORK", "Pekerjaan Manual", 20),
        new("CSE_GOUGING", "Gauging", 21),
        new("CSE_LIVE_JUNCTION_BOX", "Membuka junction box hidup", 22)
    ];

    public static IReadOnlyList<PermitWorkTypeOption> Resolve(PermitClass permitClass) => permitClass switch
    {
        PermitClass.HotWork => HotWork,
        PermitClass.ColdWork => ColdWork,
        PermitClass.ConfinedSpaceEntry => ConfinedSpaceEntry,
        _ => throw new ArgumentOutOfRangeException(nameof(permitClass), permitClass, "Kelas PTW tidak dikenali.")
    };

    public static string[] NormalizeAndValidate(
        PermitClass permitClass,
        IReadOnlyList<string>? workTypeCodes,
        string? legacyWorkTypeCode = null)
    {
        var submitted = workTypeCodes?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToArray()
            ?? [];
        if (submitted.Length == 0 && !string.IsNullOrWhiteSpace(legacyWorkTypeCode))
        {
            submitted = [legacyWorkTypeCode.Trim()];
        }

        if (submitted.Length == 0)
        {
            throw new DomainRuleViolationException(
                "permit.work_type_required",
                "Pilih minimal satu jenis pekerjaan dari Bagian 1 sesuai kelas izin.");
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
                    "permit.work_type_invalid",
                    $"Jenis pekerjaan '{submittedValue}' tidak tersedia untuk kelas izin {permitClass}.");
            }

            selected.Add(match.Code);
        }

        return options.Where(option => selected.Contains(option.Code)).Select(option => option.Code).ToArray();
    }

    public static bool RequiresDetail(PermitClass permitClass, IReadOnlyCollection<string> workTypeCodes)
    {
        var selected = new HashSet<string>(workTypeCodes, StringComparer.OrdinalIgnoreCase);
        return Resolve(permitClass).Any(option => option.RequiresDetail && selected.Contains(option.Code));
    }
}
