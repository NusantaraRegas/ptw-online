using Ptw.Domain;

namespace Ptw.Infrastructure.Printing;

/// <summary>
/// Content of one controlled Nusantara Regas PTW form. Geometry is shared by all three forms and lives
/// in <see cref="PtwFormRenderer"/>; only the content below differs per permit class.
/// </summary>
/// <remarks>
/// Transcribed from the controlled originals FM-001/002/003-B-002-NR-B220. Item text, order and column
/// counts are reproduced verbatim, including the duplicated "Sand Blasting" entry on the HOT form, which
/// is pending confirmation under OPN-003. Do not tidy this content without an approved decision record.
/// </remarks>
internal sealed record PrintTemplateDescriptor(
    string FormCode,
    PermitClass PermitClass,
    string BannerLabel,
    string BannerSubLabel,
    string AccentColorHex,
    IReadOnlyList<string> SubTypes,
    int WorkTypeColumns,
    IReadOnlyList<PermitWorkTypeOption> WorkTypes,
    IReadOnlyList<string> SupportingDocumentsPrimary,
    IReadOnlyList<string> SupportingDocumentsSecondary,
    IReadOnlyList<string> SafetyEquipmentPrimary,
    IReadOnlyList<string> SafetyEquipmentSecondary);

internal static class PrintTemplateCatalog
{
    /// <summary>Bagian 4 left column, identical on all three controlled forms.</summary>
    private static readonly string[] SupportingDocumentsPrimary =
    [
        "Job Safety Analisis (JSA)",
        "Check List Inspeksi Alat Berat",
        "Isolation / De-isolation",
        "Prosedur Pekerjaan",
        "P & ID, Plot Plan / Lay Out",
        "Dokumen Perubahan (MOC)",
        "Clearance Penggalian dari Electrical & Civil Eng.",
        "Emergency Response Plan",
        "Sertifikat Training Sea Survival",
        "Ijin Penon - aktifan Sistem / Alat pengaman",
        "Sertifikat Peralatan"
    ];

    /// <summary>Bagian 4 right column, identical on all three controlled forms.</summary>
    private static readonly string[] SupportingDocumentsSecondary =
    [
        "Sertifikat Pekerjaan",
        "Lifting Plan",
        "Penutupan jalan",
        "MSDS"
    ];

    /// <summary>Bagian 5 right column, identical on all three controlled forms.</summary>
    private static readonly string[] SafetyEquipmentSecondary =
    [
        "Non-spark tool",
        "Life jacket / work vest",
        "Rambu K3",
        "Face Shield",
        "Water curtain",
        "Pressurised Habitat",
        "Fire Blanket",
        "Radio Komunikasi",
        "Bund wall / drip pan",
        "Barikade",
        "LOTO"
    ];

    /// <summary>Bagian 5 left column for HOT and COLD.</summary>
    private static readonly string[] SafetyEquipmentPrimary =
    [
        "Respirator",
        "Scaffolding",
        "Body harness",
        "Life line",
        "Papan nama CSE (Tally Board)",
        "Lampu penerangan yg memadai",
        "APAR",
        "Breathing Apparatus",
        "Gas Monitor Portable",
        "Emergency Response team",
        "Whipcheck sambungan hose"
    ];

    /// <summary>CSE replaces the whipcheck entry with a watchman.</summary>
    private static readonly string[] SafetyEquipmentPrimaryCse =
    [
        "Respirator",
        "Scaffolding",
        "Body harness",
        "Life line",
        "Papan nama CSE (Tally Board)",
        "Lampu penerangan yg memadai",
        "APAR",
        "Breathing Apparatus",
        "Gas Monitor Portable",
        "Emergency Response team",
        "Watch man"
    ];

    /// <summary>The controlled CSE form adds Ventilator beneath LOTO in the right column.</summary>
    private static readonly string[] SafetyEquipmentSecondaryCse =
    [
        .. SafetyEquipmentSecondary,
        "Ventilator"
    ];

    private static readonly PrintTemplateDescriptor HotWork = new(
        "FM-001-B-002-NR-B220",
        PermitClass.HotWork,
        "HOT",
        "",
        "#C00000",
        ["Api Terbuka", "Percikan Api"],
        5,
        PermitWorkTypeCatalog.Resolve(PermitClass.HotWork),
        SupportingDocumentsPrimary,
        SupportingDocumentsSecondary,
        SafetyEquipmentPrimary,
        SafetyEquipmentSecondary);

    private static readonly PrintTemplateDescriptor ColdWork = new(
        "FM-002-B-002-NR-B220",
        PermitClass.ColdWork,
        "COLD",
        "",
        "#1F4E79",
        ["Low Risk", "High Risk"],
        6,
        PermitWorkTypeCatalog.Resolve(PermitClass.ColdWork),
        SupportingDocumentsPrimary,
        SupportingDocumentsSecondary,
        SafetyEquipmentPrimary,
        SafetyEquipmentSecondary);

    private static readonly PrintTemplateDescriptor ConfinedSpaceEntry = new(
        "FM-003-B-002-NR-B220",
        PermitClass.ConfinedSpaceEntry,
        "CSE",
        "CONFINED SPACE ENTRY",
        "#2E7D32",
        [],
        6,
        PermitWorkTypeCatalog.Resolve(PermitClass.ConfinedSpaceEntry),
        SupportingDocumentsPrimary,
        SupportingDocumentsSecondary,
        SafetyEquipmentPrimaryCse,
        SafetyEquipmentSecondaryCse);

    /// <summary>Bagian 7 isolation and precaution options, identical on all three controlled forms.</summary>
    internal static readonly string[] IsolationOptions =
    [
        "Isolasi   (  ) Closed / Lock Valves   (  ) Blind   (  ) Disconnect",
        "Depressurized",
        "Drained",
        "Ventilated",
        "Bilas   (  ) N2 Purge   (  ) Water",
        "Lainnya ( Jelaskan )"
    ];

    /// <summary>
    /// Bagian 7 signature rows. The controlled form names two ORF positions while v1.6 models a single
    /// Manager approval, so only the row matching the approver position is populated and the other is
    /// left blank for wet signature until OPN-002 resolves the discrepancy.
    /// </summary>
    internal static readonly string[] OperationsAuthorityPositions =
    [
        "Senior Officer Distribusi Gas dan Manajemen ORF",
        "Kepala Departemen Distribusi Gas dan Manajemen ORF"
    ];

    internal const string DistributionFooter =
        "Copy Original (1) ; Pelaksana Pekerjaan (2) ; Departemen Operasi (3) ; QHSSE (4) Sponsor Pekerjaan";

    internal static PrintTemplateDescriptor Resolve(PermitClass permitClass) => permitClass switch
    {
        PermitClass.HotWork => HotWork,
        PermitClass.ColdWork => ColdWork,
        PermitClass.ConfinedSpaceEntry => ConfinedSpaceEntry,
        _ => throw new ArgumentOutOfRangeException(
            nameof(permitClass),
            permitClass,
            "Tidak ada template cetak terkontrol untuk kelas izin ini.")
    };
}
