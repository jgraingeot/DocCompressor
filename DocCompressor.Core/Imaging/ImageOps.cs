using SkiaSharp;

namespace DocCompressor.Core.Imaging;

/// <summary>
/// Opérations sur les bitmaps : redimensionnement, détection et conversion en niveaux de gris, encodage JPEG
/// </summary>
internal static class ImageOps
{
    // Écart entre canaux au-delà duquel un pixel est considéré comme coloré (au-dessus du bruit de numérisation)
    private const int ColorThreshold = 30;
    // Proportion de pixels colorés au-delà de laquelle l'image est considérée en couleur
    private const double ColorPixelRatio = 0.0005;

    /// <summary>
    /// Redimensionne avec un filtrage de haute qualité
    /// </summary>
    public static SKBitmap Resize(SKBitmap source, int width, int height)
    {
        var info = new SKImageInfo(width, height, source.ColorType, source.AlphaType);
        return source.Resize(info, SKFilterQuality.High)
            ?? throw new CompressionException("Échec du redimensionnement de l'image.");
    }

    /// <summary>
    /// Indique si une image BGRA ne contient (pratiquement) que des gris : document noir et blanc numérisé en couleur
    /// </summary>
    public static unsafe bool IsGrayscale(SKBitmap bitmap)
    {
        if (bitmap.ColorType == SKColorType.Gray8)
            return true;
        if (bitmap.ColorType != SKColorType.Bgra8888 && bitmap.ColorType != SKColorType.Rgba8888)
            return false;

        // Échantillonnage d'environ 250 000 pixels
        long total = (long)bitmap.Width * bitmap.Height;
        int step = Math.Max(1, (int)Math.Sqrt(total / 250_000.0));
        long sampled = 0, colored = 0;
        byte* basePtr = (byte*)bitmap.GetPixels();
        int rowBytes = bitmap.RowBytes;

        for (int y = 0; y < bitmap.Height; y += step)
        {
            byte* row = basePtr + (long)y * rowBytes;
            for (int x = 0; x < bitmap.Width; x += step)
            {
                byte* p = row + x * 4;
                int c0 = p[0], c1 = p[1], c2 = p[2];
                int max = Math.Max(c0, Math.Max(c1, c2));
                int min = Math.Min(c0, Math.Min(c1, c2));
                if (max - min > ColorThreshold)
                    colored++;
                sampled++;
            }
        }
        return colored <= sampled * ColorPixelRatio;
    }

    /// <summary>
    /// Convertit une image BGRA/RGBA en niveaux de gris (luminance)
    /// </summary>
    public static unsafe SKBitmap ToGray(SKBitmap source)
    {
        if (source.ColorType == SKColorType.Gray8)
            return source.Copy();

        bool bgr = source.ColorType == SKColorType.Bgra8888;
        var result = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Gray8, SKAlphaType.Opaque));
        byte* src = (byte*)source.GetPixels();
        byte* dst = (byte*)result.GetPixels();
        int srcRowBytes = source.RowBytes, dstRowBytes = result.RowBytes;

        for (int y = 0; y < source.Height; y++)
        {
            byte* s = src + (long)y * srcRowBytes;
            byte* d = dst + (long)y * dstRowBytes;
            for (int x = 0; x < source.Width; x++, s += 4)
            {
                int r = bgr ? s[2] : s[0];
                int g = s[1];
                int b = bgr ? s[0] : s[2];
                d[x] = (byte)((r * 299 + g * 587 + b * 114 + 500) / 1000);
            }
        }
        return result;
    }

    /// <summary>
    /// Encode en JPEG (sous-échantillonnage 4:2:0) à la qualité demandée
    /// </summary>
    public static byte[] EncodeJpeg(SKBitmap bitmap, int quality)
    {
        using var pixmap = bitmap.PeekPixels();
        var options = new SKJpegEncoderOptions(quality, SKJpegEncoderDownsample.Downsample420, SKJpegEncoderAlphaOption.Ignore);
        using var data = pixmap.Encode(options)
            ?? throw new CompressionException("Échec de l'encodage JPEG.");
        return data.ToArray();
    }

    /// <summary>
    /// Dimensions après réduction proportionnelle (au moins 1 pixel)
    /// </summary>
    public static (int Width, int Height) Scale(int width, int height, double scale)
    {
        if (scale >= 1)
            return (width, height);
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }
}
