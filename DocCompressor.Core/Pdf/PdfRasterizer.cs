using DocCompressor.Core.Imaging;

namespace DocCompressor.Core.Pdf;

/// <summary>
/// Compression d'un PDF en convertissant chaque page en image JPEG.
/// Fonctionne avec tous les PDF, mais le texte n'est plus sélectionnable.
/// </summary>
internal static class PdfRasterizer
{
    // Résolutions essayées, de la meilleure à la plus faible, avec la qualité JPEG minimale tolérée pour chacune
    private static readonly (int Dpi, int MinQuality)[] Levels =
        [(200, 60), (150, 55), (120, 45), (100, 40), (85, 35), (72, 30), (60, 30), (50, 25)];

    private static readonly int[] Qualities = [85, 78, 70, 62, 55, 48, 40, 34, 28, 25];

    // Plus grand côté d'une page rendue, en pixels (évite de saturer la mémoire avec les grands formats)
    private const int MaxPageLongSide = 4000;

    public static CompressionOutcome Compress(byte[] data, long maxBytes, CancellationToken ct)
    {
        using var document = PdfiumDocument.Load(data);
        int pageCount = document.PageCount;
        if (pageCount == 0)
            throw new CompressionException("Ce PDF ne contient aucune page.");

        var pageSizes = Enumerable.Range(0, pageCount).Select(document.GetPageSize).ToArray();
        long budget = maxBytes - ImagePdfWriter.EstimateOverhead(pageCount);

        foreach (var (dpi, minQuality) in Levels)
        {
            var planner = new QualityPlanner(Qualities.Where(q => q >= minQuality).ToArray(), budget);
            var pixelSizes = new (int Width, int Height)[pageCount];
            var grays = new bool[pageCount];

            for (int i = 0; i < pageCount && planner.HasFeasibleQuality; i++)
            {
                ct.ThrowIfCancellationRequested();
                pixelSizes[i] = PixelSize(pageSizes[i], dpi);
                using var bitmap = document.RenderPage(i, pixelSizes[i].Width, pixelSizes[i].Height);
                grays[i] = ImageOps.IsGrayscale(bitmap);
                using var gray = grays[i] ? ImageOps.ToGray(bitmap) : null;
                var target = gray ?? bitmap;
                planner.AddUnit(q =>
                {
                    ct.ThrowIfCancellationRequested();
                    return ImageOps.EncodeJpeg(target, q);
                });
            }

            if (planner.Best() is not var (quality, encodings))
                continue;

            var pages = new List<ImagePage>(pageCount);
            for (int i = 0; i < pageCount; i++)
            {
                var (pageWidth, pageHeight) = pageSizes[i];
                pages.Add(new ImagePage(encodings[i]!, pixelSizes[i].Width, pixelSizes[i].Height, grays[i],
                    pageWidth, pageHeight, (0, 0, pageWidth, pageHeight)));
            }

            var pdf = ImagePdfWriter.Write(pages);
            if (pdf.Length <= maxBytes)
            {
                string pagesText = pageCount > 1 ? $"{pageCount} pages" : "1 page";
                return new CompressionOutcome(pdf, ".pdf", CompressionMethod.PdfRasterized,
                    $"PDF converti en images : {pagesText}, {dpi} ppp, qualité {quality} (texte non sélectionnable)");
            }
        }

        throw new CompressionException(
            $"Impossible de réduire ce PDF sous {SizeFormatter.Format(maxBytes)} en restant lisible " +
            "(trop de pages pour cette limite : essayez de le découper).");
    }

    private static (int Width, int Height) PixelSize((double Width, double Height) pageSize, int dpi)
    {
        double width = pageSize.Width * dpi / 72.0;
        double height = pageSize.Height * dpi / 72.0;
        double longSide = Math.Max(width, height);
        if (longSide > MaxPageLongSide)
        {
            width *= MaxPageLongSide / longSide;
            height *= MaxPageLongSide / longSide;
        }
        return (Math.Max(1, (int)Math.Round(width)), Math.Max(1, (int)Math.Round(height)));
    }
}
