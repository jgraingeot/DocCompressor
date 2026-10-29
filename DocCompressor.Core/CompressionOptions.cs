namespace DocCompressor.Core;

/// <summary>
/// Paramètres de compression d'un document
/// </summary>
public sealed class CompressionOptions
{
    /// <summary>
    /// Taille maximale par défaut : 1 Mo (1 000 000 octets, ce qui respecte aussi une limite de 1 Mio)
    /// </summary>
    public const long DefaultMaxBytes = 1_000_000;

    /// <summary>
    /// Taille maximale autorisée pour le fichier produit, en octets
    /// </summary>
    public long MaxBytes { get; init; } = DefaultMaxBytes;

    /// <summary>
    /// Si vrai, les images sont converties en PDF (une page A4) au lieu de rester en JPEG
    /// </summary>
    public bool ConvertImagesToPdf { get; init; }
}
