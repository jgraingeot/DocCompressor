using SkiaSharp;

namespace DocCompressor.Core.Imaging;

/// <summary>
/// Filigrane : texte répété en diagonale sur toute la surface du document.
/// Il est incrusté dans les pixels, ce qui empêche de le retirer simplement.
/// </summary>
internal static class Watermark
{
    private const string FontResource = "DocCompressor.Core.Fonts.Inter-SemiBold.otf";
    private const float Angle = -30f;

    // Texte gris foncé semi-transparent, bordé de blanc pour rester lisible sur les zones sombres.
    // Couleurs neutres : un scan en noir et blanc reste en niveaux de gris.
    private static readonly SKColor TextColor = new(60, 60, 60, 110);
    private static readonly SKColor HaloColor = new(255, 255, 255, 100);

    private static readonly Lazy<SKTypeface> LazyTypeface = new(LoadTypeface);

    /// <summary>
    /// Police embarquée (Inter), pour un rendu identique même sur un système sans polices
    /// </summary>
    internal static SKTypeface Typeface => LazyTypeface.Value;

    private static SKTypeface LoadTypeface()
    {
        using var stream = typeof(Watermark).Assembly.GetManifestResourceStream(FontResource)
            ?? throw new InvalidOperationException($"Ressource {FontResource} introuvable.");
        using var data = SKData.Create(stream);
        return SKTypeface.FromData(data)
            ?? throw new InvalidOperationException("Police du filigrane illisible.");
    }

    /// <summary>
    /// Dessine le filigrane sur toute l'image ; sa taille est proportionnelle à celle de l'image
    /// </summary>
    public static void Apply(SKBitmap bitmap, string text)
    {
        text = text.Trim();
        if (text.Length == 0)
            return;

        float width = bitmap.Width, height = bitmap.Height;
        float diagonal = MathF.Sqrt(width * width + height * height);
        float size = Math.Max(10f, diagonal / 50f);

        using var fill = new SKPaint { Typeface = Typeface, TextSize = size, Color = TextColor, IsAntialias = true };
        using var halo = new SKPaint
        {
            Typeface = Typeface,
            TextSize = size,
            Color = HaloColor,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = size / 8f,
            StrokeJoin = SKStrokeJoin.Round,
        };

        // Une ligne assez longue pour traverser l'image quelle que soit la rotation
        string unit = text + "        ";
        float unitWidth = fill.MeasureText(unit);
        float half = diagonal / 2 + size;
        int repeats = (int)Math.Ceiling((2 * half + unitWidth) / unitWidth) + 1;
        string line = string.Concat(Enumerable.Repeat(unit, repeats));
        float lineSpacing = size * 3.5f;

        using var canvas = new SKCanvas(bitmap);
        canvas.Translate(width / 2, height / 2);
        canvas.RotateDegrees(Angle);
        int index = 0;
        for (float y = -half; y <= half + lineSpacing; y += lineSpacing, index++)
        {
            // Lignes décalées les unes des autres, pour que le texte ne forme pas de colonnes
            float x = -half - index % 3 * unitWidth / 3f;
            canvas.DrawText(line, x, y, halo);
            canvas.DrawText(line, x, y, fill);
        }
    }
}
