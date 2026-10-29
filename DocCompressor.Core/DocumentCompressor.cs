using DocCompressor.Core.Imaging;
using DocCompressor.Core.Pdf;

namespace DocCompressor.Core;

/// <summary>
/// Point d'entrée de la compression d'un document (image ou PDF)
/// </summary>
public static class DocumentCompressor
{
    /// <summary>
    /// Taille maximale minimale acceptée : en dessous, aucun document ne resterait lisible
    /// </summary>
    public const long MinimumMaxBytes = 20_000;

    /// <summary>
    /// Extensions de fichier prises en charge
    /// </summary>
    public static IReadOnlyList<string> SupportedExtensions { get; } =
        [".pdf", ".jpg", ".jpeg", ".jfif", ".png", ".webp", ".bmp", ".gif"];

    public static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>
    /// Compresse un fichier ; le résultat est retourné en mémoire
    /// </summary>
    public static CompressionOutcome Compress(string inputPath, CompressionOptions options, CancellationToken ct = default) =>
        Compress(ReadFile(inputPath), Path.GetExtension(inputPath), options, ct);

    /// <summary>
    /// Lit un fichier en mémoire ; les erreurs sont traduites en message compréhensible
    /// </summary>
    public static byte[] ReadFile(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new CompressionException("Le fichier n'existe plus (déplacé ou supprimé).", exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CompressionException("Impossible de lire le fichier : " + exception.Message, exception);
        }
    }

    /// <summary>
    /// Compresse le contenu d'un fichier ; l'extension d'origine est conservée si le fichier est inchangé
    /// </summary>
    public static CompressionOutcome Compress(byte[] data, string extension, CompressionOptions options, CancellationToken ct = default)
    {
        CheckMaxBytes(options);
        if (data.Length == 0)
            throw new CompressionException("Le fichier est vide.");

        return IsPdf(data)
            ? PdfCompressor.Compress(data, extension, options, ct)
            : ImageCompressor.Compress(data, extension, options, ct);
    }

    /// <summary>
    /// Fusionne des documents (images et PDF), dans l'ordre donné, en un seul PDF sous la taille maximale.
    /// Les pages des PDF sont reprises telles quelles ; chaque image occupe une page A4,
    /// ou une demi-page si twoImagesPerPage est vrai (recto et verso d'une carte sur la même page).
    /// La méthode retournée est celle de la compression du PDF assemblé (Unchanged s'il tenait déjà dans la limite).
    /// </summary>
    public static CompressionOutcome Merge(
        IReadOnlyList<(string Name, byte[] Data)> documents, CompressionOptions options,
        bool twoImagesPerPage = false, CancellationToken ct = default)
    {
        CheckMaxBytes(options);
        if (documents.Count == 0)
            throw new CompressionException("Aucun fichier à fusionner.");
        var empty = documents.FirstOrDefault(d => d.Data.Length == 0);
        if (empty.Data != null)
            throw new CompressionException($"« {empty.Name} » : le fichier est vide.");

        var (pdf, pageCount) = DocumentMerger.Build(documents, twoImagesPerPage, ct);
        var outcome = PdfCompressor.Compress(pdf, ".pdf", options, ct);

        string summary = $"PDF fusionné de {(pageCount > 1 ? $"{pageCount} pages" : "1 page")}";
        return outcome with
        {
            Details = outcome.Method == CompressionMethod.Unchanged ? summary : $"{summary}. {outcome.Details}",
        };
    }

    private static void CheckMaxBytes(CompressionOptions options)
    {
        if (options.MaxBytes < MinimumMaxBytes)
            throw new CompressionException($"La taille maximale doit être d'au moins {SizeFormatter.Format(MinimumMaxBytes)}.");
    }

    internal static bool IsPdf(byte[] data)
    {
        // L'en-tête %PDF- peut être précédé de quelques octets parasites
        var header = "%PDF-"u8;
        int limit = Math.Min(data.Length, 1024);
        return data.AsSpan(0, limit).IndexOf(header) >= 0;
    }
}
