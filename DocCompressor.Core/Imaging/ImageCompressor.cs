using DocCompressor.Core.Pdf;
using SkiaSharp;

namespace DocCompressor.Core.Imaging;

/// <summary>
/// Compression d'une image (photo, scan) sous une taille maximale
/// </summary>
internal static class ImageCompressor
{
    private const int MaxQuality = 90;
    // En dessous de cette taille (plus grand côté), l'image deviendrait illisible
    private const int MinLongSide = 300;
    private const double ScaleStep = 0.85;

    public static CompressionOutcome Compress(byte[] data, string extension, CompressionOptions options, CancellationToken ct)
    {
        if (!options.ConvertImagesToPdf && data.Length <= options.MaxBytes && IsJpegOrPng(data))
            return new CompressionOutcome(data, extension, CompressionMethod.Unchanged,
                "Déjà sous la limite : fichier conservé tel quel");

        using var bitmap = ImageLoader.Load(data);
        bool gray = ImageOps.IsGrayscale(bitmap);
        int longSide = Math.Max(bitmap.Width, bitmap.Height);

        // On réduit progressivement la définition jusqu'à trouver une qualité acceptable qui tient dans la limite
        for (double scale = 1.0; ; scale *= ScaleStep)
        {
            ct.ThrowIfCancellationRequested();
            var (width, height) = ImageOps.Scale(bitmap.Width, bitmap.Height, scale);
            int targetLongSide = Math.Max(width, height);
            if (scale < 1 && targetLongSide < MinLongSide)
                break;

            using var scaled = scale < 1 ? ImageOps.Resize(bitmap, width, height) : null;
            using var grayBitmap = gray ? ImageOps.ToGray(scaled ?? bitmap) : null;
            SKBitmap target = grayBitmap ?? scaled ?? bitmap;

            Func<int, byte[]> encode = options.ConvertImagesToPdf
                ? q => ImagePdfWriter.WriteImageOnA4(ImageOps.EncodeJpeg(target, q), width, height, gray)
                : q => ImageOps.EncodeJpeg(target, q);

            var fit = QualitySearch.FindHighest(encode, options.MaxBytes, MinQualityFor(targetLongSide), MaxQuality, ct);
            if (fit is var (quality, result))
            {
                string details = $"{width}×{height} px, qualité {quality}";
                if (scale < 1)
                    details += $", redimensionnée à {Math.Round(scale * 100)} %";
                if (gray)
                    details += ", niveaux de gris";

                return options.ConvertImagesToPdf
                    ? new CompressionOutcome(result, ".pdf", CompressionMethod.ImageToPdf, "PDF A4 — image " + details)
                    : new CompressionOutcome(result, ".jpg", CompressionMethod.ImageReencoded, "JPEG " + details);
            }

            if (targetLongSide <= MinLongSide)
                break;
        }

        throw new CompressionException(
            $"Impossible de réduire cette image sous {SizeFormatter.Format(options.MaxBytes)} sans la rendre illisible.");
    }

    /// <summary>
    /// Qualité minimale tolérée selon la définition : on préfère réduire la définition que dégrader une grande image
    /// </summary>
    private static int MinQualityFor(int longSide) => longSide switch
    {
        >= 1600 => 70,
        >= 1000 => 50,
        _ => 30,
    };

    private static bool IsJpegOrPng(byte[] data) =>
        data.Length > 8 &&
        ((data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) ||
         (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47));
}
