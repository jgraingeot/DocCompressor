using DocCompressor.Core;

namespace DocCompressor.Tests;

public sealed class FileCompressorTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("doccompressor-tests-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void OutputNextToOriginalGetsASuffix()
    {
        var input = Path.Combine(_folder, "carte identité.png");

        var output = FileCompressor.BuildOutputPath(input, ".jpg", new OutputSettings());

        Assert.Equal(Path.Combine(_folder, "carte identité_compresse.jpg"), output);
    }

    [Fact]
    public void OutputInFolderKeepsTheOriginalName()
    {
        var input = Path.Combine(_folder, "scan.pdf");
        var target = Path.Combine(_folder, "envoi");

        var output = FileCompressor.BuildOutputPath(input, ".pdf",
            new OutputSettings { Location = OutputLocation.Folder, Folder = target });

        Assert.Equal(Path.Combine(target, "scan.pdf"), output);
    }

    [Fact]
    public void OutputInTheOriginalFolderNeverOverwritesTheOriginal()
    {
        var input = Path.Combine(_folder, "scan.pdf");
        File.WriteAllText(input, "original");

        var output = FileCompressor.BuildOutputPath(input, ".pdf",
            new OutputSettings { Location = OutputLocation.Folder, Folder = _folder });

        Assert.Equal(Path.Combine(_folder, "scan_compresse.pdf"), output);
    }

    [Fact]
    public void ExistingFilesAreNotOverwritten()
    {
        var input = Path.Combine(_folder, "photo.jpg");
        File.WriteAllText(Path.Combine(_folder, "photo_compresse.jpg"), "déjà là");
        File.WriteAllText(Path.Combine(_folder, "photo_compresse (2).jpg"), "déjà là");

        var output = FileCompressor.BuildOutputPath(input, ".jpg", new OutputSettings());

        Assert.Equal(Path.Combine(_folder, "photo_compresse (3).jpg"), output);
    }

    [Fact]
    public void CompressFileWritesTheResultAndKeepsTheOriginal()
    {
        using var photo = TestFiles.Photo(3000, 2000);
        var bytes = TestFiles.Jpeg(photo, 100);
        var input = Path.Combine(_folder, "photo.jpg");
        File.WriteAllBytes(input, bytes);

        var result = FileCompressor.CompressFile(input, new CompressionOptions(), new OutputSettings());

        Assert.Equal(bytes, File.ReadAllBytes(input));
        Assert.True(File.Exists(result.OutputPath));
        Assert.Equal(result.FinalSize, new FileInfo(result.OutputPath).Length);
        Assert.True(result.FinalSize <= 1_000_000);
        Assert.Equal(bytes.Length, result.OriginalSize);
    }

    [Fact]
    public void MissingFileGivesAReadableError()
    {
        var input = Path.Combine(_folder, "disparu.pdf");

        var exception = Assert.Throws<CompressionException>(() =>
            FileCompressor.CompressFile(input, new CompressionOptions(), new OutputSettings()));

        Assert.Contains("n'existe plus", exception.Message);
    }

    [Theory]
    [InlineData(999, "999 o")]
    [InlineData(850_000, "850 Ko")]
    [InlineData(1_000_000, "1 Mo")]
    [InlineData(1_250_000, "1,25 Mo")]
    public void SizesAreFormattedInFrench(long bytes, string expected)
    {
        Assert.Equal(expected, SizeFormatter.Format(bytes));
    }
}
