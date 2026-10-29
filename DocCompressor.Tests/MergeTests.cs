using System.Text;
using DocCompressor.Core;
using DocCompressor.Core.Pdf;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using SkiaSharp;

namespace DocCompressor.Tests;

public sealed class MergeTests : IDisposable
{
    private static readonly CompressionOptions OneMegabyte = new();
    private readonly string _folder = Directory.CreateTempSubdirectory("doccompressor-merge-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    /// <summary>
    /// Photo d'une carte : aplat de couleur avec du grain (plusieurs Mo en JPEG)
    /// </summary>
    private static byte[] CardPhoto(SKColor color, int seed)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(4000, 2600, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(bitmap))
            canvas.Clear(color);
        TestFiles.AddNoise(bitmap, new Random(seed), 30);
        return TestFiles.Jpeg(bitmap, 95);
    }

    private static SKColor PixelAt(PdfiumDocument pdf, int page, double xFromLeft, double yFromTop)
    {
        var (width, height) = pdf.GetPageSize(page);
        using var rendered = pdf.RenderPage(page, (int)width, (int)height);
        return rendered.GetPixel((int)xFromLeft, (int)yFromTop);
    }

    private static bool IsRed(SKColor c) => c.Red > 180 && c.Blue < 100;

    private static bool IsBlue(SKColor c) => c.Blue > 180 && c.Red < 100;

    [Fact]
    public void RectoAndVersoBecomeOnePdfInTheGivenOrder()
    {
        var recto = CardPhoto(SKColors.Red, 1);
        var verso = CardPhoto(SKColors.Blue, 2);
        Assert.True(recto.Length + verso.Length > OneMegabyte.MaxBytes);

        var outcome = DocumentCompressor.Merge([("recto.jpg", recto), ("verso.jpg", verso)], OneMegabyte);

        Assert.Equal(".pdf", outcome.Extension);
        Assert.InRange(outcome.Data.Length, 1, OneMegabyte.MaxBytes);
        Assert.StartsWith("PDF fusionné de 2 pages", outcome.Details);
        using var pdf = PdfiumDocument.Load(outcome.Data);
        Assert.Equal(2, pdf.PageCount);
        // Photos en largeur : pages A4 paysage
        var (width, height) = pdf.GetPageSize(0);
        Assert.Equal(841.89, width, 0);
        Assert.Equal(595.28, height, 0);
        Assert.True(IsRed(PixelAt(pdf, 0, width / 2, height / 2)), "La page 1 doit être le recto");
        Assert.True(IsBlue(PixelAt(pdf, 1, width / 2, height / 2)), "La page 2 doit être le verso");
    }

    [Fact]
    public void TwoImagesPerPagePutsRectoAboveVerso()
    {
        var recto = CardPhoto(SKColors.Red, 3);
        var verso = CardPhoto(SKColors.Blue, 4);

        var outcome = DocumentCompressor.Merge([("recto.jpg", recto), ("verso.jpg", verso)], OneMegabyte,
            twoImagesPerPage: true);

        Assert.InRange(outcome.Data.Length, 1, OneMegabyte.MaxBytes);
        using var pdf = PdfiumDocument.Load(outcome.Data);
        Assert.Equal(1, pdf.PageCount);
        var (width, height) = pdf.GetPageSize(0);
        Assert.Equal(595.28, width, 0);
        Assert.Equal(841.89, height, 0);
        Assert.True(IsRed(PixelAt(pdf, 0, width / 2, height * 0.26)), "Le recto doit être en haut");
        Assert.True(IsBlue(PixelAt(pdf, 0, width / 2, height * 0.74)), "Le verso doit être en bas");
    }

    [Fact]
    public void PdfPagesAreImportedWithTheirText()
    {
        using var image = TestFiles.Photo(2400, 3200, seed: 4);
        var textPdf = TestFiles.TextAndFlateImagePdf(image, pngPredictor: false);
        var photo = CardPhoto(SKColors.Red, 5);

        var outcome = DocumentCompressor.Merge([("photo.jpg", photo), ("lettre.pdf", textPdf)], OneMegabyte);

        Assert.InRange(outcome.Data.Length, 1, OneMegabyte.MaxBytes);
        using var document = PdfReader.Open(new MemoryStream(outcome.Data), PdfDocumentOpenMode.Import);
        Assert.Equal(2, document.PageCount);
        var content = string.Concat(document.Pages[1].Contents.Elements
            .Select(item => item is PdfReference r ? r.Value : item)
            .OfType<PdfDictionary>()
            .Select(d => Encoding.ASCII.GetString(d.Stream.UnfilteredValue)));
        Assert.Contains("(Bonjour DocCompressor) Tj", content);
    }

    [Fact]
    public void ImagesArePairedAroundPdfDocuments()
    {
        var scan = TestFiles.ScannedPdf(pages: 3, dpi: 150, quality: 85);
        var a = CardPhoto(SKColors.Red, 6);
        var b = CardPhoto(SKColors.Blue, 7);
        var c = CardPhoto(SKColors.Red, 8);

        // a et b partagent une page, le PDF garde ses 3 pages, c est seule sur la dernière
        var outcome = DocumentCompressor.Merge(
            [("a.jpg", a), ("b.jpg", b), ("scan.pdf", scan), ("c.jpg", c)], OneMegabyte, twoImagesPerPage: true);

        Assert.InRange(outcome.Data.Length, 1, OneMegabyte.MaxBytes);
        using var pdf = PdfiumDocument.Load(outcome.Data);
        Assert.Equal(5, pdf.PageCount);
    }

    [Fact]
    public void ProtectedPdfIsMergedThroughImages()
    {
        var source = TestFiles.ScannedPdf(pages: 2, dpi: 150, quality: 85);
        var protectedPdf = Encrypt(source, userPassword: "", ownerPassword: "proprietaire");
        var photo = CardPhoto(SKColors.Blue, 9);

        var outcome = DocumentCompressor.Merge([("avis.pdf", protectedPdf), ("photo.jpg", photo)], OneMegabyte);

        Assert.InRange(outcome.Data.Length, 1, OneMegabyte.MaxBytes);
        using var pdf = PdfiumDocument.Load(outcome.Data);
        Assert.Equal(3, pdf.PageCount);
    }

    [Fact]
    public void PasswordProtectedPdfNamesTheFileInTheError()
    {
        var source = TestFiles.ScannedPdf(pages: 1, dpi: 100, quality: 80);
        var locked = Encrypt(source, userPassword: "secret", ownerPassword: "proprietaire");

        var exception = Assert.Throws<CompressionException>(() =>
            DocumentCompressor.Merge([("photo.jpg", CardPhoto(SKColors.Red, 10)), ("avis.pdf", locked)], OneMegabyte));

        Assert.Contains("« avis.pdf »", exception.Message);
        Assert.Contains("mot de passe", exception.Message);
    }

    [Fact]
    public void UnreadableFileNamesTheFileInTheError()
    {
        var exception = Assert.Throws<CompressionException>(() =>
            DocumentCompressor.Merge([("IMG_0042.HEIC", new byte[5000])], OneMegabyte));

        Assert.Contains("« IMG_0042.HEIC »", exception.Message);
    }

    [Fact]
    public void MergeFilesWritesNextToTheFirstFile()
    {
        var recto = Path.Combine(_folder, "carte_identite_recto.jpg");
        var verso = Path.Combine(_folder, "carte_identite_verso.jpg");
        File.WriteAllBytes(recto, CardPhoto(SKColors.Red, 11));
        File.WriteAllBytes(verso, CardPhoto(SKColors.Blue, 12));
        var name = FileCompressor.SuggestMergedName([recto, verso]);

        var first = FileCompressor.MergeFiles([recto, verso], name, OneMegabyte, new OutputSettings());
        var second = FileCompressor.MergeFiles([recto, verso], name, OneMegabyte, new OutputSettings());

        Assert.Equal(Path.Combine(_folder, "carte_identite.pdf"), first.OutputPath);
        Assert.Equal(Path.Combine(_folder, "carte_identite (2).pdf"), second.OutputPath);
        Assert.True(first.FinalSize <= OneMegabyte.MaxBytes);
        Assert.Equal(new FileInfo(recto).Length + new FileInfo(verso).Length, first.OriginalSize);
    }

    [Fact]
    public void MergeFilesCanWriteToAFolder()
    {
        var recto = Path.Combine(_folder, "recto.jpg");
        File.WriteAllBytes(recto, CardPhoto(SKColors.Red, 13));
        var target = Path.Combine(_folder, "envoi");

        var result = FileCompressor.MergeFiles([recto], "Carte d'identité.pdf", OneMegabyte,
            new OutputSettings { Location = OutputLocation.Folder, Folder = target });

        Assert.Equal(Path.Combine(target, "Carte d'identité.pdf"), result.OutputPath);
        Assert.True(File.Exists(result.OutputPath));
    }

    [Theory]
    [InlineData("carte_identite_recto.jpg", "carte_identite_verso.jpg", "carte_identite")]
    [InlineData("IMG_20261029_101512.jpg", "IMG_20261029_101530.jpg", "IMG_20261029")]
    [InlineData("CNI Recto.png", "cni verso.png", "CNI")]
    [InlineData("recto.jpg", "verso.jpg", "recto_fusion")]
    public void MergedNameUsesTheCommonPart(string first, string second, string expected)
    {
        Assert.Equal(expected, FileCompressor.SuggestMergedName([first, second]));
    }

    [Fact]
    public void MergedNameForASingleFile()
    {
        Assert.Equal("scan_fusion", FileCompressor.SuggestMergedName(["scan.pdf"]));
    }

    [Theory]
    [InlineData("carte: recto/verso", "carte_ recto_verso")]
    [InlineData("dossier CAF.pdf", "dossier CAF")]
    [InlineData("  ", "documents")]
    public void FileNamesAreSanitized(string name, string expected)
    {
        Assert.Equal(expected, FileCompressor.SanitizeFileName(name));
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
