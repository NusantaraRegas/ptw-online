namespace Ptw.Domain;

/// <summary>
/// One controlled checkbox from Bagian 4 of FM-001/002/003-B-002-NR-B220.
/// Template coordinates remain stable while requirement policy may evolve independently.
/// </summary>
public sealed record PermitSupportingDocumentOption(
    string Code,
    string Label,
    int TemplateColumn,
    int TemplateIndex,
    bool Required,
    bool RequiresMetadata = false);

public static class PermitSupportingDocumentCatalog
{
    public const string JsaCode = "JSA";
    public const string WorkProcedureCode = "WORK_PROCEDURE";

    private static readonly PermitSupportingDocumentOption[] Options =
    [
        new(JsaCode, "Job Safety Analisis (JSA)", 0, 0, Required: true, RequiresMetadata: true),
        new("HEAVY_EQUIPMENT_INSPECTION", "Check List Inspeksi Alat Berat", 0, 1, Required: false),
        new("ISOLATION_DEISOLATION", "Isolation / De-isolation", 0, 2, Required: false),
        new(WorkProcedureCode, "Prosedur Pekerjaan", 0, 3, Required: true),
        new("PID_PLOT_PLAN", "P & ID, Plot Plan / Lay Out", 0, 4, Required: false),
        new("MOC", "Dokumen Perubahan (MOC)", 0, 5, Required: false),
        new("EXCAVATION_CLEARANCE", "Clearance Penggalian dari Electrical & Civil Eng.", 0, 6, Required: false),
        new("EMERGENCY_RESPONSE_PLAN", "Emergency Response Plan", 0, 7, Required: false),
        new("SEA_SURVIVAL_CERTIFICATE", "Sertifikat Training Sea Survival", 0, 8, Required: false),
        new("SAFETY_SYSTEM_DEACTIVATION_PERMIT", "Ijin Penon - aktifan Sistem / Alat pengaman", 0, 9, Required: false),
        new("EQUIPMENT_CERTIFICATE", "Sertifikat Peralatan", 0, 10, Required: false),
        new("WORK_CERTIFICATE", "Sertifikat Pekerjaan", 1, 0, Required: false),
        new("LIFTING_PLAN", "Lifting Plan", 1, 1, Required: false),
        new("ROAD_CLOSURE", "Penutupan jalan", 1, 2, Required: false),
        new("MSDS", "MSDS", 1, 3, Required: false)
    ];

    public static IReadOnlyList<PermitSupportingDocumentOption> Resolve() => Options;

    public static PermitSupportingDocumentOption Resolve(string codeOrLabel) =>
        Options.FirstOrDefault(option =>
            string.Equals(option.Code, codeOrLabel?.Trim(), StringComparison.OrdinalIgnoreCase)
            || string.Equals(option.Label, codeOrLabel?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new DomainRuleViolationException(
            "permit.supporting_document_invalid",
            $"Dokumen pendukung '{codeOrLabel}' tidak tersedia pada Bagian 4.");

    public static string[] NormalizeAndValidate(
        IReadOnlyList<string>? documentCodes,
        bool allowMissingRequired = false)
    {
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var submitted in documentCodes ?? [])
        {
            if (string.IsNullOrWhiteSpace(submitted))
            {
                continue;
            }

            selected.Add(Resolve(submitted).Code);
        }

        if (!allowMissingRequired && !selected.Contains(JsaCode))
        {
            throw new DomainRuleViolationException(
                "permit.supporting_document.jsa_required",
                "Job Safety Analisis (JSA) wajib dipilih pada Bagian 4.");
        }

        if (!allowMissingRequired && !selected.Contains(WorkProcedureCode))
        {
            throw new DomainRuleViolationException(
                "permit.supporting_document.work_procedure_required",
                "Prosedur Pekerjaan wajib dipilih pada Bagian 4.");
        }

        return Options.Where(option => selected.Contains(option.Code)).Select(option => option.Code).ToArray();
    }
}
