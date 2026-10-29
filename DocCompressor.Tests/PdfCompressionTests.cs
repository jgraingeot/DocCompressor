using System.Text;
using DocCompressor.Core;
using DocCompressor.Core.Pdf;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SkiaSharp;

namespace DocCompressor.Tests;

public class PdfCompressionTests
{
    private static readonly CompressionOptions OneMegabyte = new();

    [Fact]
    public void ScannedPdfImagesAreRecompressed()
    {
        var input = TestFiles.ScannedPdf(pages: 4, dpi: 300, quality: 95);
        Assert.True(input.Length > OneMegabyte.MaxBytes, $"Taille d'entrée : {input.Length}");

        var outcome = DocumentCompressor.Compress(input, ".pdf", OneMegabyte);

        Assert.Equal(CompressionMethod.PdfImagesRecompressed, outcome.Method);
        Assert.InRange(outcome.Data.Length, 1, OneMegabyte.MaxBytes);
        using var pdf = PdfiumDocument.Load(outcome.Data);
        Assert.Equal(4, pdf.PageCount);
    }

    [Fact]
    public void ColorScannedPdfFitsUnderTheLimit()
    {
        var input = TestFiles.ScannedPdf(pages: 3, dpi: 200, quality: 92, photo: true);
        Assert.True(input.Length > OneMegabyte.MaxBytes, $"Taille d'entrée : {input.Length}");

        var outcome = DocumentCompressor.Compress(input, ".pdf", OneMegabyte);

        Assert.InRange(outcome.Data.Length, 1, OneMegabyte.MaxBytes);
        using var pdf = PdfiumDocument.Load(outcome.Data);
        Assert.Equal(3, pdf.PageCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextIsPreservedWhenOnlyImagesAreRecompressed(bool pngPredictor)
    {
        // Moitié gauche rouge, moitié droite bleue, avec du grain pour que l'image pèse lourd
        using var image = TestFiles.Photo(2400, 3200, seed: 4);
        using (var canvas = new SKCanvas(image))
        {
            canvas.DrawRect(0, 0, 1200, 400, new SKPaint { Color = SKColors.Red });
            canvas.DrawRect(1200, 0, 1200, 400, new SKPaint { Color = SKColors.Blue });
        }
        var input = TestFiles.TextAndFlateImagePdf(image, pngPredictor);
        Assert.True(input.Length > OneMegabyte.MaxBytes, $"Taille d'entrée : {input.Length}");

        var outcome = DocumentCompressor.Compress(input, ".pdf", OneMegabyte);

        Assert.Equal(CompressionMethod.PdfImagesRecompressed, outcome.Method);
        Assert.InRange(outcome.Data.Length, 1, OneMegabyte.MaxBytes);

        // Le texte est toujours présent dans le flux de contenu de la page
        using var document = PdfReader.Open(new MemoryStream(outcome.Data), PdfDocumentOpenMode.Import);
        var content = string.Concat(document.Pages[0].Contents.Elements
            .Select(item => item is PdfSharp.Pdf.Advanced.PdfReference r ? r.Value : item)
            .OfType<PdfDictionary>()
            .Select(d => Encoding.ASCII.GetString(d.Stream.UnfilteredValue)));
        Assert.Contains("(Bonjour DocCompressor) Tj", content);

        // Les couleurs de l'image sont correctes après décodage Flate (et prédicteur PNG)
        using var pdf = PdfiumDocument.Load(outcome.Data);
        var (pageWidth, pageHeight) = pdf.GetPageSize(0);
        using var rendered = pdf.RenderPage(0, (int)pageWidth, (int)pageHeight);
        // Bande colorée en haut de l'image : de y=120+600-75 à 720 (repère PDF), soit ~y=130 en pixels depuis le haut
        var left = rendered.GetPixel(72 + 100, (int)pageHeight - 700);
        var right = rendered.GetPixel(72 + 350, (int)pageHeight - 700);
        Assert.True(left.Red > 180 && left.Blue < 90, $"Gauche : {left}");
        Assert.True(right.Blue > 180 && right.Red < 90, $"Droite : {right}");
    }

    [Fact]
    public void VectorHeavyPdfIsRasterized()
    {
        var input = TestFiles.HeavyVectorPdf(pages: 2);
        Assert.True(input.Length > OneMegabyte.MaxBytes, $"Taille d'entrée : {input.Length}");

        var outcome = DocumentCompressor.Compress(input, ".pdf", OneMegabyte);

        Assert.Equal(CompressionMethod.PdfRasterized, outcome.Method);
        Assert.InRange(outcome.Data.Length, 1, OneMegabyte.MaxBytes);
        using var pdf = PdfiumDocument.Load(outcome.Data);
        Assert.Equal(2, pdf.PageCount);
        var (width, height) = pdf.GetPageSize(0);
        Assert.Equal(595, width, 0);
        Assert.Equal(842, height, 0);
    }

    [Fact]
    public void SmallPdfIsKeptAsIs()
    {
        var input = TestFiles.ScannedPdf(pages: 1, dpi: 72, quality: 70);
        Assert.True(input.Length < OneMegabyte.MaxBytes);

        var outcome = DocumentCompressor.Compress(input, ".pdf", OneMegabyte);

        Assert.Equal(CompressionMethod.Unchanged, outcome.Method);
        Assert.Same(input, outcome.Data);
    }

    [Fact]
    public void ProtectedPdfWithoutOpenPasswordIsCompressed()
    {
        // Protégé contre la modification (mot de passe propriétaire) mais lisible sans mot de passe
        var source = TestFiles.ScannedPdf(pages: 2, dpi: 300, quality: 95);
        var input = Encrypt(source, userPassword: "", ownerPassword: "proprietaire");

        var outcome = DocumentCompressor.Compress(input, ".pdf", OneMegabyte);

        Assert.InRange(outcome.Data.Length, 1, OneMegabyte.MaxBytes);
        using var pdf = PdfiumDocument.Load(outcome.Data);
        Assert.Equal(2, pdf.PageCount);
    }

    [Fact]
    public void PasswordProtectedPdfGivesAReadableError()
    {
        var source = TestFiles.ScannedPdf(pages: 2, dpi: 300, quality: 95);
        var input = Encrypt(source, userPassword: "secret", ownerPassword: "proprietaire");

        var exception = Assert.Throws<CompressionException>(() => DocumentCompressor.Compress(input, ".pdf", OneMegabyte));

        Assert.Contains("mot de passe", exception.Message);
    }

    [Fact]
    public void ImagePdfWriterOverheadEstimateIsAnUpperBound()
    {
        var pages = Enumerable.Range(0, 12)
            .Select(_ => new ImagePage(new byte[1000], 10000, 10000, false, 2000.5, 3000.25, new PageBox(10.5, 10.5, 1980, 2980)))
            .ToList();

        var pdf = ImagePdfWriter.Write(pages);

        Assert.True(pdf.Length <= 12 * 1000 + ImagePdfWriter.EstimateOverhead(12));
    }

    private static byte[] Encrypt(byte[] pdf, string userPassword, string ownerPassword)
    {
        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Modify);
        document.SecuritySettings.UserPassword = userPassword;
        document.SecuritySettings.OwnerPassword = ownerPassword;
        document.SecuritySettings.PermitModifyDocument = false;
        using var output = new MemoryStream();
        document.Save(output, false);
        return output.ToArray();
    }
}
