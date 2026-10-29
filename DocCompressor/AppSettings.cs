using System.Text.Json;
using DocCompressor.Core;

namespace DocCompressor;

/// <summary>
/// Réglages mémorisés d'une utilisation à l'autre
/// </summary>
public sealed class AppSettings
{
    public decimal MaxSize { get; set; } = 1;
    public SizeUnit Unit { get; set; } = SizeUnit.Megabytes;
    public bool ConvertImagesToPdf { get; set; }
    public OutputLocation OutputLocation { get; set; } = OutputLocation.NextToOriginal;
    public string? OutputFolder { get; set; }

    /// <summary>
    /// %APPDATA%\DocCompressor sous Windows, ~/.config/DocCompressor sous Linux
    /// </summary>
    private static string FilePath
    {
        get
        {
            // DoNotVerify : sans cette option, le chemin est vide sous Linux si le dossier n'existe pas encore
            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify);
            if (string.IsNullOrEmpty(root))
                root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            return Path.Combine(root, "DocCompressor", "settings.json");
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // Réglages illisibles : on repart des valeurs par défaut
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Sans importance : les réglages ne seront simplement pas mémorisés
        }
    }
}

/// <summary>
/// Unité de la taille maximale
/// </summary>
public enum SizeUnit
{
    Kilobytes,
    Megabytes,
}
