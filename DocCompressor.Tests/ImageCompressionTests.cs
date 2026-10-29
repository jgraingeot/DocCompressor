using DocCompressor.Core;
using DocCompressor.Core.Pdf;
using SkiaSharp;

namespace DocCompressor.Tests;

public class ImageCompressionTests
{
    private static readonly CompressionOptions OneMegabyte = new();

    [Fact]
    public void LargePhotoIsReencodedUnderTheLimit()
    {
        using var photo = TestFiles.Photo(4000, 3000);
        var input = TestFiles.Jpeg(photo, 98);
        Assert.True(input.Length > OneMegabyte.MaxBytes);

        var outcome = DocumentCompressor.Compress(input, ".jpg", OneMegabyte);

        Assert.Equal(CompressionMethod.ImageReencoded, outcome.Method);
        Assert.Equal(".jpg", outcome.Extension);
        Assert.InRange(outcome.Data.Length, 1, OneMegabyte.MaxBytes);
        using var decoded = SKBitmap.Decode(outcome.Data);
        Assert.NotNull(decoded);
        // L'image doit rester bien définie (la réduction ne doit pas être excessive)
        Assert.True(decoded.Width >= 1600, $"Largeur obtenue : {decoded.Width}");
        Assert.Equal(4000.0 / 3000.0, (double)decoded.Width / decoded.Height, 2);
    }

    [Fact]
    public void LargePngBecomesJpegUnderTheLimit()
    {
        using var photo = TestFiles.Photo(2500, 1800, seed: 3);
        var input = TestFiles.Png(photo);
        Assert.True(input.Length > OneMegabyte.MaxBytes);

        var outcome = DocumentCompressor.Compress(input, ".png", OneMegabyte);

        Assert.Equal(".jpg", outcome.Extension);
        Assert.InRange(outcome.Data.Length, 1, OneMegabyte.MaxBytes);
    }

    [Fact]
    public void SmallJpegIsKeptAsIs()
    {
        using var photo = TestFiles.Photo(800, 600);
        var input = TestFiles.Jpeg(photo, 80);

        var outcome = DocumentCompressor.Compress(input, ".JPG", OneMegabyte);

        Assert.Equal(CompressionMethod.Unchanged, outcome.Method);
        Assert.Equal(".JPG", outcome.Extension);
        Assert.Same(input, outcome.Data);
    }

    [Theory]
    [InlineData(300_000)]
    [InlineData(100_000)]
    [InlineData(2_000_000)]
    public void CustomLimitIsRespected(long maxBytes)
    {
        using var photo = TestFiles.Photo(4000, 3000, seed: 5);
        var input = TestFiles.Jpeg(photo, 100);

        var outcome = DocumentCompressor.Compress(input, ".jpg", new CompressionOptions { MaxBytes = maxBytes });

        Assert.InRange(outcome.Data.Length, 1, maxBytes);
    }

    [Fact]
    public void ExifOrientationIsApplied()
    {
        // Moitié gauche rouge, moitié droite bleue ; l'EXIF demande une rotation de 90° horaire
        using var bitmap = new SKBitmap(new SKImageInfo(1200, 600, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Blue);
            canvas.DrawRect(0, 0, 600, 600, new SKPaint { Color = SKColors.Red });
        }
        TestFiles.AddNoise(bitmap, new Random(1), 30);
        var input = TestFiles.WithExifOrientation(TestFiles.Jpeg(bitmap, 95), orientation: 6);

        // Limite inférieure à la taille d'entrée pour forcer le réencodage
        var outcome = DocumentCompressor.Compress(input, ".jpg", new CompressionOptions { MaxBytes = input.Length - 1 });

        using var decoded = SKBitmap.Decode(outcome.Data);
        Assert.Equal(600, decoded.Width);
        Assert.Equal(1200, decoded.Height);
        var top = decoded.GetPixel(300, 100);
        var bottom = decoded.GetPixel(300, 1100);
        Assert.True(top.Red > 200 && top.Blue < 60, $"Haut : {top}");
        Assert.True(bottom.Blue > 200 && bottom.Red < 60, $"Bas : {bottom}");
    }

    [Fact]
    public void BlackAndWhiteScanIsEncodedInGrayscale()
    {
        using var scan = TestFiles.ScannedText(2480, 3508);
        var input = TestFiles.Jpeg(scan, 100);
        Assert.True(input.Length > OneMegabyte.MaxBytes);

        var outcome = DocumentCompressor.Compress(input, ".jpg", OneMegabyte);

        Assert.InRange(outcome.Data.Length, 1, OneMegabyte.MaxBytes);
        Assert.Contains("niveaux de gris", outcome.Details);
        using var codec = SKCodec.Create(new SKMemoryStream(outcome.Data));
        Assert.Equal(SKColorType.Gray8, codec.Info.ColorType);
    }

    [Fact]
    public void ColorPhotoStaysInColor()
    {
        using var photo = TestFiles.Photo(3000, 2000, seed: 9);
        var input = TestFiles.Jpeg(photo, 100);

        var outcome = DocumentCompressor.Compress(input, ".jpg", OneMegabyte);

        Assert.DoesNotContain("niveaux de gris", outcome.Details);
    }

    [Fact]
    public void TransparencyBecomesWhite()
    {
        // Photo au centre d'un fond transparent
        using var photo = TestFiles.Photo(1200, 1200);
        using var bitmap = new SKBitmap(new SKImageInfo(1600, 1600, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(photo, 200, 200);
        }
        var input = TestFiles.Png(bitmap);
        Assert.True(input.Length > OneMegabyte.MaxBytes);

        var outcome = DocumentCompressor.Compress(input, ".png", OneMegabyte);

        using var decoded = SKBitmap.Decode(outcome.Data);
        var corner = decoded.GetPixel(5, 5);
        Assert.True(corner.Red > 240 && corner.Green > 240 && corner.Blue > 240, $"Coin : {corner}");
    }

    [Fact]
    public void ImageCanBeConvertedToPdf()
    {
        using var photo = TestFiles.Photo(4000, 3000, seed: 2);
        var input = TestFiles.Jpeg(photo, 95);

        var outcome = DocumentCompressor.Compress(input, ".jpg", new CompressionOptions { ConvertImagesToPdf = true });

        Assert.Equal(CompressionMethod.ImageToPdf, outcome.Method);
        Assert.Equal(".pdf", outcome.Extension);
        Assert.InRange(outcome.Data.Length, 1, OneMegabyte.MaxBytes);
        using var pdf = PdfiumDocument.Load(outcome.Data);
        Assert.Equal(1, pdf.PageCount);
        var (width, height) = pdf.GetPageSize(0);
        Assert.Equal(841.89, width, 0); // A4 paysage pour une photo en largeur
        Assert.Equal(595.28, height, 0);
    }

    [Fact]
    public void SmallImageIsStillConvertedToPdfWhenRequested()
    {
        using var photo = TestFiles.Photo(800, 1000);
        var input = TestFiles.Jpeg(photo, 80);

        var outcome = DocumentCompressor.Compress(input, ".jpg", new CompressionOptions { ConvertImagesToPdf = true });

        Assert.Equal(".pdf", outcome.Extension);
        using var pdf = PdfiumDocument.Load(outcome.Data);
        var (width, height) = pdf.GetPageSize(0);
        Assert.True(height > width, "Page A4 portrait attendue");
    }

    [Fact]
    public void UnknownFormatGivesAReadableError()
    {
        var input = "ceci n'est pas une image"u8.ToArray();

        var exception = Assert.Throws<CompressionException>(() => DocumentCompressor.Compress(input, ".jpg", OneMegabyte));

        Assert.Contains("non reconnu", exception.Message);
    }

    [Fact]
    public void TooSmallLimitIsRejected()
    {
        using var photo = TestFiles.Photo(400, 300);
        var input = TestFiles.Jpeg(photo, 80);

        Assert.Throws<CompressionException>(() =>
            DocumentCompressor.Compress(input, ".jpg", new CompressionOptions { MaxBytes = 5_000 }));
    }
}
