namespace DocCompressor.Core;

/// <summary>
/// Composition du texte du filigrane
/// </summary>
public static class WatermarkText
{
    /// <summary>
    /// Texte saisi, suivi de la date si demandé (« … – le 29/10/2026 »)
    /// </summary>
    public static string Compose(string text, bool addDate, DateTime date)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || !addDate)
            return trimmed;
        return $"{trimmed} – le {date:dd'/'MM'/'yyyy}";
    }
}
