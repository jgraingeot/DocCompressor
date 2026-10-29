using System.Globalization;
using System.Text;
using DocCompressor.Core.Imaging;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SkiaSharp;

namespace DocCompressor.Core.Pdf;

/// <summary>
/// Assemblage de plusieurs documents (images et PDF) en un seul PDF, dans l'ordre donné.
/// Le PDF obtenu est volontairement de bonne qualité : il est compressé ensuite comme n'importe quel PDF.
/// </summary>
internal static class DocumentMerger
{
    // Définition des images placées sur les pages, avant la compression finale
    private const int ImageDpi = 300;
    private const int ImageQuality = 92;
    // Résolution utilisée pour les PDF que PDFsharp ne sait pas importer (convertis en images)
    private const int FallbackDpi = 200;

    /// <summary>
    /// Assemble les documents ; les images sont placées une par page A4, ou deux par page si demandé
    /// </summary>
    public static (byte[] Pdf, int PageCount) Build(
        IReadOnlyList<(string Name, byte[] Data)> documents, bool twoImagesPerPage, CancellationToken ct)
    {
        var output = new PdfDocument();
        var sources = new List<PdfDocument>();
        var pendingImages = new List<SKBitmap>();

        // Place les images en attente (une seule si le nombre d'images est impair)
        void FlushImages()
        {
            if (pendingImages.Count == 0)
                return;
            var boxes = PageLayout.Pair(pendingImages.Select(b => (b.Width, b.Height)).ToList());
            AddImagePage(output, PageLayout.A4Width, PageLayout.A4Height,
                pendingImages.Select((bitmap, i) => (bitmap, boxes[i])).ToList());
            foreach (var bitmap in pendingImages)
                bitmap.Dispose();
            pendingImages.Clear();
        }

        try
        {
            foreach (var (name, data) in documents)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (DocumentCompressor.IsPdf(data))
                    {
                        FlushImages();
                        AppendPdf(output, data, sources, ct);
                        continue;
                    }

                    var bitmap = ImageLoader.Load(data);
                    if (twoImagesPerPage)
                    {
                        pendingImages.Add(bitmap);
                        if (pendingImages.Count == 2)
                            FlushImages();
                        continue;
                    }

                    using (bitmap)
                    {
                        var (pageWidth, pageHeight, box) = PageLayout.Single(bitmap.Width, bitmap.Height);
                        AddImagePage(output, pageWidth, pageHeight, [(bitmap, box)]);
                    }
                }
                catch (CompressionException exception)
                {
                    throw new CompressionException($"« {name} » : {exception.Message}", exception);
                }
            }
            FlushImages();

            // Le document n'est plus consultable une fois enregistré : nombre de pages relevé avant
            int pageCount = output.PageCount;
            if (pageCount == 0)
                throw new CompressionException("Aucune page à fusionner.");

            output.Options.NoCompression = false;
            output.Options.CompressContentStreams = true;
            output.Options.UseFlateDecoderForJpegImages = PdfUseFlateDecoderForJpegImages.Never;
            using var stream = new MemoryStream();
            output.Save(stream, false);
            return (stream.ToArray(), pageCount);
        }
        finally
        {
            foreach (var bitmap in pendingImages)
                bitmap.Dispose();
            foreach (var source in sources)
                source.Dispose();
            output.Dispose();
        }
    }

    /// <summary>
    /// Ajoute les pages d'un PDF. Elles sont importées telles quelles (texte conservé) si possible,
    /// sinon converties en images (PDF chiffré ou de structure non gérée par PDFsharp).
    /// </summary>
    private static void AppendPdf(PdfDocument output, byte[] data, List<PdfDocument> sources, CancellationToken ct)
    {
        int pageCountBefore = output.PageCount;
        try
        {
            // Le document source doit rester ouvert jusqu'à l'enregistrement du résultat
            var source = PdfReader.Open(new MemoryStream(data), PdfDocumentOpenMode.Import);
            sources.Add(source);
            foreach (var page in source.Pages)
                output.AddPage(page);
            return;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Import partiel : on retire les pages déjà ajoutées avant de passer par les images
            while (output.PageCount > pageCountBefore)
                output.Pages.RemoveAt(output.PageCount - 1);
        }

        using var document = PdfiumDocument.Load(data);
        for (int i = 0; i < document.PageCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (pageWidth, pageHeight) = document.GetPageSize(i);
            var (width, height) = PdfRasterizer.PixelSize((pageWidth, pageHeight), FallbackDpi);
            using var bitmap = document.RenderPage(i, width, height);
            AddImagePage(output, pageWidth, pageHeight, [(bitmap, new PageBox(0, 0, pageWidth, pageHeight))]);
        }
    }

    /// <summary>
    /// Ajoute une page contenant les images indiquées, encodées en JPEG
    /// </summary>
    private static void AddImagePage(PdfDocument output, double pageWidth, double pageHeight,
        IReadOnlyList<(SKBitmap Bitmap, PageBox Box)> images)
    {
        var page = output.AddPage();
        page.MediaBox = new PdfRectangle(new XPoint(0, 0), new XSize(pageWidth, pageHeight));

        var xObjects = new PdfDictionary(output);
        var content = new StringBuilder();
        for (int i = 0; i < images.Count; i++)
        {
            var (bitmap, box) = images[i];

            // Inutile de garder plus de pixels que ImageDpi pour la taille affichée
            double maxLongSide = Math.Max(box.Width, box.Height) / 72.0 * ImageDpi;
            double scale = Math.Min(1.0, maxLongSide / Math.Max(bitmap.Width, bitmap.Height));
            var (width, height) = ImageOps.Scale(bitmap.Width, bitmap.Height, scale);
            using var scaled = scale < 1 ? ImageOps.Resize(bitmap, width, height) : null;
            bool gray = ImageOps.IsGrayscale(scaled ?? bitmap);
            using var grayBitmap = gray ? ImageOps.ToGray(scaled ?? bitmap) : null;

            var image = new PdfDictionary(output);
            image.Elements.SetName("/Type", "/XObject");
            image.Elements.SetName("/Subtype", "/Image");
            image.Elements.SetInteger("/Width", width);
            image.Elements.SetInteger("/Height", height);
            image.Elements.SetName("/ColorSpace", gray ? "/DeviceGray" : "/DeviceRGB");
            image.Elements.SetInteger("/BitsPerComponent", 8);
            image.Elements.SetName("/Filter", "/DCTDecode");
            image.CreateStream(ImageOps.EncodeJpeg(grayBitmap ?? scaled ?? bitmap, ImageQuality));
            output.Internals.AddObject(image);

            xObjects.Elements.SetReference($"/Im{i}", image);
            content.Append(string.Create(CultureInfo.InvariantCulture,
                $"q {box.Width:0.##} 0 0 {box.Height:0.##} {box.X:0.##} {box.Y:0.##} cm /Im{i} Do Q\n"));
        }

        page.Resources.Elements["/XObject"] = xObjects;
        page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes(content.ToString()));
    }
}
