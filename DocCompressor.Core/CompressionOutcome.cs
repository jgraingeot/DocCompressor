namespace DocCompressor.Core;

/// <summary>
/// Méthode utilisée pour atteindre la taille cible
/// </summary>
public enum CompressionMethod
{
    /// <summary>Le fichier respectait déjà la limite : il est recopié tel quel</summary>
    Unchanged,

    /// <summary>L'image a été réencodée en JPEG (éventuellement redimensionnée)</summary>
    ImageReencoded,

    /// <summary>L'image a été placée dans un PDF</summary>
    ImageToPdf,

    /// <summary>Les images contenues dans le PDF ont été recompressées (texte conservé)</summary>
    PdfImagesRecompressed,

    /// <summary>Les pages du PDF ont été converties en images</summary>
    PdfRasterized,
}

/// <summary>
/// Résultat de la compression d'un document, en mémoire
/// </summary>
/// <param name="Data">Contenu du fichier produit</param>
/// <param name="Extension">Extension du fichier produit (avec le point)</param>
/// <param name="Method">Méthode utilisée</param>
/// <param name="Details">Description lisible de ce qui a été fait</param>
public sealed record CompressionOutcome(byte[] Data, string Extension, CompressionMethod Method, string Details);

/// <summary>
/// Erreur de compression dont le message peut être présenté tel quel à l'utilisateur
/// </summary>
public sealed class CompressionException : Exception
{
    public CompressionException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}
