using DocCompressor.Core;
using DocCompressor.Core.Imaging;
using DocCompressor.Core.Pdf;
using SkiaSharp;

namespace DocCompressor.Tests;

public class WatermarkTests
{
    private const string Text = "Document destiné exclusivement à l'agence Dupont – le 29/10/2026";
    private static readonly CompressionOptions WithWatermark = new() { WatermarkText = Text };

    /// <summary>
    /// Nombre de pixels nettement plus sombres que le fond blanc dans une zone de l'image
    /// </summary>
    private static int DarkPixels(SKBitmap bitmap, SKRectI area)
    {
        int count = 0;
        for (int y = area.Top; y < area.Bottom; y += 2)
            for (int x = area.Left; x < area.Right; x += 2)
            {
                var c = bitmap.GetPixel(x, y);
                if ((c.Red + c.Green + c.Blue) / 3 < 210)
                    count++;
            }
        return count;
    }

    private static SKBitmap WhitePage(int width, int height)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(bitmap))
            canvas.Clear(new SKColor(250, 250, 250));
        TestFiles.AddNoise(bitmap, new Random(1), 4, neutral: true);
        return bitmap;
    }

    [Fact]
    public void EmbeddedFontCoversFrenchText()
    {
        Assert.Equal("Inter", Watermark.Typeface.FamilyName);
        using var font = new SKFont(Watermark.Typeface);
        Assert.True(font.ContainsGlyphs("àâçéèêëîïôùûüÿœæÀÉÈÇ«»–’€"));
    }

    [Fact]
    public void WatermarkCoversTheWholeImage()
    {
        using var page = WhitePage(1800, 1200);
        var input = TestFiles.Jpeg(page, 90);

        var outcome = DocumentCompressor.Compress(input, ".jpg", WithWatermark);

        // Même un petit fichier est réencodé pour recevoir le filigrane
        Assert.Equal(CompressionMethod.ImageReencoded, outcome.Method);
        Assert.Contains("filigrane", outcome.Details);
        using var decoded = SKBitmap.Decode(outcome.Data);
        int cellWidth = decoded.Width / 3, cellHeight = decoded.Height / 3;
        for (int row = 0; row < 3; row++)
            for (int column = 0; column < 3; column++)
            {
                var cell = SKRectI.Create(column * cellWidth, row * cellHeight, cellWidth, cellHeight);
                Assert.True(DarkPixels(decoded, cell) > 50, $"Pas de filigrane dans la zone {row},{column}");
            }
    }

    [Fact]
    public void BlankWatermarkTextMeansNoWatermark()
    {
        using var page = WhitePage(800, 600);
        var input = TestFiles.Jpeg(page, 90);

        var outcome = DocumentCompressor.Compress(input, ".jpg", new CompressionOptions { WatermarkText = "   " });

        Assert.Equal(CompressionMethod.Unchanged, outcome.Method);
    }

    [Fact]
    public void BlackAndWhiteScanStaysGrayWithWatermark()
    {
        using var scan = TestFiles.ScannedText(1700, 2400);
        var input = TestFiles.Jpeg(scan, 95);

        var outcome = DocumentCompressor.Compress(input, ".jpg", WithWatermark);

        using var codec = SKCodec.Create(new SKMemoryStream(outcome.Data));
        Assert.Equal(SKColorType.Gray8, codec.Info.ColorType);
    }

    [Fact]
    public void SmallPdfIsFlattenedWithTheWatermark()
    {
        var input = TestFiles.ScannedPdf(pages: 2, dpi: 100, quality: 80);
        Assert.True(input.Length < WithWatermark.MaxBytes);

        var outcome = DocumentCompressor.Compress(input, ".pdf", WithWatermark);

        Assert.Equal(CompressionMethod.PdfRasterized, outcome.Method);
        Assert.StartsWith("Filigrane ajouté", outcome.Details);
        Assert.InRange(outcome.Data.Length, 1, WithWatermark.MaxBytes);
        using var pdf = PdfiumDocument.Load(outcome.Data);
        Assert.Equal(2, pdf.PageCount);
        // Le filigrane apparaît jusque dans la marge gauche, blanche sur l'original
        var (width, height) = pdf.GetPageSize(1);
        using var rendered = pdf.RenderPage(1, (int)width * 2, (int)height * 2);
        var margin = SKRectI.Create(0, 0, rendered.Width / 13, rendered.Height);
        Assert.True(DarkPixels(rendered, margin) > 100);
    }

    [Fact]
    public void MergedPdfGetsTheWatermarkOnEveryPage()
    {
        using var recto = TestFiles.Photo(3000, 1900, seed: 1);
        using var verso = TestFiles.Photo(3000, 1900, seed: 2);

        var outcome = DocumentCompressor.Merge(
            [("recto.jpg", TestFiles.Jpeg(recto, 95)), ("verso.jpg", TestFiles.Jpeg(verso, 95)),
             ("avis.pdf", TestFiles.ScannedPdf(pages: 1, dpi: 100, quality: 80))],
            WithWatermark, twoImagesPerPage: true);

        Assert.Equal(CompressionMethod.PdfRasterized, outcome.Method);
        Assert.Contains("Filigrane ajouté", outcome.Details);
        Assert.InRange(outcome.Data.Length, 1, WithWatermark.MaxBytes);
        using var pdf = PdfiumDocument.Load(outcome.Data);
        Assert.Equal(2, pdf.PageCount);
    }

    [Theory]
    [InlineData("  Pour l'agence Dupont  ", true, "Pour l'agence Dupont – le 29/10/2026")]
    [InlineData("Pour l'agence Dupont", false, "Pour l'agence Dupont")]
    [InlineData("   ", true, "")]
    public void TextIsComposedWithTheDate(string text, bool addDate, string expected)
    {
        Assert.Equal(expected, WatermarkText.Compose(text, addDate, new DateTime(2026, 10, 29)));
    }
}
