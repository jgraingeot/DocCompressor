using Avalonia.Media;
using DocCompressor.Core;

namespace DocCompressor.ViewModels;

/// <summary>
/// État d'un fichier de la liste
/// </summary>
public enum FileStatus
{
    Pending,
    Processing,
    Done,
    Error,
    Unsupported,
}

/// <summary>
/// Fichier à compresser, tel qu'affiché dans la liste
/// </summary>
public sealed class FileItemViewModel : ViewModelBase
{
    private static readonly IBrush NeutralBrush = new SolidColorBrush(Color.Parse("#6B6B6B"));
    private static readonly IBrush WorkingBrush = new SolidColorBrush(Color.Parse("#1F6FD1"));
    private static readonly IBrush SuccessBrush = new SolidColorBrush(Color.Parse("#00AA00"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#DD0000"));

    private FileStatus _status;
    private string _details = "";
    private long? _resultSize;
    private string? _outputPath;

    public FileItemViewModel(string path)
    {
        Path = path;
        FileName = System.IO.Path.GetFileName(path);
        OriginalSize = new FileInfo(path).Length;

        if (!DocumentCompressor.IsSupported(path))
        {
            _status = FileStatus.Unsupported;
            _details = System.IO.Path.GetExtension(path).ToLowerInvariant() is ".heic" or ".heif"
                ? "Photo HEIC (iPhone) non prise en charge : exportez-la en JPEG, ou réglez l'appareil photo sur « Le plus compatible »."
                : "Format non pris en charge (formats acceptés : PDF, JPG, PNG, WebP, BMP, GIF).";
        }
    }

    public string Path { get; }

    public string FileName { get; }

    public long OriginalSize { get; }

    public string OriginalSizeText => SizeFormatter.Format(OriginalSize);

    public FileStatus Status
    {
        get => _status;
        private set
        {
            if (SetField(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusBrush));
                OnPropertyChanged(nameof(CanProcess));
                OnPropertyChanged(nameof(CanOpenOutput));
            }
        }
    }

    /// <summary>
    /// Vrai si le fichier doit être (re)traité au prochain lancement
    /// </summary>
    public bool CanProcess => Status is FileStatus.Pending or FileStatus.Error;

    public string StatusText => Status switch
    {
        FileStatus.Pending => "En attente",
        FileStatus.Processing => "Compression…",
        FileStatus.Done when _resultSize is long size && size < OriginalSize =>
            $"✓ Réduit de {Math.Round(100.0 * (OriginalSize - size) / OriginalSize)} %",
        FileStatus.Done => "✓ Déjà conforme",
        FileStatus.Error => "✗ Échec",
        _ => "✗ Non pris en charge",
    };

    public IBrush StatusBrush => Status switch
    {
        FileStatus.Processing => WorkingBrush,
        FileStatus.Done => SuccessBrush,
        FileStatus.Error or FileStatus.Unsupported => ErrorBrush,
        _ => NeutralBrush,
    };

    /// <summary>
    /// Ce qui a été fait, ou la raison de l'échec
    /// </summary>
    public string Details
    {
        get => _details;
        private set => SetField(ref _details, value);
    }

    public string ResultSizeText => _resultSize is long size ? SizeFormatter.Format(size) : "—";

    /// <summary>
    /// Fichier produit lors du dernier traitement (conservé si le fichier repasse en attente)
    /// </summary>
    public string? OutputPath
    {
        get => _outputPath;
        private set => SetField(ref _outputPath, value);
    }

    public bool CanOpenOutput => Status == FileStatus.Done && OutputPath != null && File.Exists(OutputPath);

    public void MarkProcessing()
    {
        Status = FileStatus.Processing;
        Details = "";
    }

    public void MarkDone(FileCompressionResult result)
    {
        _resultSize = result.FinalSize;
        OutputPath = result.OutputPath;
        Details = $"{result.Details} → {System.IO.Path.GetFileName(result.OutputPath)}";
        OnPropertyChanged(nameof(ResultSizeText));
        Status = FileStatus.Done;
        OnPropertyChanged(nameof(CanOpenOutput));
    }

    public void MarkError(string message)
    {
        _resultSize = null;
        Details = message;
        OnPropertyChanged(nameof(ResultSizeText));
        Status = FileStatus.Error;
    }

    /// <summary>
    /// Remet le fichier en attente (réglages modifiés, ou traitement annulé)
    /// </summary>
    public void Reset()
    {
        if (Status == FileStatus.Unsupported)
            return;
        _resultSize = null;
        Details = "";
        OnPropertyChanged(nameof(ResultSizeText));
        Status = FileStatus.Pending;
    }
}
