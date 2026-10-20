namespace Ptw.Infrastructure.Printing;

/// <summary>
/// Bagian 8 (daily revalidation) of the controlled form, completed by hand in the field under the
/// hybrid model, so the renderer prints only the grid and enough writing space, never data. Bagian 9
/// and 10 moved into the application workflow (UAT-2026-10-12) and are no longer drawn.
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
