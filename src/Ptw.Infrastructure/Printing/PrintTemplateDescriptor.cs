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
    private static readonly string[] SupportingDocumentsPrimary = PermitSupportingDocumentCatalog.Resolve()
        .Where(option => option.TemplateColumn == 0)
        .OrderBy(option => option.TemplateIndex)
        .Select(option => option.Label)
        .ToArray();

    /// <summary>Bagian 4 right column, identical on all three controlled forms.</summary>
    private static readonly string[] SupportingDocumentsSecondary = PermitSupportingDocumentCatalog.Resolve()
        .Where(option => option.TemplateColumn == 1)
        .OrderBy(option => option.TemplateIndex)
        .Select(option => option.Label)
        .ToArray();

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
        SafetyEquipment(PermitClass.HotWork, 0),
        SafetyEquipment(PermitClass.HotWork, 1));

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
        SafetyEquipment(PermitClass.ColdWork, 0),
        SafetyEquipment(PermitClass.ColdWork, 1));

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
        SafetyEquipment(PermitClass.ConfinedSpaceEntry, 0),
        SafetyEquipment(PermitClass.ConfinedSpaceEntry, 1));

    private static string[] SafetyEquipment(PermitClass permitClass, int templateColumn) =>
        PermitSafetyEquipmentCatalog.Resolve(permitClass)
            .Where(option => option.TemplateColumn == templateColumn)
            .OrderBy(option => option.TemplateIndex)
            .Select(option => option.Label)
            .ToArray();

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
    /// Bagian 7 signature rows in their controlled order: Senior Officer operational review first,
    /// followed by the Manager Pemilik Wilayah approval that issues the PTW.
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
