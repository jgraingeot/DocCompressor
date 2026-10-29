using System.Globalization;

namespace DocCompressor.Core;

/// <summary>
/// Mise en forme des tailles de fichier (unités décimales : 1 Ko = 1000 octets)
/// </summary>
public static class SizeFormatter
{
    // Virgule décimale à la française, sans dépendre des données de culture du système
    private static readonly NumberFormatInfo French = new() { NumberDecimalSeparator = "," };

    /// <summary>
    /// Formate une taille en octets, par exemple « 850 Ko » ou « 1,25 Mo »
    /// </summary>
    public static string Format(long bytes)
    {
        if (bytes < 1000)
            return $"{bytes} o";
        if (bytes < 1_000_000)
            return (bytes / 1000.0).ToString("0", French) + " Ko";
        return (bytes / 1_000_000.0).ToString("0.##", French) + " Mo";
    }
}
