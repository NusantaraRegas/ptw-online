namespace Ptw.Domain;

/// <summary>
/// One document that must have uploaded evidence before a PTW can be submitted.
/// JSA and Prosedur Pekerjaan also exist in the controlled Bagian 4 catalog. Making a
/// document mandatory here gates submission but does not change its Bagian 4 selection.
/// </summary>
public sealed record PermitMandatoryDocumentOption(
    string Code,
    string Label,
    bool RequiresMetadata = false);

public static class PermitMandatoryDocumentCatalog
{
    public const string IdentityCode = "ID";
    public const string BpjsTkCode = "BPJS_TK";
    public const string FtwCode = "FTW";
    public const string ESimiCode = "ESIMI";

    private static readonly PermitMandatoryDocumentOption[] Options =
    [
        new(PermitSupportingDocumentCatalog.JsaCode, "Job Safety Analisis (JSA)", RequiresMetadata: true),
        new(PermitSupportingDocumentCatalog.WorkProcedureCode, "Prosedur Pekerjaan"),
        new(IdentityCode, "ID"),
        new(BpjsTkCode, "BPJS TK"),
        new(FtwCode, "FTW"),
        new(ESimiCode, "E-SIMI")
    ];

    public static IReadOnlyList<PermitMandatoryDocumentOption> Resolve() => Options;

    public static PermitMandatoryDocumentOption? Find(string? codeOrLabel) =>
        Options.FirstOrDefault(option =>
            string.Equals(option.Code, codeOrLabel?.Trim(), StringComparison.OrdinalIgnoreCase)
            || string.Equals(option.Label, codeOrLabel?.Trim(), StringComparison.OrdinalIgnoreCase));
}
