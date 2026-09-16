using System.Reflection;
using PdfSharp.Fonts;

namespace Ptw.Infrastructure.Printing;

/// <summary>
/// Supplies the print renderer with embedded Liberation Sans faces. The ASP.NET and runtime container
/// images ship without system fonts, so PDFsharp cannot resolve a typeface without this. Embedding also
/// keeps rendering deterministic: the same snapshot renders identically on any host (FSD section 15.2).
/// </summary>
internal sealed class EmbeddedFormFontResolver : IFontResolver
{
    internal const string FamilyName = "Liberation Sans";

    private const string RegularFace = "LiberationSans#Regular";
    private const string BoldFace = "LiberationSans#Bold";
    private const string ResourcePrefix = "Ptw.Infrastructure.Printing.Fonts.";

    private static readonly object SyncRoot = new();
    private static bool _registered;

    /// <summary>
    /// Installs the resolver once per process. PDFsharp exposes a single global font resolver, and
    /// reassigning it after fonts are cached throws, so registration is guarded.
    /// </summary>
    internal static void EnsureRegistered()
    {
        if (_registered)
        {
            return;
        }

        lock (SyncRoot)
        {
            if (_registered)
            {
                return;
            }

            if (GlobalFontSettings.FontResolver is null)
            {
                GlobalFontSettings.FontResolver = new EmbeddedFormFontResolver();
            }

            _registered = true;
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        new(isBold ? BoldFace : RegularFace, isBold, isItalic);

    public byte[]? GetFont(string faceName)
    {
        var resourceName = faceName switch
        {
            BoldFace => ResourcePrefix + "LiberationSans-Bold.ttf",
            _ => ResourcePrefix + "LiberationSans-Regular.ttf"
        };

        using var stream = typeof(EmbeddedFormFontResolver).GetTypeInfo().Assembly
            .GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Font cetak '{resourceName}' tidak ditemukan pada assembly.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
