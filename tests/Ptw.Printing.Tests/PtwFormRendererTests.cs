using Ptw.Application;
using Ptw.Domain;
using Ptw.Infrastructure.Printing;
using PdfSharp.Pdf.IO;

namespace Ptw.Printing.Tests;

/// <summary>
/// Document regression for the controlled Nusantara Regas PTW forms. Rendering must stay deterministic so
/// that a render retry reproduces the approved document byte for byte.
/// </summary>
public sealed class PtwFormRendererTests
{
    private static readonly DateTimeOffset IssuedAt = new(2026, 9, 15, 2, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(PermitClass.HotWork, "FM-001-B-002-NR-B220")]
    [InlineData(PermitClass.ColdWork, "FM-002-B-002-NR-B220")]
    [InlineData(PermitClass.ConfinedSpaceEntry, "FM-003-B-002-NR-B220")]
    public void RendersControlledFormForEveryPermitClass(PermitClass permitClass, string expectedFormCode)
    {
        var renderer = new PtwFormRenderer();

        var result = renderer.Render(Request(permitClass));

        Assert.Equal("application/pdf", result.MediaType);
        Assert.Equal(PtwFormRenderer.RendererVersion, result.RendererVersion);
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(result.Content, 0, 5), StringComparison.Ordinal);
        Assert.Equal(expectedFormCode, PrintTemplateCatalog.Resolve(permitClass).FormCode);
        using var pdf = PdfReader.Open(new MemoryStream(result.Content), PdfDocumentOpenMode.Import);
        Assert.Equal(2, pdf.PageCount);
        Assert.True(pdf.Pages[0].Width.Point > pdf.Pages[0].Height.Point);
        Assert.True(pdf.Pages[1].Width.Point < pdf.Pages[1].Height.Point);
        DumpForVisualReview(result.Content, expectedFormCode);
    }

    [Fact]
    public void RenderingTheSameSnapshotTwiceProducesTheSameDocument()
    {
        var renderer = new PtwFormRenderer();

        var first = renderer.Render(Request(PermitClass.HotWork));
        var second = renderer.Render(Request(PermitClass.HotWork));

        Assert.Equal(first.Content.Length, second.Content.Length);
        Assert.Equal(Canonicalise(first.Content), Canonicalise(second.Content));
    }

    [Fact]
    public void Bagian5CheckboxesComeFromHseValidationEvidence()
    {
        var renderer = new PtwFormRenderer();
        var selected = Snapshot(PermitClass.HotWork);
        var withoutSelection = selected with
        {
            HseValidation = selected.HseValidation! with { SafetyEquipmentCodes = [] }
        };

        var selectedDocument = renderer.Render(new(selected, "A1B2C3D4E5F60718293A4B5C6D7E8F90", Watermark: false));
        var emptyDocument = renderer.Render(new(withoutSelection, "A1B2C3D4E5F60718293A4B5C6D7E8F90", Watermark: false));

        Assert.NotEqual(Canonicalise(selectedDocument.Content), Canonicalise(emptyDocument.Content));
    }

    [Fact]
    public void Bagian7ChecklistAndAuthorityRowsComeFromAreaDecisions()
    {
        var renderer = new PtwFormRenderer();
        var populated = Snapshot(PermitClass.HotWork);
        var withoutDecisions = populated with
        {
            AreaOperationsReview = null,
            Approval = null
        };

        var populatedDocument = renderer.Render(new(populated, "A1B2C3D4E5F60718293A4B5C6D7E8F90", Watermark: false));
        var emptyDocument = renderer.Render(new(withoutDecisions, "A1B2C3D4E5F60718293A4B5C6D7E8F90", Watermark: false));

        Assert.NotEqual(Canonicalise(populatedDocument.Content), Canonicalise(emptyDocument.Content));
    }

    [Fact]
    public void DraftPreviewIsWatermarkedAndDiffersFromTheOfficialDocument()
    {
        var renderer = new PtwFormRenderer();

        var official = renderer.Render(Request(PermitClass.HotWork));
        var preview = renderer.Render(Request(PermitClass.HotWork) with { Watermark = true });

        Assert.NotEqual(official.Content, preview.Content);
        DumpForVisualReview(preview.Content, "PREVIEW-WATERMARK");
    }

    [Fact]
    public void EveryControlledClassHasATemplateAndUnknownClassesFailClosed()
    {
        foreach (var permitClass in Enum.GetValues<PermitClass>())
        {
            Assert.NotNull(PrintTemplateCatalog.Resolve(permitClass));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => PrintTemplateCatalog.Resolve((PermitClass)99));
    }

    [Fact]
    public void ControlledTemplatePageMappingAndCseSafetyColumnsMatchTheOfficialForm()
    {
        Assert.Equal(1, TemplateOverlayCatalog.Resolve(PermitClass.HotWork).PageNumber);
        Assert.Equal(2, TemplateOverlayCatalog.Resolve(PermitClass.ColdWork).PageNumber);
        Assert.Equal(3, TemplateOverlayCatalog.Resolve(PermitClass.ConfinedSpaceEntry).PageNumber);

        var cse = PrintTemplateCatalog.Resolve(PermitClass.ConfinedSpaceEntry);
        Assert.Contains("Watch man", cse.SafetyEquipmentPrimary);
        Assert.DoesNotContain("Ventilator", cse.SafetyEquipmentPrimary);
        Assert.Contains("Ventilator", cse.SafetyEquipmentSecondary);
        Assert.Equal(
            cse.SafetyEquipmentPrimary,
            PermitSafetyEquipmentCatalog.Resolve(PermitClass.ConfinedSpaceEntry)
                .Where(option => option.TemplateColumn == 0)
                .OrderBy(option => option.TemplateIndex)
                .Select(option => option.Label));
        Assert.Equal(
            cse.SupportingDocumentsPrimary,
            PermitSupportingDocumentCatalog.Resolve()
                .Where(option => option.TemplateColumn == 0)
                .OrderBy(option => option.TemplateIndex)
                .Select(option => option.Label));
        Assert.Equal(
            cse.SupportingDocumentsSecondary,
            PermitSupportingDocumentCatalog.Resolve()
                .Where(option => option.TemplateColumn == 1)
                .OrderBy(option => option.TemplateIndex)
                .Select(option => option.Label));
        foreach (var permitClass in new[] { PermitClass.HotWork, PermitClass.ColdWork })
        {
            Assert.Equal(
                PrintTemplateCatalog.Resolve(permitClass).SubTypes,
                PermitHeaderClassificationCatalog.Resolve(permitClass)
                    .OrderBy(option => option.TemplateIndex)
                    .Select(option => option.Label));
        }
    }

    [Fact]
    public void ColdWorkRestrokesSegmentedSectionDividerAsOneContinuousRule()
    {
        var rule = Assert.Single(TemplateOverlayCatalog.StructuralRepairRules(PermitClass.ColdWork));

        Assert.Equal(new PdfPoint(282.25, 349.75), rule.Start);
        Assert.Equal(new PdfPoint(282.25, 454.25), rule.End);
        Assert.Empty(TemplateOverlayCatalog.StructuralRepairRules(PermitClass.HotWork));
        Assert.Empty(TemplateOverlayCatalog.StructuralRepairRules(PermitClass.ConfinedSpaceEntry));
    }

    [Fact]
    public void WorkTypeCatalogPreservesDistinctCheckboxesForDuplicateTemplateLabels()
    {
        var sandBlasting = PermitWorkTypeCatalog.Resolve(PermitClass.HotWork)
            .Where(option => option.Label == "Sand Blasting")
            .ToArray();

        Assert.Equal(2, sandBlasting.Length);
        Assert.Equal(2, sandBlasting.Select(option => option.Code).Distinct().Count());
        Assert.Equal([4, 17], sandBlasting.Select(option => option.TemplateIndex));
    }

    [Fact]
    public void OtherWorkTypeDescriptionChangesTheControlledOverlay()
    {
        var renderer = new PtwFormRenderer();
        var snapshot = Snapshot(PermitClass.ColdWork);
        var withOther = snapshot with
        {
            Permit = snapshot.Permit with
            {
                WorkTypeCode = "COLD_OTHER",
                WorkTypeCodes = ["COLD_OTHER"],
                OtherWorkTypeDescription = "Pembersihan strainer sementara"
            }
        };

        var ordinary = renderer.Render(new(snapshot, "A1B2C3D4E5F60718293A4B5C6D7E8F90", Watermark: false));
        var other = renderer.Render(new(withOther, "A1B2C3D4E5F60718293A4B5C6D7E8F90", Watermark: false));

        Assert.NotEqual(Canonicalise(ordinary.Content), Canonicalise(other.Content));
        DumpForVisualReview(other.Content, "COLD-OTHER-WORK-TYPE");
    }

    [Fact]
    public void PlanningReferenceFieldsChangeTheControlledOverlay()
    {
        var renderer = new PtwFormRenderer();
        var populated = Snapshot(PermitClass.HotWork);
        var empty = populated with
        {
            Permit = populated.Permit with
            {
                EquipmentName = null,
                WorkOrderNumber = null,
                AdditionalHazardReference = null
            }
        };

        var populatedDocument = renderer.Render(new(populated, "A1B2C3D4E5F60718293A4B5C6D7E8F90", Watermark: false));
        var emptyDocument = renderer.Render(new(empty, "A1B2C3D4E5F60718293A4B5C6D7E8F90", Watermark: false));

        Assert.NotEqual(Canonicalise(populatedDocument.Content), Canonicalise(emptyDocument.Content));
    }

    [Theory]
    [InlineData(PermitClass.HotWork)]
    [InlineData(PermitClass.ColdWork)]
    public void HeaderClassificationChangesTheControlledOverlay(PermitClass permitClass)
    {
        var renderer = new PtwFormRenderer();
        var selected = Snapshot(permitClass);
        var empty = selected with
        {
            Permit = selected.Permit with { HeaderClassificationCodes = [] }
        };

        var selectedDocument = renderer.Render(new(selected, "A1B2C3D4E5F60718293A4B5C6D7E8F90", Watermark: false));
        var emptyDocument = renderer.Render(new(empty, "A1B2C3D4E5F60718293A4B5C6D7E8F90", Watermark: false));

        Assert.NotEqual(Canonicalise(selectedDocument.Content), Canonicalise(emptyDocument.Content));
    }

    private static PrintPackageRenderRequest Request(PermitClass permitClass) =>
        new(Snapshot(permitClass), "A1B2C3D4E5F60718293A4B5C6D7E8F90", Watermark: false);

    private static PrintPackageSnapshotPayload Snapshot(PermitClass permitClass) => new(
        Guid.Parse("0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b"),
        "PTW-20260915-0007",
        3,
        "ISSUED",
        Draft(permitClass),
        new PermitValidationEvidence(
            "hse.validator.demo",
            "Divalidasi sesuai JSA.",
            IssuedAt.AddHours(-2),
            [
                "SAFETY_RESPIRATOR",
                "SAFETY_FIRE_EXTINGUISHER",
                "SAFETY_PORTABLE_GAS_MONITOR",
                "SAFETY_BARRICADE",
                "SAFETY_LOTO"
            ]),
        new PermitApprovalEvidence(
            "manager.orf.demo",
            "Kepala Departemen Distribusi Gas dan Manajemen ORF",
            ApprovalCapacity.Manager,
            "manager.orf.demo",
            "Kepala Departemen Distribusi Gas dan Manajemen ORF",
            Guid.Parse("0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5c"),
            null,
            "ruleset-2026.09",
            "FM-B-002-NR-B220/1",
            "campaign-2026.09",
            "Saya menyetujui penerbitan PTW ini.",
            IssuedAt,
            "Siti Rahmawati"),
        "ruleset-2026.09",
        "FM-B-002-NR-B220/1",
        "campaign-2026.09",
        IssuedAt,
        AreaOperationsReview: new AreaOperationsReviewEvidence(
            "senior.officer.orf.demo",
            "Andi Pratama",
            "Senior Officer Distribusi Gas dan Manajemen ORF",
            Guid.Parse("0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5d"),
            [
                PermitOperationalConditionCatalog.Isolation,
                PermitOperationalConditionCatalog.IsolationClosedLockValves,
                PermitOperationalConditionCatalog.Depressurized,
                PermitOperationalConditionCatalog.Drained,
                PermitOperationalConditionCatalog.Flushing,
                PermitOperationalConditionCatalog.FlushingN2Purge,
                PermitOperationalConditionCatalog.Other
            ],
            "Verifikasi valve lokal sebelum pekerjaan dimulai",
            "Kondisi operasi Bagian 7 telah ditinjau.",
            IssuedAt.AddMinutes(-15)));

    private static PermitDraft Draft(PermitClass permitClass) => new(
        "Penggantian gasket pada line 8 inch",
        "Pekerjaan penggantian gasket pada flange line gas 8 inch di area ORF Muara Karang "
            + "termasuk isolasi, pembersihan, dan pemasangan kembali.",
        "ORF",
        "sponsor.demo",
        "Budi Santoso",
        "PT Mitra Kerja Sejahtera",
        permitClass,
        RiskLevel.Medium,
        IssuedAt.AddHours(6),
        IssuedAt.AddDays(5),
        null,
        "ESIMI-2026-004512",
        [],
        [],
        ["Job Safety Analisis (JSA)", "Prosedur Pekerjaan", "P & ID, Plot Plan / Lay Out"],
        "CONTRACTOR",
        null,
        "TAG-ORF-LN-08",
        "ORF Muara Karang - Area Metering",
        true,
        "Bersamaan dengan pekerjaan inspeksi rutin di area yang sama.",
        [],
        ["Depressurized", "Drained", "Ventilated"],
        "JSA-ORF-2026-018",
        "Rev.2",
        IssuedAt.AddDays(-9),
        WorkTypeCodes(permitClass),
        OtherWorkTypeDescription: null,
        EquipmentName: "Gas inlet separator",
        WorkOrderNumber: "WO-2026-001",
        AdditionalHazardReference: "Akses sisi utara licin saat hujan; lihat pengendalian pada JSA.",
        HeaderClassificationCodes: HeaderClassificationCodes(permitClass));

    private static IReadOnlyList<string> HeaderClassificationCodes(PermitClass permitClass) =>
        permitClass switch
        {
            PermitClass.HotWork => ["HOT_OPEN_FLAME", "HOT_SPARK"],
            PermitClass.ColdWork => ["COLD_HIGH_RISK"],
            PermitClass.ConfinedSpaceEntry => [],
            _ => throw new ArgumentOutOfRangeException(nameof(permitClass), permitClass, null)
        };

    private static IReadOnlyList<string> WorkTypeCodes(PermitClass permitClass) => permitClass switch
    {
        PermitClass.HotWork => ["HOT_GRINDING", "HOT_WELDING"],
        PermitClass.ColdWork => ["COLD_PAINTING", "COLD_MECHANICAL"],
        PermitClass.ConfinedSpaceEntry => ["CSE_WELDING", "CSE_CLEANING"],
        _ => throw new ArgumentOutOfRangeException(nameof(permitClass), permitClass, null)
    };

    /// <summary>
    /// Removes the three values PDFsharp randomises per document: the six-letter font subset tag, the
    /// trailer file identifier and the XMP document and instance identifiers. Everything else, including
    /// every drawing operator, must match exactly so a render retry reproduces the approved sheet.
    /// </summary>
    private static string Canonicalise(byte[] content)
    {
        var text = System.Text.Encoding.Latin1.GetString(content);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"/[A-Z]{6}\+", "/AAAAAA+");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"/ID\s*\[<[^>]*><[^>]*>\]", "/ID[<0><0>]");
        return System.Text.RegularExpressions.Regex.Replace(
            text,
            @"uuid:[0-9a-fA-F-]{36}",
            "uuid:00000000-0000-0000-0000-000000000000");
    }

    /// <summary>
    /// Writes the rendered sheet so HSSE can lay it beside the controlled original. The fidelity check is
    /// a human comparison; this test only guarantees the document renders and stays deterministic.
    /// </summary>
    private static void DumpForVisualReview(byte[] content, string name)
    {
        var directory = Environment.GetEnvironmentVariable("PTW_PRINT_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, $"{name}.pdf"), content);
    }
}
