namespace Ptw.Infrastructure.Printing;

/// <summary>
/// HSSE reminder printed below the controlled form on page one. The wording was agreed at the UAT of
/// 12 October 2026 (UAT-2026-10-12 no. 11-12) and must be reproduced verbatim.
/// </summary>
internal static class HsseReminder
{
    internal const string Title = "PENGINGAT HSSE";

    internal static readonly IReadOnlyList<string> Lines =
    [
        "1. PASTIKAN Pelaksana Pekerjaan/Kontraktor Memahami serta Mengimplementasikan 10 CLSR & 9 Perilaku Utama, 8 Arahan Direktur Pertamina dan HSSE Golden Rules (PATUH, INTERVENSI, PEDULI) saat Bekerja.",
        "2. PASTIKAN Pelaksana Pekerjaan/Kontraktor Memahami Semua Risiko serta Melaksanakan Tindakan Pengendaliannya.",
        "3. PASTIKAN Pelaksana Pekerjaan/Kontraktor Bekerja Aman dengan Perilaku/Tindakan Aman dan Kondisi Aman.",
        "4. PASTIKAN semua Pelaksana Pekerjaan Fit To Work."
    ];

    private const double TitleHeightMm = 4.4;
    private const double LineHeightMm = 3.9;
    private const double PaddingMm = 1.4;

    internal static double BlockHeightMm => (PaddingMm * 2) + TitleHeightMm + (Lines.Count * LineHeightMm);

    internal static void Draw(FormCanvas canvas, double pageWidthMm, double pageHeightMm, double marginMm)
    {
        var x = marginMm;
        var width = pageWidthMm - (marginMm * 2);
        var y = pageHeightMm - marginMm - BlockHeightMm;
        canvas.Box(x, y, width, BlockHeightMm, heavy: true);
        canvas.Text(x + PaddingMm, y + PaddingMm, width - (PaddingMm * 2), TitleHeightMm, Title, 6.0, bold: true);
        var cursor = y + PaddingMm + TitleHeightMm;
        foreach (var line in Lines)
        {
            canvas.Text(x + PaddingMm, cursor, width - (PaddingMm * 2), LineHeightMm, line, 5.4);
            cursor += LineHeightMm;
        }
    }
}
