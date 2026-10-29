using SkiaSharp;

namespace DocCompressor.Core.Imaging;

/// <summary>
/// Filigrane : texte répété en lignes obliques sur toute la surface du document, incrusté dans les pixels
/// (ce qui empêche de le retirer simplement). L'aspect reprend celui de FiligraneFacile (filigrane.beta.gouv.fr),
/// mesuré sur une page A4 traitée par ce service.
/// </summary>
internal static class Watermark
{
    private const string FontResource = "DocCompressor.Core.Fonts.LiberationSans-Regular.ttf";

    // Pente des lignes de texte (montantes vers la droite)
    private const float Angle = -25f;
    // Taille du texte rapportée à la diagonale de l'image : 12,8 pt sur une page A4
    private const float SizeRatio = 1f / 80f;
    // Distance entre deux lignes, en tailles de texte : 104 pt sur une page A4
    private const float LineSpacing = 8.1f;
    // Trois espaces entre deux répétitions du texte
    private const string Separator = "   ";

    // Couleur de chaque ligne, tirée dans cette palette : gris le plus souvent, parfois rouge ou bleu.
    // Semi-transparentes : le document reste lisible sous le filigrane.
    private static readonly SKColor[] Palette =
    [
        new(0, 0, 0, 105),
        new(0, 0, 0, 105),
        new(215, 45, 50, 115),
        new(45, 42, 90, 108),
    ];

    // Ombre grise très floue sous chaque ligne : le filigrane ne se réduit pas à un trait de couleur facile à filtrer
    private static readonly SKColor ShadowColor = new(0, 0, 0, 85);
    private const float ShadowBlur = 0.9f;

    private static readonly Lazy<SKTypeface> LazyTypeface = new(LoadTypeface);

    /// <summary>
    /// Police embarquée (Liberation Sans, mêmes proportions qu'Arial), pour un rendu identique même sur un système sans polices
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
        float size = Math.Max(9f, diagonal * SizeRatio);

        using var paint = new SKPaint { Typeface = Typeface, TextSize = size, IsAntialias = true };

        // Une ligne assez longue pour traverser l'image quelle que soit la rotation
        string unit = text + Separator;
        float unitWidth = paint.MeasureText(unit);
        float half = diagonal / 2 + size;
        int repeats = (int)Math.Ceiling(2 * half / unitWidth) + 1;
        string line = string.Concat(Enumerable.Repeat(unit, repeats));

        // Couleurs tirées d'après le texte : un même filigrane donne toujours le même résultat
        var random = new Random(StableHash(text));
        var lines = new List<(float Y, SKColor Color)>();
        for (float y = -half; y <= half + size * LineSpacing; y += size * LineSpacing)
            lines.Add((y, Palette[random.Next(Palette.Length)]));

        using var canvas = new SKCanvas(bitmap);
        canvas.Translate(width / 2, height / 2);
        canvas.RotateDegrees(Angle);

        using (var blur = SKImageFilter.CreateBlur(size * ShadowBlur, size * ShadowBlur))
        using (var layer = new SKPaint { ImageFilter = blur })
        {
            canvas.SaveLayer(layer);
            paint.Color = ShadowColor;
            foreach (var (y, _) in lines)
                canvas.DrawText(line, -half, y, paint);
            canvas.Restore();
        }

        foreach (var (y, color) in lines)
        {
            paint.Color = color;
            canvas.DrawText(line, -half, y, paint);
        }
    }

    /// <summary>
    /// Empreinte du texte, identique d'une exécution à l'autre (contrairement à string.GetHashCode)
    /// </summary>
    private static int StableHash(string text)
    {
        uint hash = 2166136261;
        foreach (char c in text)
            hash = (hash ^ c) * 16777619;
        return (int)hash;
    }
}
