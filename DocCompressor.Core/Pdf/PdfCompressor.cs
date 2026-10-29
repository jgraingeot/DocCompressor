namespace DocCompressor.Core.Pdf;

/// <summary>
/// Compression d'un PDF : recompression des images si possible, sinon conversion des pages en images
/// </summary>
internal static class PdfCompressor
{
    public static CompressionOutcome Compress(byte[] data, string extension, CompressionOptions options, CancellationToken ct)
    {
        // Le filigrane doit être incrusté dans les pages : conversion en images dans tous les cas
        if (options.HasWatermark)
            return PdfRasterizer.Compress(data, options.MaxBytes, options.WatermarkText, ct);

        if (data.Length <= options.MaxBytes)
            return new CompressionOutcome(data, extension, CompressionMethod.Unchanged,
                "Déjà sous la limite : fichier conservé tel quel");

        try
        {
            var outcome = PdfImageRecompressor.TryCompress(data, options.MaxBytes, ct);
            if (outcome != null)
                return outcome;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // PDF chiffré, endommagé ou de structure non gérée par PDFsharp : PDFium saura le convertir en images
        }

        return PdfRasterizer.Compress(data, options.MaxBytes, null, ct);
    }
}
