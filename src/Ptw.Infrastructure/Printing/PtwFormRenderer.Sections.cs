using Ptw.Application;
using Ptw.Domain;

namespace Ptw.Infrastructure.Printing;

internal sealed partial class PtwFormRenderer
{
    private const double MidX = 92;

    private static void DrawSections345(
        FormCanvas canvas,
        PrintTemplateDescriptor descriptor,
        PrintPackageSnapshotPayload snapshot)
    {
        const double bandY = 83.5;
        const double contentY = bandY + BandHeight;
        const double contentHeight = 68;
        const double section3Width = MidX - LeftX;
        const double section4X = MidX;
        const double section4Width = 85;
        const double section5X = section4X + section4Width;
        var section5Width = LeftX + LeftWidth - section5X;

        canvas.Band(LeftX, bandY, section3Width, BandHeight, "BAGIAN - 3   PERMINTAAN IJIN KERJA");
        canvas.Band(section4X, bandY, section4Width, BandHeight, "BAGIAN - 4   DOKUMEN PENDUKUNG");
        canvas.Band(section5X, bandY, section5Width, BandHeight, "BAGIAN - 5   PERLENGKAPAN SAFETY");

        canvas.Box(LeftX, contentY, section3Width, contentHeight);
        canvas.Box(section4X, contentY, section4Width, contentHeight);
        canvas.Box(section5X, contentY, section5Width, contentHeight);

        DrawSection3(canvas, LeftX, contentY, section3Width, snapshot);
        DrawSection4(canvas, section4X, contentY, section4Width, descriptor, snapshot);
        DrawSection5(canvas, section5X, contentY, section5Width, descriptor, snapshot);
    }

    private static void DrawSection3(
        FormCanvas canvas,
        double x,
        double y,
        double width,
        PrintPackageSnapshotPayload snapshot)
    {
        var draft = snapshot.Permit;
        canvas.Text(
            x,
            y,
            width,
            3.6,
            "(Di- isi oleh pelaksana dan sponsor pekerjaan)",
            4.2,
            align: TextAlign.Center);

        var cursor = y + 4.2;
        const double rowHeight = 4.4;
        canvas.Field(x + 1.5, cursor, width - 3, rowHeight, "Nama Pemohon :", draft.SponsorId, 26);
        cursor += rowHeight;
        // Posisi and Dept are controlled fields the digital form does not capture yet.
        canvas.Field(x + 1.5, cursor, 44, rowHeight, "Posisi :", null, 13);
        canvas.Field(x + 46, cursor, width - 48, rowHeight, "Dept :", null, 12);
        cursor += rowHeight;
        canvas.Field(
            x + 1.5,
            cursor,
            width - 3,
            rowHeight,
            "Nama Perusahaan ( untuk Kontraktor ) :",
            draft.Company,
            52);
        cursor += rowHeight + 2;

        cursor = DrawSignatureSlot(canvas, x, cursor, width, "Tanda Tangan", "Pelaksana Pekerjaan", draft.PerformingAuthority);
        cursor = DrawSignatureSlot(canvas, x, cursor, width, "Tanda Tangan", "Sponsor Pekerjaan", draft.SponsorId);
        _ = cursor;
    }

    private static double DrawSignatureSlot(
        FormCanvas canvas,
        double x,
        double y,
        double width,
        string signatureCaption,
        string roleCaption,
        string? name)
    {
        canvas.Text(x + 1.5, y, width - 3, 4, signatureCaption, 4.8);
        canvas.Field(x + width - 42, y + 5, 40, 4, "Tanggal :", null, 12);
        canvas.WritingRule(x + 1.5, y + 11.5, width - 3);
        canvas.Text(x + 1.5, y + 11.8, width - 3, 4, roleCaption, 4.8, bold: true);
        if (!string.IsNullOrWhiteSpace(name))
        {
            canvas.Text(x + 1.5, y + 15.2, width - 3, 3.6, name, 4.4);
        }

        return y + 19.5;
    }

    private static void DrawSection4(
        FormCanvas canvas,
        double x,
        double y,
        double width,
        PrintTemplateDescriptor descriptor,
        PrintPackageSnapshotPayload snapshot)
    {
        canvas.Text(x, y, width, 3.6, "( Disiapkan oleh Pelaksana Pekerjaan)", 4.2, align: TextAlign.Center);

        var selected = BuildDocumentSelection(snapshot);
        var cursor = y + 4.2;
        const double rowHeight = 4.6;
        var columnWidth = width / 2;

        for (var index = 0; index < descriptor.SupportingDocumentsPrimary.Count; index++)
        {
            canvas.CheckItem(
                x + 1.5,
                cursor + (index * rowHeight),
                columnWidth - 2,
                rowHeight,
                descriptor.SupportingDocumentsPrimary[index],
                IsSelected(selected, descriptor.SupportingDocumentsPrimary[index]),
                4.2);
        }

        for (var index = 0; index < descriptor.SupportingDocumentsSecondary.Count; index++)
        {
            canvas.CheckItem(
                x + columnWidth,
                cursor + (index * rowHeight),
                columnWidth - 2,
                rowHeight,
                descriptor.SupportingDocumentsSecondary[index],
                IsSelected(selected, descriptor.SupportingDocumentsSecondary[index]),
                4.2);
        }
    }

    private static void DrawSection5(
        FormCanvas canvas,
        double x,
        double y,
        double width,
        PrintTemplateDescriptor descriptor,
        PrintPackageSnapshotPayload snapshot)
    {
        canvas.Text(x, y, width, 3.6, "(Diisi oleh HSE)", 4.2, align: TextAlign.Center);

        var selected = SelectedSafetyEquipmentLabels(snapshot);
        var cursor = y + 4.2;
        const double rowHeight = 4.6;
        var columnWidth = width / 2;

        for (var index = 0; index < descriptor.SafetyEquipmentPrimary.Count; index++)
        {
            canvas.CheckItem(
                x + 1.5,
                cursor + (index * rowHeight),
                columnWidth - 2,
                rowHeight,
                descriptor.SafetyEquipmentPrimary[index],
                IsSelected(selected, descriptor.SafetyEquipmentPrimary[index]),
                4.2);
        }

        for (var index = 0; index < descriptor.SafetyEquipmentSecondary.Count; index++)
        {
            canvas.CheckItem(
                x + columnWidth,
                cursor + (index * rowHeight),
                columnWidth - 2,
                rowHeight,
                descriptor.SafetyEquipmentSecondary[index],
                IsSelected(selected, descriptor.SafetyEquipmentSecondary[index]),
                4.2);
        }
    }

    private static void DrawSections67(FormCanvas canvas, PrintPackageSnapshotPayload snapshot)
    {
        const double bandY = 155.5;
        const double contentY = bandY + BandHeight;
        const double contentHeight = 125;
        const double section6Width = MidX - LeftX;
        const double section7X = MidX;
        var section7Width = LeftX + LeftWidth - section7X;

        canvas.Band(
            LeftX,
            bandY,
            section6Width,
            BandHeight,
            "BAGIAN - 6   GAS TES AWAL (Diisi Oleh HSE)");
        canvas.Band(section7X, bandY, section7Width, BandHeight, "BAGIAN - 7   IJIN DARI BAGIAN OPERASI");
        canvas.Box(LeftX, contentY, section6Width, contentHeight);
        canvas.Box(section7X, contentY, section7Width, contentHeight);

        DrawSection6(canvas, LeftX, contentY, section6Width);
        DrawSection7(canvas, section7X, contentY, section7Width, snapshot);
    }

    /// <summary>
    /// Bagian 6 is printed as blank scaffolding. Under the v1.6 hybrid model the initial gas test is
    /// performed and signed by hand in the field, and the system must not pre-empt that determination.
    /// </summary>
    private static void DrawSection6(FormCanvas canvas, double x, double y, double width)
    {
        var cursor = y + 1.5;
        canvas.Text(x + 1.5, cursor, 26, 4.4, "Diperlukan", 4.8);
        canvas.CheckBox(x + 26, cursor + 1, size: 2.6);
        canvas.Text(x + 32, cursor, 30, 4.4, "Tidak Diperlukan", 4.8);
        canvas.CheckBox(x + 62, cursor + 1, size: 2.6);
        cursor += 6;

        canvas.Text(x + 1.5, cursor, width - 3, 4.4, "Hasil Gas Test Awal", 4.8, bold: true);
        cursor += 5;

        // Result grid: Jam, % O2, % LEL, % Lainnya, left blank for the gas tester.
        var columns = new[] { "Jam", "% O2", "% LEL", "% Lainnya" };
        var tableWidth = width - 4;
        var columnWidth = tableWidth / columns.Length;
        const double headerHeight = 5;
        const double dataRowHeight = 5.5;
        const int dataRows = 3;
        var tableHeight = headerHeight + (dataRows * dataRowHeight);
        canvas.Box(x + 2, cursor, tableWidth, tableHeight);
        for (var index = 0; index < columns.Length; index++)
        {
            var columnX = x + 2 + (index * columnWidth);
            if (index > 0)
            {
                canvas.Line(columnX, cursor, columnX, cursor + tableHeight);
            }

            canvas.Text(columnX, cursor, columnWidth, headerHeight, columns[index], 4.6, bold: true, TextAlign.Center);
        }

        for (var row = 0; row <= dataRows; row++)
        {
            var rowY = cursor + headerHeight + (row * dataRowHeight);
            if (row < dataRows)
            {
                canvas.Line(x + 2, rowY, x + 2 + tableWidth, rowY);
            }
        }

        cursor += tableHeight + 6;
        canvas.Field(x + 1.5, cursor, width - 3, 4.4, "Nama Petugas Gas Tes :", null, 40);
        cursor += 5;
        canvas.Field(x + 1.5, cursor, width - 3, 4.4, "No. Pegawai / ID Badge :", null, 40);
        cursor += 5;
        canvas.Text(x + 1.5, cursor, width - 3, 4.4, "Tanda tangan", 4.8);
        cursor += 12;

        canvas.Text(x + 1.5, cursor, width - 3, 4.4, "Mengetahui,", 4.8);
        cursor += 5;
        canvas.Field(x + 1.5, cursor, width - 3, 4.4, "Pejabat HSE :", null, 40);
        cursor += 5;
        canvas.Field(x + 1.5, cursor, width - 3, 4.4, "No. Pegawai / ID Badge :", null, 40);
        cursor += 5;
        canvas.Text(x + 1.5, cursor, width - 3, 4.4, "Tanda Tangan", 4.8);
    }

    private static void DrawSection7(
        FormCanvas canvas,
        double x,
        double y,
        double width,
        PrintPackageSnapshotPayload snapshot)
    {
        var draft = snapshot.Permit;
        var cursor = y + 1.5;

        canvas.Paragraph(
            x + 1.5,
            cursor,
            width - 3,
            "Saya mengijinkan pekerjaan untuk dimulai dengan mematuhi ketentuan kondisi diatas dan juga "
                + "mematuhi verifikasi / syarat - syarat yang dicontreng oleh operator lapangan dan / atau gas tester",
            4.6,
            3.4,
            2);
        cursor += 8;

        var isolation = draft.IsolationPrecautionCodes;
        for (var index = 0; index < PrintTemplateCatalog.IsolationOptions.Length; index++)
        {
            var option = PrintTemplateCatalog.IsolationOptions[index];
            canvas.CheckItem(
                x + 3,
                cursor + (index * 5),
                width - 6,
                5,
                option,
                IsSelected(isolation, option),
                4.6);
        }

        cursor += (PrintTemplateCatalog.IsolationOptions.Length * 5) + 3;

        // Validity window, populated from the approved permit version.
        const double labelWidth = 34;
        const double cellWidth = 30;
        canvas.Box(x + 2, cursor, labelWidth + (cellWidth * 2), 10);
        canvas.Line(x + 2, cursor + 5, x + 2 + labelWidth + (cellWidth * 2), cursor + 5);
        canvas.Line(x + 2 + labelWidth, cursor, x + 2 + labelWidth, cursor + 10);
        canvas.Line(x + 2 + labelWidth + cellWidth, cursor, x + 2 + labelWidth + cellWidth, cursor + 10);
        canvas.Text(x + 3, cursor, labelWidth, 5, "Ijin Berlaku dari", 4.6);
        canvas.Text(x + 3, cursor + 5, labelWidth, 5, "Ijin Berlaku sampai *", 4.6);
        canvas.Text(x + 2 + labelWidth, cursor, cellWidth, 5, FormCanvas.Wib(draft.ValidFrom, "dd/MM/yy"), 4.6, bold: true, TextAlign.Center);
        canvas.Text(x + 2 + labelWidth + cellWidth, cursor, cellWidth, 5, FormCanvas.Wib(draft.ValidFrom, "HH:mm"), 4.6, bold: true, TextAlign.Center);
        canvas.Text(x + 2 + labelWidth, cursor + 5, cellWidth, 5, FormCanvas.Wib(draft.ValidUntil, "dd/MM/yy"), 4.6, bold: true, TextAlign.Center);
        canvas.Text(x + 2 + labelWidth + cellWidth, cursor + 5, cellWidth, 5, FormCanvas.Wib(draft.ValidUntil, "HH:mm"), 4.6, bold: true, TextAlign.Center);
        canvas.Text(x + 100, cursor + 2, width - 102, 5, "* permit berlaku max. 7 hari sejak diterbitkan", 4.4);
        cursor += 13;

        DrawOperationsAuthorityTable(canvas, x, cursor, width, snapshot);
    }

    private static void DrawOperationsAuthorityTable(
        FormCanvas canvas,
        double x,
        double y,
        double width,
        PrintPackageSnapshotPayload snapshot)
    {
        canvas.Text(x + 2, y, width - 4, 4.4, "Penanggung Jawab Operasi", 4.8, bold: true);
        var tableY = y + 5;
        var tableWidth = width - 4;
        double[] columnRatios = [0.28, 0.32, 0.22, 0.18];
        string[] headers = ["Nama", "Jabatan", "Tanda Tangan", "Tanggal"];
        const double headerHeight = 5;
        const double rowHeight = 9;
        var rows = PrintTemplateCatalog.OperationsAuthorityPositions.Length;

        canvas.Box(x + 2, tableY, tableWidth, headerHeight + (rows * rowHeight));

        var offsets = new double[columnRatios.Length + 1];
        var running = 0.0;
        for (var index = 0; index < columnRatios.Length; index++)
        {
            offsets[index] = running;
            running += tableWidth * columnRatios[index];
        }

        offsets[^1] = tableWidth;

        for (var index = 0; index < headers.Length; index++)
        {
            var columnX = x + 2 + offsets[index];
            if (index > 0)
            {
                canvas.Line(columnX, tableY, columnX, tableY + headerHeight + (rows * rowHeight));
            }

            canvas.Text(
                columnX,
                tableY,
                offsets[index + 1] - offsets[index],
                headerHeight,
                headers[index],
                4.6,
                bold: true,
                TextAlign.Center);
        }

        var approval = snapshot.Approval;
        for (var row = 0; row < rows; row++)
        {
            var rowY = tableY + headerHeight + (row * rowHeight);
            canvas.Line(x + 2, rowY, x + 2 + tableWidth, rowY);

            var position = PrintTemplateCatalog.OperationsAuthorityPositions[row];
            canvas.Text(
                x + 3 + offsets[1],
                rowY,
                offsets[2] - offsets[1] - 2,
                rowHeight,
                position,
                4.2);

            // Only the row matching the recorded approver position is populated; the other stays blank
            // for wet signature until OPN-002 resolves the two-signatory discrepancy.
            if (approval is null || !PositionMatches(approval.ActorPosition, position))
            {
                continue;
            }

            canvas.Text(x + 3 + offsets[0], rowY, offsets[1] - offsets[0] - 2, rowHeight, approval.ActorId, 4.2, bold: true);
            canvas.Text(
                x + 3 + offsets[2],
                rowY + 1,
                offsets[3] - offsets[2] - 2,
                rowHeight - 2,
                "Disetujui elektronik",
                4.0,
                bold: true,
                TextAlign.Center);
            canvas.Text(
                x + 3 + offsets[3],
                rowY,
                offsets[4] - offsets[3] - 2,
                rowHeight,
                FormCanvas.Wib(approval.ApprovedAt, "dd/MM/yy HH:mm"),
                4.2,
                align: TextAlign.Center);
        }
    }

    private static bool PositionMatches(string? actorPosition, string controlledPosition)
    {
        if (string.IsNullOrWhiteSpace(actorPosition))
        {
            return false;
        }

        return string.Equals(
            Normalise(actorPosition),
            Normalise(controlledPosition),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves stable Bagian 4 codes to the exact controlled-template labels. The JSA metadata fallback
    /// keeps legacy snapshots readable without changing the immutable package payload.
    /// </summary>
    private static List<string?> BuildDocumentSelection(PrintPackageSnapshotPayload snapshot)
    {
        var selection = snapshot.Permit.RequiredDocumentCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => PermitSupportingDocumentCatalog.Resolve(code).Label)
            .Cast<string?>()
            .ToList();
        if (!string.IsNullOrWhiteSpace(snapshot.Permit.JsaDocumentNumber)
            && !IsSelected(selection, PermitSupportingDocumentCatalog.Resolve(
                PermitSupportingDocumentCatalog.JsaCode).Label))
        {
            selection.Add(PermitSupportingDocumentCatalog.Resolve(
                PermitSupportingDocumentCatalog.JsaCode).Label);
        }

        return selection;
    }
}
