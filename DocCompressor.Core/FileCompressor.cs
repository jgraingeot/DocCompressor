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
/// Résultat de la compression d'un fichier sur disque (pour une fusion, InputPath est le premier fichier)
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
        WriteNewFile(outputPath, outcome.Data);
        return new FileCompressionResult(inputPath, outputPath, data.Length, outcome.Data.Length, outcome.Method, outcome.Details);
    }

    /// <summary>
    /// Fusionne des fichiers, dans l'ordre donné, en un seul PDF nommé outputName.
    /// Il est enregistré dans le dossier choisi, ou à défaut dans celui du premier fichier.
    /// </summary>
    public static FileCompressionResult MergeFiles(
        IReadOnlyList<string> inputPaths, string outputName, CompressionOptions options, OutputSettings output,
        bool twoImagesPerPage = false, CancellationToken ct = default)
    {
        if (inputPaths.Count == 0)
            throw new CompressionException("Aucun fichier à fusionner.");

        var documents = inputPaths.Select(path => (Path.GetFileName(path), DocumentCompressor.ReadFile(path))).ToList();
        var outcome = DocumentCompressor.Merge(documents, options, twoImagesPerPage, ct);
        ct.ThrowIfCancellationRequested();

        var folder = output.Location == OutputLocation.Folder && !string.IsNullOrWhiteSpace(output.Folder)
            ? Path.GetFullPath(output.Folder)
            : Path.GetDirectoryName(Path.GetFullPath(inputPaths[0]))!;
        var outputPath = UniquePath(folder, SanitizeFileName(outputName), ".pdf", inputPaths.Select(Path.GetFullPath));
        WriteNewFile(outputPath, outcome.Data);

        return new FileCompressionResult(inputPaths[0], outputPath, documents.Sum(d => (long)d.Item2.Length),
            outcome.Data.Length, outcome.Method, outcome.Details);
    }

    /// <summary>
    /// Nom proposé pour un PDF fusionné : la partie commune des noms (« carte_identite_recto » et
    /// « carte_identite_verso » donnent « carte_identite »), sinon le premier nom suivi de « _fusion »
    /// </summary>
    public static string SuggestMergedName(IReadOnlyList<string> inputPaths)
    {
        var names = inputPaths.Select(Path.GetFileNameWithoutExtension).OfType<string>().ToList();
        if (names.Count == 0)
            return "documents";

        var first = names[0];
        int length = first.Length;
        foreach (var name in names.Skip(1))
        {
            int i = 0;
            while (i < length && i < name.Length && char.ToLowerInvariant(first[i]) == char.ToLowerInvariant(name[i]))
                i++;
            length = i;
        }

        // On ne coupe pas au milieu d'un mot ou d'un nombre (« IMG_20261029_1015 » devient « IMG_20261029 »)
        if (length < first.Length && length > 0 && char.IsLetterOrDigit(first[length]) && char.IsLetterOrDigit(first[length - 1]))
        {
            while (length > 0 && char.IsLetterOrDigit(first[length - 1]))
                length--;
        }

        var common = first[..length].TrimEnd(' ', '_', '-', '.', '(');
        return names.Count > 1 && common.Length >= 3 ? common : first + "_fusion";
    }

    /// <summary>
    /// Nom de fichier valide sous Windows comme sous Linux, sans extension .pdf
    /// </summary>
    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat("<>:\"/\\|?*").ToHashSet();
        var cleaned = new string(name.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim();
        if (cleaned.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[..^4];
        cleaned = cleaned.Trim().TrimEnd('.');
        return cleaned.Length > 0 ? cleaned : "documents";
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

        return UniquePath(folder, name, extension, [inputFullPath]);
    }

    /// <summary>
    /// Chemin libre : un numéro est ajouté si le nom est déjà pris ou désigne un fichier d'origine
    /// </summary>
    private static string UniquePath(string folder, string name, string extension, IEnumerable<string> reserved)
    {
        var reservedPaths = reserved.ToList();
        var candidate = Path.Combine(folder, name + extension);
        for (int i = 2; File.Exists(candidate) || reservedPaths.Any(path => PathsEqual(candidate, path)); i++)
            candidate = Path.Combine(folder, $"{name} ({i}){extension}");
        return candidate;
    }

    /// <summary>
    /// Écrit un nouveau fichier (jamais d'écrasement)
    /// </summary>
    private static void WriteNewFile(string path, byte[] data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            stream.Write(data);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CompressionException("Impossible d'enregistrer le fichier : " + exception.Message, exception);
        }
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b),
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
