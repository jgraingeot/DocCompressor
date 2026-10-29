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
        if (options.MaxBytes < MinimumMaxBytes)
            throw new CompressionException($"La taille maximale doit être d'au moins {SizeFormatter.Format(MinimumMaxBytes)}.");
        if (data.Length == 0)
            throw new CompressionException("Le fichier est vide.");

        return IsPdf(data)
            ? PdfCompressor.Compress(data, extension, options, ct)
            : ImageCompressor.Compress(data, extension, options, ct);
    }

    private static bool IsPdf(byte[] data)
    {
        // L'en-tête %PDF- peut être précédé de quelques octets parasites
        var header = "%PDF-"u8;
        int limit = Math.Min(data.Length, 1024);
        return data.AsSpan(0, limit).IndexOf(header) >= 0;
    }
}
