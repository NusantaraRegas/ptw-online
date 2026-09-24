using PdfSharp.Drawing;
using Ptw.Application;
using Ptw.Domain;

namespace Ptw.Infrastructure.Printing;

/// <summary>
/// Populates the official controlled PDF background. Coordinates are measured in PDF points from the
/// uploaded FM-001/002/003-B-002-NR-B220 pages. The template owns all labels, rules and blank field-work
/// areas; this overlay writes only approved snapshot data and selection marks.
/// </summary>
internal sealed partial class PtwFormRenderer
{
    private const double PointToMillimetre = 25.4 / 72.0;

    private static void DrawControlledTemplateOverlay(
        FormCanvas canvas,
        PrintTemplateDescriptor descriptor,
        PrintPackageSnapshotPayload snapshot,
        string snapshotHash)
    {
        var layout = TemplateOverlayCatalog.Resolve(descriptor.PermitClass);
        var draft = snapshot.Permit;

        OverlayText(canvas, layout.PermitNumber, snapshot.PermitNumber, 4.1, bold: true);
        OverlayText(canvas, layout.PermitDate, FormCanvas.Wib(snapshot.CreatedAt, "dd/MM/yy"), 4.0);
        OverlayFittedText(canvas, layout.WorkOrderNumber, draft.WorkOrderNumber, 4.0, 2.8);
        DrawHeaderClassificationSelection(canvas, draft, layout.HeaderClassificationChecks);

        DrawSelection(
            canvas,
            descriptor.WorkTypes,
            PermitWorkTypeCatalog.NormalizeAndValidate(
                draft.PermitClass,
                draft.WorkTypeCodes,
                draft.WorkTypeCode),
            layout.WorkTypeCheckXs,
            layout.WorkTypeCheckYs);
        OverlayFittedText(
            canvas,
            layout.OtherWorkTypeDescription,
            draft.OtherWorkTypeDescription,
            preferredSize: 4.0,
            minimumSize: 2.8);

        OverlayText(canvas, layout.AppliedDate, FormCanvas.Wib(snapshot.CreatedAt, "dd/MM/yy"), 4.0);
        OverlayText(canvas, layout.PlannedStart, FormCanvas.Wib(draft.ValidFrom, "dd/MM/yy HH:mm"), 4.0);
        OverlayText(canvas, layout.EquipmentTag, draft.EquipmentTag, 4.0);
        OverlayFittedText(
            canvas,
            layout.EquipmentName,
            draft.EquipmentName,
            4.0,
            2.8,
            backgroundBottomInset: 1.5);
        OverlayText(canvas, layout.PlantArea, draft.PlantArea, 4.0);

        var description = string.IsNullOrWhiteSpace(draft.Description)
            ? draft.Title
            : $"{draft.Title} - {draft.Description}";
        OverlayParagraph(canvas, layout.Description, description, 3.5, 3.0, 2, bold: true, yOffsetPoints: -1.0);
        OverlayFittedText(
            canvas,
            layout.AdditionalHazardReference,
            draft.AdditionalHazardReference,
            preferredSize: 3.8,
            minimumSize: 2.6);

        var sponsorName = snapshot.Sponsor?.ActorName ?? draft.SponsorId;
        OverlayFittedText(canvas, layout.SponsorName, sponsorName, 4.0, 2.8);
        OverlayFittedText(
            canvas,
            layout.SponsorPosition,
            snapshot.Sponsor?.ActorPosition is { } position ? $"Posisi : {position}" : null,
            3.8,
            2.6);
        OverlayFittedText(
            canvas,
            layout.SponsorDepartment,
            snapshot.Sponsor?.Department is { } department ? $"Dept : {department}" : null,
            3.8,
            2.6);
        OverlayText(canvas, layout.Company, draft.Company, 4.0, clearBackground: true);
        OverlayText(canvas, layout.PerformingAuthority, draft.PerformingAuthority, 3.8, clearBackground: true);
        OverlayFittedText(canvas, layout.SponsorRole, sponsorName, 3.8, 2.6);
        if (snapshot.Sponsor is not null)
        {
            _ = TryOverlaySignature(canvas, layout.SponsorSignature, snapshot.Sponsor.Signature);
            canvas.Fill(
                Pt(layout.SponsorSignedAt.X - 1),
                Pt(layout.SponsorSignedAt.Y - 1),
                Pt(layout.SponsorSignedAt.Width + 2),
                Pt(layout.SponsorSignedAt.Height + 2),
                XColors.White);
            OverlayText(
                canvas,
                layout.SponsorSignedAt,
                $"Tanggal : {FormCanvas.Wib(snapshot.Sponsor.SubmittedAt, "dd/MM/yy HH:mm")}",
                3.5);
        }

        var selectedDocuments = BuildDocumentSelection(snapshot);
        var selectedSafetyEquipment = SelectedSafetyEquipmentLabels(snapshot);
        DrawChecklistSelection(
            canvas,
            descriptor.SupportingDocumentsPrimary,
            selectedDocuments,
            layout.SupportingPrimaryX,
            layout.ChecklistYs);
        DrawChecklistSelection(
            canvas,
            descriptor.SupportingDocumentsSecondary,
            selectedDocuments,
            layout.SupportingSecondaryX,
            layout.ChecklistYs);
        DrawChecklistSelection(
            canvas,
            descriptor.SafetyEquipmentPrimary,
            selectedSafetyEquipment,
            layout.SafetyPrimaryX,
            layout.ChecklistYs);
        DrawChecklistSelection(
            canvas,
            descriptor.SafetyEquipmentSecondary,
            selectedSafetyEquipment,
            layout.SafetySecondaryX,
            layout.ChecklistYs);

        OverlayText(canvas, layout.ValidFromDate, FormCanvas.Wib(draft.ValidFrom, "dd/MM/yy"), 3.8, align: TextAlign.Center);
        OverlayText(canvas, layout.ValidFromTime, FormCanvas.Wib(draft.ValidFrom, "HH:mm"), 3.8, align: TextAlign.Center);
        OverlayText(canvas, layout.ValidUntilDate, FormCanvas.Wib(draft.ValidUntil, "dd/MM/yy"), 3.8, align: TextAlign.Center);
        OverlayText(canvas, layout.ValidUntilTime, FormCanvas.Wib(draft.ValidUntil, "HH:mm"), 3.8, align: TextAlign.Center);

        DrawOperationalConditionEvidence(canvas, layout, snapshot.AreaOperationsReview);
        DrawApprovalEvidence(canvas, layout, snapshot.AreaOperationsReview, snapshot.Approval);

        // The official COLD worksheet builds the Bagian 3/4 divider from adjacent PDF segments.
        // Chromium can round those segment endpoints differently at common zoom levels, leaving
        // visible gaps. Re-stroking the same rule as one path keeps the controlled layout intact.
        foreach (var rule in TemplateOverlayCatalog.StructuralRepairRules(descriptor.PermitClass))
        {
            canvas.Line(Pt(rule.Start.X), Pt(rule.Start.Y), Pt(rule.End.X), Pt(rule.End.Y));
        }

        ReplaceTemplateLabel(
            canvas,
            layout.Section10OwnerLabel,
            "(Diisi oleh Pemilik Wilayah)",
            4.0,
            3.2,
            TextAlign.Center);
        ReplaceTemplateLabel(
            canvas,
            layout.Section10OfficerLabel,
            "Officer : ....................................",
            3.8,
            3.0);
        ReplaceTemplateLabel(canvas, layout.Section10OfficerReference, " Officer.", 3.8, 3.0);
        ReplaceTemplateLabel(
            canvas,
            layout.Section10ManagerLabel,
            "Manager Pemilik Wilayah : ................",
            3.8,
            2.8);

        var reference = snapshotHash.Length >= 12 ? snapshotHash[..12] : snapshotHash;
        OverlayText(
            canvas,
            layout.ReconciliationReference,
            $"Ref: {snapshot.PermitNumber ?? "-"} | v{snapshot.PermitVersion} | {reference} | Bukti persetujuan elektronik, bukan tanda tangan tersertifikasi",
            4.0,
            align: TextAlign.Right);
    }

    private static void DrawSelection(
        FormCanvas canvas,
        IReadOnlyList<PermitWorkTypeOption> options,
        IReadOnlyCollection<string?>? selected,
        IReadOnlyList<double> checkXs,
        IReadOnlyList<double> checkYs)
    {
        var columns = checkXs.Count;
        foreach (var option in options)
        {
            if (!IsSelected(selected, option.Code))
            {
                continue;
            }

            var column = option.TemplateIndex % columns;
            var row = option.TemplateIndex / columns;
            if (row < checkYs.Count)
            {
                DrawTick(canvas, new PdfPoint(checkXs[column], checkYs[row]));
            }
        }
    }

    private static void DrawHeaderClassificationSelection(
        FormCanvas canvas,
        PermitDraft draft,
        IReadOnlyList<PdfPoint> checkPositions)
    {
        var selected = new HashSet<string>(
            PermitHeaderClassificationCatalog.NormalizeAndValidate(
                draft.PermitClass,
                draft.HeaderClassificationCodes,
                allowMissing: true),
            StringComparer.OrdinalIgnoreCase);
        foreach (var option in PermitHeaderClassificationCatalog.Resolve(draft.PermitClass))
        {
            if (selected.Contains(option.Code) && option.TemplateIndex < checkPositions.Count)
            {
                var point = checkPositions[option.TemplateIndex];
                canvas.CheckMark(Pt(point.X), Pt(point.Y), Pt(7.0));
            }
        }
    }

    private static void DrawChecklistSelection(
        FormCanvas canvas,
        IReadOnlyList<string> labels,
        IReadOnlyCollection<string?>? selected,
        double checkX,
        IReadOnlyList<double> checkYs)
    {
        for (var index = 0; index < labels.Count && index < checkYs.Count; index++)
        {
            if (IsSelected(selected, labels[index]))
            {
                DrawTick(canvas, new PdfPoint(checkX, checkYs[index]));
            }
        }
    }

    private static void DrawOperationalConditionEvidence(
        FormCanvas canvas,
        TemplateOverlayLayout layout,
        AreaOperationsReviewEvidence? review)
    {
        if (review is null)
        {
            return;
        }

        var selected = new HashSet<string>(review.ConditionCodes, StringComparer.OrdinalIgnoreCase);
        string[] mainCodes =
        [
            PermitOperationalConditionCatalog.Isolation,
            PermitOperationalConditionCatalog.Depressurized,
            PermitOperationalConditionCatalog.Drained,
            PermitOperationalConditionCatalog.Ventilated,
            PermitOperationalConditionCatalog.Flushing,
            PermitOperationalConditionCatalog.Other
        ];
        for (var index = 0; index < mainCodes.Length && index < layout.IsolationChecks.Count; index++)
        {
            if (selected.Contains(mainCodes[index]))
            {
                DrawTick(canvas, layout.IsolationChecks[index]);
            }
        }

        string[] childCodes =
        [
            PermitOperationalConditionCatalog.IsolationClosedLockValves,
            PermitOperationalConditionCatalog.IsolationBlind,
            PermitOperationalConditionCatalog.IsolationDisconnect,
            PermitOperationalConditionCatalog.FlushingN2Purge,
            PermitOperationalConditionCatalog.FlushingWater
        ];
        for (var index = 0; index < childCodes.Length && index < layout.OperationalSubConditionChecks.Count; index++)
        {
            if (selected.Contains(childCodes[index]))
            {
                DrawParentheticalTick(canvas, layout.OperationalSubConditionChecks[index]);
            }
        }

        OverlayFittedText(
            canvas,
            layout.OtherOperationalConditionDetail,
            review.OtherConditionDetail,
            3.6,
            2.6);
    }

    private static void DrawApprovalEvidence(
        FormCanvas canvas,
        TemplateOverlayLayout layout,
        AreaOperationsReviewEvidence? review,
        PermitApprovalEvidence? approval)
    {
        if (review is not null && layout.ApprovalRows.Count > 0)
        {
            DrawApprovalRow(
                canvas,
                layout.ApprovalRows[0],
                review.ActorName,
                review.ActorPosition,
                review.ReviewedAt,
                "Diverifikasi elektronik",
                review.Signature);
        }

        if (approval is not null && layout.ApprovalRows.Count > 1)
        {
            DrawApprovalRow(
                canvas,
                layout.ApprovalRows[1],
                approval.ActorName ?? approval.ActorId,
                approval.ActorPosition,
                approval.ApprovedAt,
                "Disetujui elektronik",
                approval.Signature);
        }
    }

    private static void DrawApprovalRow(
        FormCanvas canvas,
        ApprovalOverlayRow target,
        string actorName,
        string actorPosition,
        DateTimeOffset decidedAt,
        string signatureLabel,
        VisualSignatureEvidence? signature)
    {
        OverlayText(canvas, target.Name, actorName, 3.6, align: TextAlign.Center);
        ReplaceTableCellLabel(
            canvas,
            target.Position,
            actorPosition,
            preferredSize: 3.6,
            minimumSize: 2.4,
            align: TextAlign.Center);
        if (!TryOverlaySignature(canvas, target.Signature, signature))
        {
            OverlayText(canvas, target.Signature, signatureLabel, 3.2, align: TextAlign.Center);
        }
        OverlayText(
            canvas,
            target.Date,
            FormCanvas.Wib(decidedAt, "dd/MM/yy HH:mm"),
            3.6,
            align: TextAlign.Center);
    }

    private static bool TryOverlaySignature(
        FormCanvas canvas,
        PdfRect target,
        VisualSignatureEvidence? signature)
    {
        if (signature is null
            || !string.Equals(signature.MediaType, "image/png", StringComparison.OrdinalIgnoreCase)
            || signature.Content.Length == 0)
        {
            return false;
        }

        try
        {
            using var stream = new MemoryStream(signature.Content, writable: false);
            using var image = XImage.FromStream(stream);
            const double paddingPoints = 2;
            var availableWidth = target.Width - (paddingPoints * 2);
            var availableHeight = target.Height - (paddingPoints * 2);
            var scale = Math.Min(
                availableWidth / image.PixelWidth,
                availableHeight / image.PixelHeight);
            var imageWidth = image.PixelWidth * scale;
            var imageHeight = image.PixelHeight * scale;
            canvas.Fill(
                Pt(target.X),
                Pt(target.Y),
                Pt(target.Width),
                Pt(target.Height),
                XColors.White);
            canvas.DrawImage(
                image,
                Pt(target.X + ((target.Width - imageWidth) / 2)),
                Pt(target.Y + ((target.Height - imageHeight) / 2)),
                Pt(imageWidth),
                Pt(imageHeight));
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void DrawTick(FormCanvas canvas, PdfPoint point) =>
        canvas.CheckMark(Pt(point.X + 0.45), Pt(point.Y + 4.8), Pt(4.2));

    private static void DrawParentheticalTick(FormCanvas canvas, PdfPoint point) =>
        canvas.CheckMark(Pt(point.X + 1.9), Pt(point.Y + 2.1), Pt(3.2));

    private static void OverlayText(
        FormCanvas canvas,
        PdfRect rect,
        string? text,
        double size,
        bool bold = false,
        TextAlign align = TextAlign.Left,
        bool clearBackground = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (clearBackground)
        {
            canvas.Fill(Pt(rect.X - 5.5), Pt(rect.Y + 0.8), Pt(rect.Width + 6.5), Pt(rect.Height - 1.6), XColors.White);
        }

        canvas.Text(Pt(rect.X), Pt(rect.Y), Pt(rect.Width), Pt(rect.Height), text, size, bold, align);
    }

    private static void OverlayParagraph(
        FormCanvas canvas,
        PdfRect rect,
        string? text,
        double size,
        double lineHeightMm,
        int maxLines,
        bool bold = false,
        double yOffsetPoints = 0)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        canvas.Paragraph(
            Pt(rect.X),
            Pt(rect.Y + yOffsetPoints),
            Pt(rect.Width),
            text,
            size,
            lineHeightMm,
            maxLines,
            bold);
    }

    private static void OverlayFittedText(
        FormCanvas canvas,
        PdfRect rect,
        string? text,
        double preferredSize,
        double minimumSize,
        double backgroundBottomInset = 0.8)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var size = preferredSize;
        while (size > minimumSize && canvas.MeasureMm(text, size, bold: false) > Pt(rect.Width))
        {
            size -= 0.2;
        }

        canvas.Fill(
            Pt(rect.X),
            Pt(rect.Y + 0.8),
            Pt(rect.Width),
            Pt(rect.Height - 0.8 - backgroundBottomInset),
            XColors.White);
        canvas.Text(Pt(rect.X), Pt(rect.Y), Pt(rect.Width), Pt(rect.Height), text, size);
    }

    private static void ReplaceTemplateLabel(
        FormCanvas canvas,
        PdfRect rect,
        string text,
        double preferredSize,
        double minimumSize,
        TextAlign align = TextAlign.Left)
    {
        var size = preferredSize;
        while (size > minimumSize && canvas.MeasureMm(text, size, bold: false) > Pt(rect.Width))
        {
            size -= 0.2;
        }

        canvas.Fill(Pt(rect.X), Pt(rect.Y), Pt(rect.Width), Pt(rect.Height), XColors.White);
        canvas.Text(Pt(rect.X), Pt(rect.Y), Pt(rect.Width), Pt(rect.Height), text, size, align: align);
    }

    private static void ReplaceTableCellLabel(
        FormCanvas canvas,
        PdfRect rect,
        string text,
        double preferredSize,
        double minimumSize,
        TextAlign align = TextAlign.Left)
    {
        var size = preferredSize;
        while (size > minimumSize && canvas.MeasureMm(text, size, bold: false) > Pt(rect.Width))
        {
            size -= 0.2;
        }

        canvas.Fill(
            Pt(rect.X),
            Pt(rect.Y + 0.8),
            Pt(rect.Width),
            Pt(rect.Height - 1.6),
            XColors.White);
        canvas.Text(Pt(rect.X), Pt(rect.Y), Pt(rect.Width), Pt(rect.Height), text, size, align: align);
    }

    private static double Pt(double points) => points * PointToMillimetre;
}

internal readonly record struct PdfPoint(double X, double Y);

internal readonly record struct PdfLine(PdfPoint Start, PdfPoint End);

internal readonly record struct PdfRect(double X, double Y, double Width, double Height);

internal sealed record ApprovalOverlayRow(
    PdfRect Name,
    PdfRect Position,
    PdfRect Signature,
    PdfRect Date);

internal sealed record TemplateOverlayLayout(
    int PageNumber,
    PdfRect PermitNumber,
    PdfRect PermitDate,
    PdfRect WorkOrderNumber,
    IReadOnlyList<PdfPoint> HeaderClassificationChecks,
    PdfRect AppliedDate,
    PdfRect PlannedStart,
    PdfRect EquipmentTag,
    PdfRect EquipmentName,
    PdfRect PlantArea,
    PdfRect Description,
    PdfRect AdditionalHazardReference,
    PdfRect SponsorName,
    PdfRect SponsorPosition,
    PdfRect SponsorDepartment,
    PdfRect Company,
    PdfRect PerformingAuthority,
    PdfRect SponsorRole,
    PdfRect SponsorSignature,
    PdfRect SponsorSignedAt,
    IReadOnlyList<double> WorkTypeCheckXs,
    IReadOnlyList<double> WorkTypeCheckYs,
    PdfRect OtherWorkTypeDescription,
    double SupportingPrimaryX,
    double SupportingSecondaryX,
    double SafetyPrimaryX,
    double SafetySecondaryX,
    IReadOnlyList<double> ChecklistYs,
    IReadOnlyList<PdfPoint> IsolationChecks,
    IReadOnlyList<PdfPoint> OperationalSubConditionChecks,
    PdfRect OtherOperationalConditionDetail,
    PdfRect ValidFromDate,
    PdfRect ValidFromTime,
    PdfRect ValidUntilDate,
    PdfRect ValidUntilTime,
    IReadOnlyList<ApprovalOverlayRow> ApprovalRows,
    PdfRect Section10OwnerLabel,
    PdfRect Section10OfficerLabel,
    PdfRect Section10OfficerReference,
    PdfRect Section10ManagerLabel,
    PdfRect ReconciliationReference);

internal sealed record TemplatePageSplitLayout(PdfRect PageOne, PdfRect PageTwo);

internal static class TemplateOverlayCatalog
{
    private static readonly PdfLine[] ColdWorkStructuralRepairRules =
    [
        new(new(282.25, 349.75), new(282.25, 454.25))
    ];

    private static readonly TemplateOverlayLayout HotWork = new(
        1,
        new(514, 198, 258, 7),
        new(514, 207, 258, 7),
        new(520, 214, 252, 7),
        [new(283.5, 206.5), new(283.5, 214.2)],
        new(130, 287, 185, 7),
        new(421, 287, 302, 7),
        new(111, 295, 160, 7),
        new(320, 295, 156.5, 7),
        new(511, 295, 261, 7),
        new(125, 302, 645, 18),
        new(223, 331, 549, 7),
        new(116, 354, 177, 7),
        new(74, 363, 82, 7),
        new(156, 363, 137, 7),
        new(162, 372, 131, 7),
        new(123, 412, 169, 7),
        new(119, 450, 174, 7),
        new(110, 421, 68, 27),
        new(187, 440, 106, 7),
        [87.3, 217.6, 309.6, 383.4, 462.9],
        [231.8, 244.2, 256.2, 267.5],
        new(505, 244, 263, 8),
        309.6,
        462.9,
        549.4,
        661.7,
        [350.4, 358.9, 367.9, 377.3, 386.6, 396.1, 406.8, 417.2, 426.6, 436.1, 445.7, 454.5],
        [
            new(383.4, 516.5), new(383.4, 528.2), new(504.4, 528.2),
            new(383.4, 539.8), new(504.4, 539.8), new(383.4, 551.3)
        ],
        [
            new(419.7, 522.3), new(478.5, 522.3), new(511.6, 522.3),
            new(548.9, 545.4), new(581.7, 545.4)
        ],
        new(448, 553, 318, 10),
        new(480, 575, 55, 7),
        new(571, 575, 76, 7),
        new(480, 582, 55, 7),
        new(571, 582, 76, 7),
        [
            new(new(373, 628, 78, 14), new(453, 628, 81, 14), new(543, 628, 89, 14), new(634, 628, 138, 14)),
            new(new(373, 643, 78, 14), new(453, 643, 81, 14), new(543, 643, 89, 14), new(634, 643, 138, 14))
        ],
        new(954, 479, 220, 8),
        new(978.5, 541.5, 94, 8),
        new(1076.5, 550.8, 33, 8),
        new(978.5, 630.5, 94, 8),
        new(782, 672, 393, 8));

    private static readonly TemplateOverlayLayout ColdWork = new(
        2,
        new(530, 196, 214, 7),
        new(530, 205, 214, 7),
        new(536, 212, 208, 7),
        [new(267.5, 204.0), new(267.5, 211.5)],
        new(115, 285, 192, 7),
        new(415, 285, 283, 7),
        new(96, 292, 162, 7),
        new(304, 292, 188.5, 7),
        new(526, 292, 218, 7),
        new(110, 300, 634, 18),
        new(207, 329, 537, 7),
        new(100, 352, 195, 7),
        new(58, 361, 83, 7),
        new(140, 361, 155, 7),
        new(146, 370, 149, 7),
        new(107, 410, 188, 7),
        new(103, 448, 192, 7),
        new(94, 419, 69, 27),
        new(171, 438, 124, 7),
        [71.4, 201.7, 300.6, 381.5, 475.9, 556.5],
        [229.4, 241.8, 253.8, 265.1],
        new(602, 253.5, 139, 8),
        300.6,
        475.9,
        556.5,
        668.3,
        [348.0, 356.5, 365.5, 374.9, 384.2, 393.7, 404.4, 414.8, 424.2, 433.7, 443.3, 452.0],
        [
            new(381.5, 509.8), new(381.5, 521.5), new(515.8, 521.5),
            new(381.5, 533.0), new(515.8, 533.0), new(381.5, 544.6)
        ],
        [
            new(422.1, 515.6), new(480.9, 515.6), new(514.0, 515.6),
            new(555.9, 538.6), new(588.6, 538.6)
        ],
        new(450, 546, 290, 10),
        new(490, 573, 61, 7),
        new(570, 573, 70, 7),
        new(490, 584, 61, 7),
        new(570, 584, 70, 7),
        [
            new(new(367, 630, 84, 14), new(463, 630, 78, 14), new(544, 630, 88, 14), new(634, 630, 110, 14)),
            new(new(367, 645, 84, 14), new(463, 645, 78, 14), new(544, 645, 88, 14), new(634, 645, 110, 14))
        ],
        new(944, 478.5, 210, 8),
        new(961, 534.8, 94, 8),
        new(1059, 544.2, 33, 8),
        new(961, 648.3, 94, 8),
        new(764, 674, 394, 8));

    private static readonly TemplateOverlayLayout ConfinedSpaceEntry = new(
        3,
        new(497, 196, 262, 7),
        new(497, 205, 262, 7),
        new(502, 212, 257, 7),
        [],
        new(80, 284, 189, 7),
        new(372, 284, 346, 7),
        new(63, 291, 155, 7),
        new(259, 291, 202.5, 7),
        new(493, 291, 266, 7),
        new(76, 298, 682, 18),
        new(162, 325, 597, 7),
        new(67, 346, 187, 7),
        new(29, 355, 76, 7),
        new(104, 355, 150, 7),
        new(108, 364, 146, 7),
        new(74, 401, 180, 7),
        new(70, 436, 184, 7),
        new(66, 408, 63, 26),
        new(135, 427, 119, 7),
        [41.5, 163.8, 256.7, 340.6, 440.2, 526.3],
        [227.5, 239.2, 250.5, 263.2],
        new(564, 238.8, 190, 8),
        256.7,
        440.2,
        526.3,
        647.4,
        [342.7, 350.7, 359.2, 367.9, 376.7, 385.6, 395.5, 405.4, 414.1, 423.0, 432.0, 440.8],
        [
            new(340.6, 515.5), new(340.6, 526.3), new(484.7, 526.3),
            new(340.6, 537.3), new(484.7, 537.3), new(340.6, 548.1)
        ],
        [
            new(375.5, 520.9), new(428.7, 520.9), new(458.6, 520.9),
            new(522.5, 542.6), new(552.4, 542.6)
        ],
        new(405, 549, 348, 10),
        new(452, 570, 69, 7),
        new(543, 570, 73, 7),
        new(452, 581, 69, 7),
        new(543, 581, 73, 7),
        [
            new(new(329, 634, 86, 14), new(422, 634, 87, 14), new(527, 634, 88, 14), new(618, 634, 141, 14)),
            new(new(329, 648, 86, 14), new(422, 648, 87, 14), new(527, 648, 88, 14), new(618, 648, 141, 14))
        ],
        new(937, 467.5, 205, 8),
        new(959.2, 527.9, 89, 8),
        new(1047, 536.8, 30, 8),
        new(959.2, 625.9, 89, 8),
        new(774, 674, 371, 8));

    // Tight vector crops measured from the controlled source pages. Page one ends after Bagian 7;
    // page two starts at the left rule of Bagian 8. A small two-point padding keeps the outer rules
    // intact without carrying fragments from the neighbouring half of the original A3 sheet.
    private static readonly TemplatePageSplitLayout HotWorkPages = new(
        new(70, 172, 707, 497),
        new(780, 172, 397, 512));

    private static readonly TemplatePageSplitLayout ColdWorkPages = new(
        new(54, 170, 705, 501),
        new(762, 170, 398, 514));

    private static readonly TemplatePageSplitLayout ConfinedSpaceEntryPages = new(
        new(25, 171, 735, 502),
        new(774, 171, 373, 513));

    internal static TemplateOverlayLayout Resolve(PermitClass permitClass) => permitClass switch
    {
        PermitClass.HotWork => HotWork,
        PermitClass.ColdWork => ColdWork,
        PermitClass.ConfinedSpaceEntry => ConfinedSpaceEntry,
        _ => throw new ArgumentOutOfRangeException(nameof(permitClass), permitClass, "Template overlay PTW tidak tersedia.")
    };

    internal static TemplatePageSplitLayout PageSplit(PermitClass permitClass) => permitClass switch
    {
        PermitClass.HotWork => HotWorkPages,
        PermitClass.ColdWork => ColdWorkPages,
        PermitClass.ConfinedSpaceEntry => ConfinedSpaceEntryPages,
        _ => throw new ArgumentOutOfRangeException(nameof(permitClass), permitClass, "Pemisahan halaman PTW tidak tersedia.")
    };

    internal static IReadOnlyList<PdfLine> StructuralRepairRules(PermitClass permitClass) => permitClass switch
    {
        PermitClass.ColdWork => ColdWorkStructuralRepairRules,
        PermitClass.HotWork or PermitClass.ConfinedSpaceEntry => [],
        _ => throw new ArgumentOutOfRangeException(nameof(permitClass), permitClass, "Template overlay PTW tidak tersedia.")
    };
}
