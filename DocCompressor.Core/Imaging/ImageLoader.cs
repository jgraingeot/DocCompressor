using SkiaSharp;

namespace DocCompressor.Core.Imaging;

/// <summary>
/// Décodage des images : orientation EXIF appliquée, transparence remplacée par du blanc
/// </summary>
internal static class ImageLoader
{
    // Au-delà, l'image est décodée en résolution réduite pour limiter la mémoire utilisée
    private const long MaxDecodedPixels = 60_000_000;

    /// <summary>
    /// Décode une image en bitmap BGRA opaque, dans le bon sens d'affichage
    /// </summary>
    public static SKBitmap Load(byte[] data)
    {
        using var skData = SKData.CreateCopy(data);
        using var codec = SKCodec.Create(skData)
            ?? throw new CompressionException("Format d'image non reconnu ou non pris en charge.");

        var size = codec.Info.Size;
        long pixels = (long)size.Width * size.Height;
        if (pixels > MaxDecodedPixels)
            size = codec.GetScaledDimensions((float)Math.Sqrt((double)MaxDecodedPixels / pixels));

        var alphaType = codec.Info.AlphaType == SKAlphaType.Opaque ? SKAlphaType.Opaque : SKAlphaType.Premul;
        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, alphaType);
        var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
        {
            bitmap.Dispose();
            throw new CompressionException($"Impossible de décoder l'image ({result}).");
        }

        bitmap = ApplyOrigin(bitmap, codec.EncodedOrigin);
        return Flatten(bitmap);
    }

    /// <summary>
    /// Applique l'orientation EXIF (photos prises avec un téléphone) et libère la source si besoin
    /// </summary>
    public static SKBitmap ApplyOrigin(SKBitmap source, SKEncodedOrigin origin)
    {
        float w = source.Width, h = source.Height;
        SKMatrix matrix;
        switch (origin)
        {
            case SKEncodedOrigin.TopRight:      // miroir horizontal
                matrix = new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1);
                break;
            case SKEncodedOrigin.BottomRight:   // rotation 180°
                matrix = new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1);
                break;
            case SKEncodedOrigin.BottomLeft:    // miroir vertical
                matrix = new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1);
                break;
            case SKEncodedOrigin.LeftTop:       // transposition
                matrix = new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1);
                break;
            case SKEncodedOrigin.RightTop:      // rotation 90° horaire
                matrix = new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1);
                break;
            case SKEncodedOrigin.RightBottom:   // transposition inverse
                matrix = new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1);
                break;
            case SKEncodedOrigin.LeftBottom:    // rotation 90° antihoraire
                matrix = new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1);
                break;
            default:
                return source;
        }

        bool swap = origin >= SKEncodedOrigin.LeftTop;
        var info = new SKImageInfo(swap ? source.Height : source.Width, swap ? source.Width : source.Height,
            source.ColorType, source.AlphaType);
        var result = new SKBitmap(info);
        using (var canvas = new SKCanvas(result))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.SetMatrix(matrix);
            canvas.DrawBitmap(source, 0, 0);
        }
        source.Dispose();
        return result;
    }

    /// <summary>
    /// Remplace la transparence par un fond blanc (le JPEG ne gère pas la transparence)
    /// </summary>
    private static SKBitmap Flatten(SKBitmap source)
    {
        if (source.AlphaType == SKAlphaType.Opaque)
            return source;

        var result = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(result))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(source, 0, 0);
        }
        source.Dispose();
        return result;
    }
}
