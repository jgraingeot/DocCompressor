namespace DocCompressor.Core;

/// <summary>
/// Emplacement des fichiers compressés
/// </summary>
public enum OutputLocation
{
    /// <summary>Dans le dossier du fichier d'origine, avec un suffixe</summary>
    NextToOriginal,

    /// <summary>Dans un dossier choisi, sous le nom d'origine</summary>
    Folder,
}

/// <summary>
/// Paramètres d'enregistrement des fichiers compressés
/// </summary>
public sealed class OutputSettings
{
    public const string DefaultSuffix = "_compresse";

    public OutputLocation Location { get; init; } = OutputLocation.NextToOriginal;

    /// <summary>
    /// Dossier de destination (utilisé avec <see cref="OutputLocation.Folder"/>)
    /// </summary>
    public string? Folder { get; init; }

    /// <summary>
    /// Suffixe ajouté au nom quand le fichier est enregistré à côté de l'original
    /// </summary>
    public string Suffix { get; init; } = DefaultSuffix;
}

/// <summary>
/// Résultat de la compression d'un fichier sur disque
/// </summary>
public sealed record FileCompressionResult(
    string InputPath, string OutputPath, long OriginalSize, long FinalSize, CompressionMethod Method, string Details);

/// <summary>
/// Compression d'un fichier et enregistrement du résultat, sans jamais écraser l'original
/// </summary>
public static class FileCompressor
{
    public static FileCompressionResult CompressFile(
        string inputPath, CompressionOptions options, OutputSettings output, CancellationToken ct = default)
    {
        var data = DocumentCompressor.ReadFile(inputPath);
        var outcome = DocumentCompressor.Compress(data, Path.GetExtension(inputPath), options, ct);
        ct.ThrowIfCancellationRequested();

        var outputPath = BuildOutputPath(inputPath, outcome.Extension, output);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            using var stream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write);
            stream.Write(outcome.Data);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CompressionException("Impossible d'enregistrer le fichier : " + exception.Message, exception);
        }

        return new FileCompressionResult(inputPath, outputPath, data.Length, outcome.Data.Length, outcome.Method, outcome.Details);
    }

    /// <summary>
    /// Chemin du fichier produit ; un numéro est ajouté si le nom est déjà pris
    /// </summary>
    public static string BuildOutputPath(string inputPath, string extension, OutputSettings settings)
    {
        var inputFullPath = Path.GetFullPath(inputPath);
        var inputFolder = Path.GetDirectoryName(inputFullPath)!;
        var folder = settings.Location == OutputLocation.Folder && !string.IsNullOrWhiteSpace(settings.Folder)
            ? Path.GetFullPath(settings.Folder)
            : inputFolder;

        var name = Path.GetFileNameWithoutExtension(inputFullPath);
        // Dans le dossier de l'original, le suffixe évite toute confusion avec celui-ci
        if (settings.Location == OutputLocation.NextToOriginal || PathsEqual(folder, inputFolder))
            name += settings.Suffix;

        var candidate = Path.Combine(folder, name + extension);
        for (int i = 2; File.Exists(candidate) || PathsEqual(candidate, inputFullPath); i++)
            candidate = Path.Combine(folder, $"{name} ({i}){extension}");
        return candidate;
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b),
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
