using System.Reflection;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Ptw.Application;
using Ptw.Domain;

namespace Ptw.Infrastructure.Printing;

/// <summary>
/// Renders one controlled Nusantara Regas PTW form as two A3 portrait pages.
/// </summary>
/// <remarks>
/// Page one contains Bagian 1 to 7. Page two starts at Bagian 8 and contains the blank field-work
/// scaffolding for daily revalidation, completion, inspection and handback. Bagian 1 to 5 and the
/// validity, operational-condition checks, and both approval rows in Bagian 7 are populated from
/// the approved snapshot; Bagian 6 and Bagian 8 to 10 remain completed by hand.
/// </remarks>
internal sealed partial class PtwFormRenderer : IPrintPackageRenderer
{
    internal const string RendererVersion = "ptw-form-renderer/3.3.0";

    private const double PageWidth = 420;
    private const double PageHeight = 297;
    private const double Margin = 6;

    private const double OutputMargin = 8;

    private const double LeftX = 6;
    private const double LeftWidth = 256;
    private const double RightX = 265;
    private const double RightWidth = 149;

    private const double BandHeight = 3.8;

    public bool IsAvailable => true;

    public PrintPackageRenderResult Render(PrintPackageRenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EmbeddedFormFontResolver.EnsureRegistered();

        var snapshot = request.Snapshot;
        var descriptor = PrintTemplateCatalog.Resolve(snapshot.Permit.PermitClass);

        using var document = new PdfDocument();
        // Fixed metadata keeps rendering deterministic so a retry reproduces the approved document.
        document.Info.Title = $"PTW {snapshot.PermitNumber ?? "DRAFT"} - {descriptor.FormCode}";
        document.Info.Author = "NR PTW Online";
        document.Info.Subject = descriptor.FormCode;
        document.Info.CreationDate = snapshot.CreatedAt.UtcDateTime;
        document.Info.ModificationDate = snapshot.CreatedAt.UtcDateTime;

        using var templateStream = typeof(PtwFormRenderer).GetTypeInfo().Assembly
            .GetManifestResourceStream("Ptw.Infrastructure.Printing.Assets.template-ptw-online.pdf")
            ?? throw new InvalidOperationException("Template cetak PTW terkontrol tidak tersedia.");
        using var template = XPdfForm.FromStream(templateStream);
        template.PageNumber = TemplateOverlayCatalog.Resolve(descriptor.PermitClass).PageNumber;

        // Keep the controlled source sheet intact and expose it as two more legible pages. The first
        // crop ends after Bagian 7; the second starts exactly at the Bagian 8 column. Drawing the source
        // PDF through a transform preserves its vector text and rules instead of rasterising the form.
        var pageSplit = TemplateOverlayCatalog.PageSplit(descriptor.PermitClass);
        DrawCroppedPage(
            document,
            template,
            descriptor,
            snapshot,
            request.SnapshotHash,
            request.Watermark,
            pageSplit.PageOne,
            outputPageWidth: 420,
            outputPageHeight: 297);
        DrawCroppedPage(
            document,
            template,
            descriptor,
            snapshot,
            request.SnapshotHash,
            request.Watermark,
            pageSplit.PageTwo,
            outputPageWidth: 297,
            outputPageHeight: 420);

        using var buffer = new MemoryStream();
        document.Save(buffer, false);
        return new PrintPackageRenderResult(buffer.ToArray(), "application/pdf", RendererVersion);
    }

    private static void DrawCroppedPage(
        PdfDocument document,
        XPdfForm template,
        PrintTemplateDescriptor descriptor,
        PrintPackageSnapshotPayload snapshot,
        string snapshotHash,
        bool watermark,
        PdfRect sourceBounds,
        double outputPageWidth,
        double outputPageHeight)
    {
        var sourceX = Pt(sourceBounds.X);
        var sourceY = Pt(sourceBounds.Y);
        var sourceWidth = Pt(sourceBounds.Width);
        var sourceHeight = Pt(sourceBounds.Height);
        var availableWidth = outputPageWidth - (OutputMargin * 2);
        var availableHeight = outputPageHeight - (OutputMargin * 2);
        var scale = Math.Min(availableWidth / sourceWidth, availableHeight / sourceHeight);
        var renderedWidth = sourceWidth * scale;
        var renderedHeight = sourceHeight * scale;
        var destinationX = (outputPageWidth - renderedWidth) / 2;
        var destinationY = (outputPageHeight - renderedHeight) / 2;

        var page = document.AddPage();
        page.Width = XUnit.FromMillimeter(outputPageWidth);
        page.Height = XUnit.FromMillimeter(outputPageHeight);
        using var graphics = XGraphics.FromPdfPage(page);
        using var canvas = new FormCanvas(graphics, ParseColor(descriptor.AccentColorHex));

        var state = graphics.Save();
        graphics.IntersectClip(new XRect(
            FormCanvas.Mm(destinationX),
            FormCanvas.Mm(destinationY),
            FormCanvas.Mm(renderedWidth),
            FormCanvas.Mm(renderedHeight)));
        graphics.TranslateTransform(FormCanvas.Mm(destinationX), FormCanvas.Mm(destinationY));
        graphics.ScaleTransform(scale);
        graphics.TranslateTransform(-FormCanvas.Mm(sourceX), -FormCanvas.Mm(sourceY));
        graphics.DrawImage(template, 0, 0, template.PointWidth, template.PointHeight);
        DrawControlledTemplateOverlay(canvas, descriptor, snapshot, snapshotHash);
        graphics.Restore(state);

        if (watermark)
        {
            canvas.Watermark(outputPageWidth, outputPageHeight, "DRAFT / TIDAK BERLAKU");
        }
    }

    private static void DrawHeader(
        FormCanvas canvas,
        PrintTemplateDescriptor descriptor,
        PrintPackageSnapshotPayload snapshot)
    {
        const double y = Margin;
        const double height = 18;
        canvas.Box(LeftX, y, LeftWidth, height, heavy: true);

        // Logo cell.
        const double logoWidth = 38;
        canvas.Line(LeftX + logoWidth, y, LeftX + logoWidth, y + height);
        DrawLogo(canvas, LeftX + 2, y + 4.5, logoWidth - 4, 9);

        // Class banner cell with the sub-class tick pair from the controlled form.
        const double classX = LeftX + logoWidth;
        const double classWidth = 52;
        canvas.Line(classX + classWidth, y, classX + classWidth, y + height);
        canvas.Fill(classX + 1.5, y + 4, 14, 10, canvas.Accent);
        canvas.Text(
            classX + 1.5,
            y + 4,
            14,
            10,
            descriptor.BannerLabel,
            7.5,
            bold: true,
            TextAlign.Center,
            XColors.White);
        if (descriptor.SubTypes.Count > 0)
        {
            var selectedHeaderClassifications = new HashSet<string>(
                PermitHeaderClassificationCatalog.NormalizeAndValidate(
                    snapshot.Permit.PermitClass,
                    snapshot.Permit.HeaderClassificationCodes,
                    allowMissing: true),
                StringComparer.OrdinalIgnoreCase);
            var headerOptions = PermitHeaderClassificationCatalog.Resolve(descriptor.PermitClass);
            for (var index = 0; index < descriptor.SubTypes.Count; index++)
            {
                var itemY = y + 4.5 + (index * 5);
                canvas.Fill(classX + 17, itemY, 33, 4, canvas.Accent);
                canvas.Text(
                    classX + 18,
                    itemY,
                    24,
                    4,
                    descriptor.SubTypes[index],
                    5.0,
                    bold: true,
                    TextAlign.Left,
                    XColors.White);
                canvas.Box(classX + 44.5, itemY + 0.8, 4.5, 2.4);
                var option = headerOptions.FirstOrDefault(item => item.TemplateIndex == index);
                if (option is not null && selectedHeaderClassifications.Contains(option.Code))
                {
                    canvas.CheckMark(classX + 45.0, itemY + 0.75, 2.1);
                }
            }
        }
        else if (!string.IsNullOrEmpty(descriptor.BannerSubLabel))
        {
            canvas.Fill(classX + 17, y + 6.5, 33, 5, canvas.Accent);
            canvas.Text(
                classX + 17,
                y + 6.5,
                33,
                5,
                descriptor.BannerSubLabel,
                4.6,
                bold: true,
                TextAlign.Center,
                XColors.White);
        }

        // Title cell.
        const double titleX = classX + classWidth;
        const double titleWidth = 90;
        canvas.Line(titleX + titleWidth, y, titleX + titleWidth, y + height);
        canvas.Text(titleX, y + 3, titleWidth, 6, "PERMIT TO WORK", 9, bold: true, TextAlign.Center);
        canvas.Text(titleX, y + 10, titleWidth, 4, $"Formulir {descriptor.FormCode}", 5.0, align: TextAlign.Center);

        // Permit identity cell.
        const double infoX = titleX + titleWidth;
        var infoWidth = LeftX + LeftWidth - infoX;
        var rowHeight = height / 3;
        canvas.Line(infoX, y + rowHeight, infoX + infoWidth, y + rowHeight);
        canvas.Line(infoX, y + (rowHeight * 2), infoX + infoWidth, y + (rowHeight * 2));
        canvas.Field(infoX + 1.5, y, infoWidth - 3, rowHeight, "No. Permit :", snapshot.PermitNumber, 22);
        canvas.Field(
            infoX + 1.5,
            y + rowHeight,
            infoWidth - 3,
            rowHeight,
            "Tanggal :",
            FormCanvas.Wib(snapshot.CreatedAt, "dd/MM/yy"),
            22);
        canvas.Field(
            infoX + 1.5,
            y + (rowHeight * 2),
            infoWidth - 3,
            rowHeight,
            "Work Order No. :",
            snapshot.Permit.WorkOrderNumber,
            22);
    }

    private static void DrawSection1WorkTypes(
        FormCanvas canvas,
        PrintTemplateDescriptor descriptor,
        PrintPackageSnapshotPayload snapshot)
    {
        const double bandY = 24.5;
        canvas.Band(
            LeftX,
            bandY,
            LeftWidth,
            BandHeight,
            "BAGIAN - 1 : Jenis Pekerjaan. (diselesaikan oleh pelaksana pekerjaan)",
            "Conteng ( √ )  Kotak diatas dan dibawah");

        var gridY = bandY + BandHeight;
        const double gridHeight = 24;
        canvas.Box(LeftX, gridY, LeftWidth, gridHeight);

        var columns = descriptor.WorkTypeColumns;
        var templateSlots = descriptor.WorkTypes.Max(option => option.TemplateIndex) + 1;
        var rows = (int)Math.Ceiling(templateSlots / (double)columns);
        var cellWidth = LeftWidth / columns;
        var cellHeight = gridHeight / rows;
        var selected = PermitWorkTypeCatalog.NormalizeAndValidate(
            snapshot.Permit.PermitClass,
            snapshot.Permit.WorkTypeCodes,
            snapshot.Permit.WorkTypeCode);

        foreach (var option in descriptor.WorkTypes)
        {
            var column = option.TemplateIndex % columns;
            var row = option.TemplateIndex / columns;
            canvas.CheckItem(
                LeftX + (column * cellWidth) + 1.5,
                gridY + (row * cellHeight),
                cellWidth - 3,
                cellHeight,
                option.Label,
                IsSelected(selected, option.Code),
                4.6);
        }
    }

    private static void DrawSection2WorkDescription(FormCanvas canvas, PrintPackageSnapshotPayload snapshot)
    {
        const double bandY = 52.5;
        canvas.Band(
            LeftX,
            bandY,
            LeftWidth,
            BandHeight,
            "BAGIAN - 2 : Penjelasan tentang pekerjaan (diselesaikan oleh pelaksana pekerjaan)");

        var y = bandY + BandHeight;
        const double height = 27;
        canvas.Box(LeftX, y, LeftWidth, height);
        var draft = snapshot.Permit;
        const double rowHeight = 4.2;

        canvas.Field(LeftX + 1.5, y, 78, rowHeight, "Tanggal Permit diajukan :", FormCanvas.Wib(snapshot.CreatedAt, "dd/MM/yy"), 38);
        canvas.Text(LeftX + 80, y, 20, rowHeight, "(DD/MM/YY)", 4.2);
        canvas.Field(LeftX + 120, y, 100, rowHeight, "Rencana mulai kerja :", FormCanvas.Wib(draft.ValidFrom, "dd/MM/yy HH:mm"), 34);
        canvas.Text(LeftX + 222, y, 32, rowHeight, "(DD/MM/YY, HH:MM)", 4.2, align: TextAlign.Right);
        canvas.Line(LeftX, y + rowHeight, LeftX + LeftWidth, y + rowHeight);

        var row2 = y + rowHeight;
        canvas.Field(LeftX + 1.5, row2, 82, rowHeight, "No. Equipment :", draft.EquipmentTag, 26);
        canvas.Field(LeftX + 86, row2, 84, rowHeight, "Nama Equipment :", draft.EquipmentName, 28);
        canvas.Field(LeftX + 172, row2, 82, rowHeight, "Plant / Area :", draft.PlantArea, 22);
        canvas.Line(LeftX, row2 + rowHeight, LeftX + LeftWidth, row2 + rowHeight);

        var row3 = row2 + rowHeight;
        canvas.Text(LeftX + 1.5, row3, 40, rowHeight, "Penjelasan Pekerjaan :", 4.8);
        var description = string.IsNullOrWhiteSpace(draft.Description)
            ? draft.Title
            : $"{draft.Title} — {draft.Description}";
        canvas.Paragraph(LeftX + 42, row3 + 0.4, LeftWidth - 45, description, 4.6, 3.2, 2);
        canvas.Line(LeftX, row3 + (rowHeight * 2.2), LeftX + LeftWidth, row3 + (rowHeight * 2.2));

        var row4 = row3 + (rowHeight * 2.2);
        canvas.Text(LeftX + 1.5, row4, 60, rowHeight, "Lampiran Dokumen (Drawing)", 4.8);
        canvas.WritingRule(LeftX + 62, row4 + rowHeight - 0.9, LeftWidth - 65);
        canvas.Line(LeftX, row4 + rowHeight, LeftX + LeftWidth, row4 + rowHeight);

        var row5 = row4 + rowHeight;
        canvas.Field(
            LeftX + 1.5,
            row5,
            LeftWidth - 3,
            rowHeight,
            "Informasi tentang bahaya yang terkait yang belum masuk di permit ini :",
            draft.AdditionalHazardReference,
            112,
            4.6);
    }

    private static void DrawFooter(
        FormCanvas canvas,
        PrintPackageSnapshotPayload snapshot,
        string snapshotHash)
    {
        const double y = 285;
        canvas.Text(
            LeftX,
            y,
            LeftWidth,
            4,
            PrintTemplateCatalog.DistributionFooter,
            4.4,
            align: TextAlign.Center);

        // Reconciliation reference for the signed field copy uploaded at closure (BRD RB-016).
        var reference = snapshotHash.Length >= 12 ? snapshotHash[..12] : snapshotHash;
        canvas.Text(
            RightX,
            y,
            RightWidth,
            4,
            $"Ref: {snapshot.PermitNumber ?? "-"} · v{snapshot.PermitVersion} · {reference} · Bukti persetujuan elektronik, bukan tanda tangan tersertifikasi",
            4.0,
            align: TextAlign.Right);
    }

    private static void DrawLogo(FormCanvas canvas, double x, double y, double width, double height)
    {
        try
        {
            using var stream = typeof(PtwFormRenderer).GetTypeInfo().Assembly
                .GetManifestResourceStream("Ptw.Infrastructure.Printing.Assets.logo-regas.png");
            if (stream is null)
            {
                canvas.Text(x, y, width, height, "NUSANTARA REGAS", 5.4, bold: true, TextAlign.Center);
                return;
            }

            using var image = XImage.FromStream(stream);
            canvas.DrawImage(image, x, y, width, height);
        }
        catch (InvalidOperationException)
        {
            canvas.Text(x, y, width, height, "NUSANTARA REGAS", 5.4, bold: true, TextAlign.Center);
        }
    }

    private static XColor ParseColor(string hex)
    {
        var value = hex.TrimStart('#');
        return XColor.FromArgb(
            Convert.ToInt32(value[..2], 16),
            Convert.ToInt32(value.Substring(2, 2), 16),
            Convert.ToInt32(value.Substring(4, 2), 16));
    }

    /// <summary>
    /// Matches a stored code against a controlled item label, ignoring case, spacing and punctuation so
    /// that the free-text values captured before Increment B still tick the right box where they match.
    /// </summary>
    private static bool IsSelected(IReadOnlyCollection<string?>? selected, string label)
    {
        if (selected is null || selected.Count == 0)
        {
            return false;
        }

        var normalisedLabel = Normalise(label);
        if (normalisedLabel.Length == 0)
        {
            return false;
        }

        foreach (var candidate in selected)
        {
            if (candidate is null)
            {
                continue;
            }

            var normalised = Normalise(candidate);
            if (normalised.Length > 0 && string.Equals(normalised, normalisedLabel, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string[] SelectedSafetyEquipmentLabels(PrintPackageSnapshotPayload snapshot)
    {
        var selected = snapshot.HseValidation?.SafetyEquipmentCodes is { Count: > 0 }
            ? snapshot.HseValidation.SafetyEquipmentCodes
            : snapshot.Permit.SafetyEquipmentCodes;
        return PermitSafetyEquipmentCatalog.Resolve(snapshot.Permit.PermitClass)
            .Where(option =>
                IsSelected(selected, option.Code)
                || IsSelected(selected, option.Label))
            .OrderBy(option => option.TemplateColumn)
            .ThenBy(option => option.TemplateIndex)
            .Select(option => option.Label)
            .ToArray();
    }

    private static string Normalise(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();
}
