namespace Ptw.Infrastructure.Printing;

/// <summary>
/// Bagian 8, 9 and 10 of the controlled form. These are completed by hand in the field under the v1.6
/// hybrid model, so the renderer prints only the grid and enough writing space, never data.
/// </summary>
internal sealed partial class PtwFormRenderer
{
    /// <summary>Leaf column widths of the Bagian 8 revalidation grid, in millimetres, summing to 149.</summary>
    private static readonly double[] RevalidationColumns =
    [
        12,    // Tanggal
        8.5,   // Jam
        7, 7, 7, 7,          // GAS TEST: LEL, O2%, H2S, CO
        13.5, 9,             // Pelaksana Isolasi / Petugas Gas Test: Tanda Tangan, Badge
        13.5, 9,             // Pelaksana Pekerjaan: Tanda Tangan, Badge
        12, 8.5, 13.5,       // Pengawas Lapangan: Tanggal, Jam, Tanda Tangan
        6.5, 6.5,            // Status: Open, Close
        9                    // Badge
    ];

    private static void DrawSection8Revalidation(FormCanvas canvas)
    {
        const double bandY = Margin;
        canvas.Band(RightX, bandY, RightWidth, BandHeight, "BAGIAN - 8   REVALIDASI IJIN KERJA");

        var noteY = bandY + BandHeight;
        canvas.Box(RightX, noteY, RightWidth, 3.6);
        canvas.Text(
            RightX + 1.5,
            noteY,
            RightWidth - 3,
            3.6,
            "( Revalidasi harus dilakukan untuk menyakinkan bahwa kondisi lapangan belum berubah sejak awal mulainya )",
            3.9);

        var tableY = noteY + 3.6;
        const double groupHeight = 6.5;
        const double headerHeight = 4.5;
        const double subHeaderHeight = 3.6;
        const double dataRowHeight = 9;
        const int dataRows = 16;
        var headerBottom = tableY + groupHeight + headerHeight + subHeaderHeight;
        var tableHeight = headerBottom - tableY + (dataRows * dataRowHeight);

        var offsets = BuildOffsets(RevalidationColumns);
        canvas.Box(RightX, tableY, RightWidth, tableHeight);

        // Group header: the three roles that sign each revalidation. Only the group boundaries are ruled
        // here, so the leaf column rules below do not cut through a group caption.
        DrawSpan(canvas, offsets, 2, 8, tableY, groupHeight, "Pelaksana Isolasi/Petugas Gas Test", 3.6, "(Field Operator)");
        DrawSpan(canvas, offsets, 8, 10, tableY, groupHeight, "Pelaksana Pekerjaan", 3.6);
        DrawSpan(canvas, offsets, 10, 16, tableY, groupHeight, "Pengawas Lapangan", 3.6, "( Leader Operator )");
        foreach (var boundary in new[] { 2, 8, 10 })
        {
            canvas.Line(RightX + offsets[boundary], tableY, RightX + offsets[boundary], tableY + groupHeight);
        }

        canvas.Line(RightX, tableY + groupHeight, RightX + RightWidth, tableY + groupHeight);

        // Tanggal and Jam span the header and sub-header rows.
        var headerY = tableY + groupHeight;
        DrawSpan(canvas, offsets, 0, 1, headerY, headerHeight + subHeaderHeight, "Tanggal", 4.0);
        DrawSpan(canvas, offsets, 1, 2, headerY, headerHeight + subHeaderHeight, "Jam", 4.0);

        DrawSpan(canvas, offsets, 2, 6, headerY, headerHeight, "GAS TEST", 4.0);
        DrawSpan(canvas, offsets, 6, 7, headerY, headerHeight + subHeaderHeight, "Tanda Tangan", 3.6);
        DrawSpan(canvas, offsets, 7, 8, headerY, headerHeight + subHeaderHeight, "Badge", 3.6);
        DrawSpan(canvas, offsets, 8, 9, headerY, headerHeight + subHeaderHeight, "Tanda Tangan", 3.6);
        DrawSpan(canvas, offsets, 9, 10, headerY, headerHeight + subHeaderHeight, "Badge", 3.6);
        DrawSpan(canvas, offsets, 10, 11, headerY, headerHeight + subHeaderHeight, "Tanggal", 3.6);
        DrawSpan(canvas, offsets, 11, 12, headerY, headerHeight + subHeaderHeight, "Jam", 3.6);
        DrawSpan(canvas, offsets, 12, 13, headerY, headerHeight + subHeaderHeight, "Tanda Tangan", 3.6);
        DrawSpan(canvas, offsets, 13, 15, headerY, headerHeight, "Status", 4.0);
        DrawSpan(canvas, offsets, 15, 16, headerY, headerHeight + subHeaderHeight, "Badge", 3.6);

        var subHeaderY = headerY + headerHeight;
        canvas.Line(RightX + offsets[2], subHeaderY, RightX + offsets[6], subHeaderY);
        canvas.Line(RightX + offsets[13], subHeaderY, RightX + offsets[15], subHeaderY);
        string[] gasColumns = ["LEL", "O2%", "H2S", "CO"];
        for (var index = 0; index < gasColumns.Length; index++)
        {
            DrawSpan(canvas, offsets, 2 + index, 3 + index, subHeaderY, subHeaderHeight, gasColumns[index], 3.6);
        }

        DrawSpan(canvas, offsets, 13, 14, subHeaderY, subHeaderHeight, "Open", 3.6);
        DrawSpan(canvas, offsets, 14, 15, subHeaderY, subHeaderHeight, "Close", 3.6);
        canvas.Line(RightX, headerBottom, RightX + RightWidth, headerBottom);

        // Leaf column rules start below the group header so group captions stay intact.
        for (var index = 1; index < RevalidationColumns.Length; index++)
        {
            canvas.Line(RightX + offsets[index], headerY, RightX + offsets[index], tableY + tableHeight);
        }

        for (var row = 1; row < dataRows; row++)
        {
            var rowY = headerBottom + (row * dataRowHeight);
            canvas.Line(RightX, rowY, RightX + RightWidth, rowY);
        }
    }

    private static void DrawSections910(FormCanvas canvas)
    {
        const double bandY = 174.5;
        const double contentY = bandY + BandHeight;
        const double contentHeight = 105.5;
        const double section9Width = 68;
        const double section10X = RightX + section9Width;
        var section10Width = RightX + RightWidth - section10X;

        canvas.Band(RightX, bandY, section9Width, BandHeight, "BAGIAN - 9   PEKERJAAN SELESAI");
        canvas.Band(
            section10X,
            bandY,
            section10Width,
            BandHeight,
            "BAGIAN - 10   INSPEKSI, PERSETUJUAN & PENGEMBALIAN");
        canvas.Box(RightX, contentY, section9Width, contentHeight);
        canvas.Box(section10X, contentY, section10Width, contentHeight);

        DrawSection9(canvas, RightX, contentY, section9Width);
        DrawSection10(canvas, section10X, contentY, section10Width);
    }

    private static void DrawSection9(FormCanvas canvas, double x, double y, double width)
    {
        canvas.Text(
            x,
            y,
            width,
            3.6,
            "(Diisi oleh Pelaksana Pekerjaan dan Sponsor Pekerjaan)",
            3.9,
            align: TextAlign.Center);

        var cursor = y + 4.5;
        canvas.CheckItem(x + 1.5, cursor, width - 3, 5, "Pekerjaan telah selesai", size: 4.4);
        cursor += 5.5;
        canvas.CheckItem(
            x + 1.5,
            cursor,
            width - 3,
            5,
            "Pekerjaan belum diselesaikan dalam waktu 7 hari",
            size: 4.4);
        cursor += 5.5;
        canvas.CheckItem(
            x + 5,
            cursor,
            width - 6.5,
            5,
            "Semua Peralatan dan material sudah dikeluarkan dari lapangan",
            size: 4.0);
        cursor += 5.5;
        canvas.CheckItem(
            x + 5,
            cursor,
            width - 6.5,
            5,
            "Semua Peralatan dan material belum dikeluarkan dari lapangan",
            size: 4.0);
        cursor += 7;

        canvas.Text(x + 1.5, cursor, width - 3, 4.4, "Penjelasan :", 4.4);
        for (var line = 0; line < 3; line++)
        {
            canvas.WritingRule(x + 1.5, cursor + 5.5 + (line * 5), width - 3);
        }

        cursor += 22;
        canvas.Field(x + 1.5, cursor, width - 3, 4.4, "Pelaksana Pekerjaan :", null, 34);
        cursor += 5;
        canvas.Field(x + 1.5, cursor, width - 3, 4.4, "No ID Badge :", null, 34);
        cursor += 5;
        canvas.Text(x + 1.5, cursor, width - 3, 4.4, "Tanda Tangan", 4.4);
        cursor += 11;

        canvas.Field(x + 1.5, cursor, width - 3, 4.4, "Sponsor Pekerjaan :", null, 34);
        cursor += 5;
        canvas.Field(x + 1.5, cursor, width - 3, 4.4, "No. ID Badge :", null, 34);
        cursor += 5;
        canvas.Text(x + 1.5, cursor, width - 3, 4.4, "Tanda Tangan", 4.4);
    }

    private static void DrawSection10(FormCanvas canvas, double x, double y, double width)
    {
        canvas.Text(x, y, width, 3.6, "(Diisi oleh Pemilik Wilayah)", 3.9, align: TextAlign.Center);

        var cursor = y + 4.5;
        cursor = DrawWrappedCheckItem(
            canvas,
            x,
            cursor,
            width,
            "Kami telah melakukan inspeksi terhadap area kerja, peralatan milik pelaksana kerja sudah tidak ada dan area kerja sudah bersih");
        cursor = DrawWrappedCheckItem(
            canvas,
            x,
            cursor,
            width,
            "Pekerjaan belum selesai dan statusnya adalah :");
        for (var line = 0; line < 2; line++)
        {
            canvas.WritingRule(x + 6, cursor + (line * 5), width - 8);
        }

        cursor += 12;
        canvas.Field(x + 1.5, cursor, width * 0.55, 4.4, "Officer :", null, 24);
        canvas.Text(x + (width * 0.6), cursor, width * 0.38, 4.4, "Tanda Tangan", 4.2);
        cursor += 8;

        cursor = DrawWrappedCheckItem(
            canvas,
            x,
            cursor,
            width,
            "Saya setuju pekerjaan telah selesai atas rincian Officer. Tempat kerja dan peralatan telah kembali normal.");
        cursor = DrawWrappedCheckItem(
            canvas,
            x,
            cursor,
            width,
            "Semua sistem inhibited telah dikembalikan ke normal.");
        cursor = DrawWrappedCheckItem(
            canvas,
            x,
            cursor,
            width,
            "Peralatan / area kerja telah diserahkan kepada kami dari Pelaksana kerja, kami berupaya membuat peralatan / daerah bekerja ke kondisi operasional setelah mencabut semua rambu - rambu yang sudah tidak diperlukan, barikade, penggembokan dll, dan setelah semua langkah tindakan pencegahan operasional yang diperlukan / langkah prosedural.");

        cursor += 4;
        canvas.Field(x + 1.5, cursor, width * 0.55, 4.4, "Manager Pemilik Wilayah :", null, 26);
        canvas.Text(x + (width * 0.6), cursor, width * 0.38, 4.4, "Tanda Tangan", 4.2);
    }

    private static double DrawWrappedCheckItem(
        FormCanvas canvas,
        double x,
        double y,
        double width,
        string label)
    {
        canvas.CheckBox(x + 1.5, y + 0.6, size: 2.4);
        var used = canvas.Paragraph(x + 5, y, width - 6.5, label, 4.0, 3.2, 5);
        return y + Math.Max(used, 3.2) + 1.6;
    }

    private static double[] BuildOffsets(double[] widths)
    {
        var offsets = new double[widths.Length + 1];
        var running = 0.0;
        for (var index = 0; index < widths.Length; index++)
        {
            offsets[index] = running;
            running += widths[index];
        }

        offsets[^1] = running;
        return offsets;
    }

    private static void DrawSpan(
        FormCanvas canvas,
        double[] offsets,
        int fromColumn,
        int toColumn,
        double y,
        double height,
        string text,
        double size,
        string? secondLine = null)
    {
        var x = RightX + offsets[fromColumn];
        var width = offsets[toColumn] - offsets[fromColumn];
        if (secondLine is null)
        {
            canvas.Text(x, y, width, height, text, size, bold: true, TextAlign.Center);
            return;
        }

        canvas.Text(x, y, width, height / 2, text, size, bold: true, TextAlign.Center);
        canvas.Text(x, y + (height / 2), width, height / 2, secondLine, size, bold: true, TextAlign.Center);
    }
}
