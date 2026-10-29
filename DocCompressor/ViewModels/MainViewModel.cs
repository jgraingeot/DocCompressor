using System.Collections.ObjectModel;
using DocCompressor.Core;

namespace DocCompressor.ViewModels;

/// <summary>
/// Modèle de vue de la fenêtre principale : liste des fichiers, réglages et traitement
/// </summary>
public sealed class MainViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private decimal? _maxSize;
    private SizeUnit _unit;
    private bool _convertImagesToPdf;
    private OutputLocation _outputLocation;
    private string? _outputFolder;
    private bool _isBusy;
    private double _progress;
    private string _statusMessage = "";
    private CancellationTokenSource? _cancellation;

    public MainViewModel(AppSettings settings)
    {
        _settings = settings;
        _maxSize = settings.MaxSize;
        _unit = settings.Unit;
        _convertImagesToPdf = settings.ConvertImagesToPdf;
        _outputLocation = settings.OutputLocation;
        _outputFolder = settings.OutputFolder;

        Files.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(FileCountText));
            RefreshCommands();
        };
    }

    public ObservableCollection<FileItemViewModel> Files { get; } = [];

    public bool IsEmpty => Files.Count == 0;

    public string FileCountText => Files.Count > 1 ? $"{Files.Count} fichiers" : $"{Files.Count} fichier";

    public IReadOnlyList<string> Units { get; } = ["Ko", "Mo"];

    /// <summary>
    /// Taille maximale saisie, dans l'unité choisie
    /// </summary>
    public decimal? MaxSize
    {
        get => _maxSize;
        set
        {
            if (SetField(ref _maxSize, value))
                OnSettingsChanged();
        }
    }

    public int UnitIndex
    {
        get => (int)_unit;
        set
        {
            if (value < 0 || !SetField(ref _unit, (SizeUnit)value))
                return;
            OnPropertyChanged(nameof(SizeIncrement));
            OnSettingsChanged();
        }
    }

    public decimal SizeIncrement => _unit == SizeUnit.Megabytes ? 0.1m : 50m;

    public bool ConvertImagesToPdf
    {
        get => _convertImagesToPdf;
        set
        {
            if (SetField(ref _convertImagesToPdf, value))
                OnSettingsChanged();
        }
    }

    public bool SaveNextToOriginal
    {
        get => _outputLocation == OutputLocation.NextToOriginal;
        set
        {
            if (value)
                SetOutputLocation(OutputLocation.NextToOriginal);
        }
    }

    public bool SaveToFolder
    {
        get => _outputLocation == OutputLocation.Folder;
        set
        {
            if (value)
                SetOutputLocation(OutputLocation.Folder);
        }
    }

    public string SaveNextToOriginalText => $"À côté de chaque original, avec le suffixe « {OutputSettings.DefaultSuffix} »";

    public string? OutputFolder
    {
        get => _outputFolder;
        set
        {
            if (SetField(ref _outputFolder, value))
                OnSettingsChanged();
        }
    }

    /// <summary>
    /// Taille maximale en octets, ou null si la saisie est invalide
    /// </summary>
    public long? MaxBytes => _maxSize is decimal size && size > 0
        ? (long)Math.Round(size * (_unit == SizeUnit.Megabytes ? 1_000_000m : 1000m))
        : null;

    /// <summary>
    /// Problème empêchant de lancer la compression, à afficher à l'utilisateur
    /// </summary>
    public string? ValidationMessage
    {
        get
        {
            if (MaxBytes is not long maxBytes || maxBytes < DocumentCompressor.MinimumMaxBytes)
                return $"La taille maximale doit être d'au moins {SizeFormatter.Format(DocumentCompressor.MinimumMaxBytes)}.";
            if (_outputLocation == OutputLocation.Folder && string.IsNullOrWhiteSpace(_outputFolder))
                return "Choisissez le dossier où enregistrer les fichiers compressés.";
            return null;
        }
    }

    public bool HasValidationMessage => ValidationMessage != null;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                OnPropertyChanged(nameof(CompressButtonText));
                OnPropertyChanged(nameof(ShowProgress));
                RefreshCommands();
            }
        }
    }

    public bool IsIdle => !IsBusy;

    public string CompressButtonText => IsBusy ? "Annuler" : "Compresser";

    public bool CanCompress => !IsBusy && ValidationMessage == null && Files.Any(f => f.CanProcess);

    public bool CanClickCompress => IsBusy || CanCompress;

    public bool CanClear => !IsBusy && !IsEmpty;

    public bool HasResults => Files.Any(f => f.CanOpenOutput);

    public double Progress
    {
        get => _progress;
        private set
        {
            if (SetField(ref _progress, value))
                OnPropertyChanged(nameof(ShowProgress));
        }
    }

    public bool ShowProgress => IsBusy || Progress > 0;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    /// <summary>
    /// Ajoute des fichiers (ou le contenu de dossiers) à la liste, sans doublon
    /// </summary>
    public void AddFiles(IEnumerable<string> paths)
    {
        if (IsBusy)
            return;

        var comparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        var known = new HashSet<string>(Files.Select(f => f.Path), comparer);
        int added = 0;

        foreach (var path in paths.SelectMany(Expand))
        {
            var fullPath = Path.GetFullPath(path);
            if (!known.Add(fullPath))
                continue;
            Files.Add(new FileItemViewModel(fullPath));
            added++;
        }

        if (added > 0)
        {
            Progress = 0;
            StatusMessage = "";
        }
    }

    private static IEnumerable<string> Expand(string path)
    {
        if (File.Exists(path))
            return [path];
        if (Directory.Exists(path))
        {
            try
            {
                return Directory.EnumerateFiles(path).Where(DocumentCompressor.IsSupported).Order().ToList();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }
        return [];
    }

    /// <summary>
    /// Affiche un message d'erreur dans la barre d'état
    /// </summary>
    public void ReportError(string message)
    {
        StatusMessage = message;
    }

    public void Remove(FileItemViewModel item)
    {
        if (!IsBusy)
            Files.Remove(item);
    }

    public void Clear()
    {
        if (IsBusy)
            return;
        Files.Clear();
        Progress = 0;
        StatusMessage = "";
    }

    /// <summary>
    /// Lance la compression des fichiers en attente, ou l'annule si elle est en cours
    /// </summary>
    public async Task CompressOrCancelAsync()
    {
        if (IsBusy)
        {
            _cancellation?.Cancel();
            StatusMessage = "Annulation…";
            return;
        }
        if (!CanCompress || MaxBytes is not long maxBytes)
            return;

        var items = Files.Where(f => f.CanProcess).ToList();
        var options = new CompressionOptions { MaxBytes = maxBytes, ConvertImagesToPdf = ConvertImagesToPdf };
        var output = new OutputSettings { Location = _outputLocation, Folder = _outputFolder };

        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        IsBusy = true;
        Progress = 0;

        int processed = 0, failures = 0;
        bool cancelled = false;
        foreach (var item in items)
        {
            if (token.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            item.MarkProcessing();
            StatusMessage = $"Compression de « {item.FileName} » ({processed + 1}/{items.Count})…";
            try
            {
                DeletePreviousOutput(item);
                var result = await Task.Run(() => FileCompressor.CompressFile(item.Path, options, output, token), token);
                item.MarkDone(result);
            }
            catch (OperationCanceledException)
            {
                item.Reset();
                cancelled = true;
                break;
            }
            catch (CompressionException exception)
            {
                item.MarkError(exception.Message);
                failures++;
            }
            catch (Exception exception)
            {
                item.MarkError("Erreur inattendue : " + exception.Message);
                failures++;
            }

            processed++;
            Progress = 100.0 * processed / items.Count;
        }

        _cancellation.Dispose();
        _cancellation = null;
        IsBusy = false;

        int succeeded = processed - failures;
        string limit = SizeFormatter.Format(maxBytes);
        StatusMessage = cancelled
            ? $"Compression annulée ({succeeded} fichier(s) prêt(s))."
            : failures == 0
                ? succeeded > 1 ? $"Terminé : {succeeded} fichiers prêts, tous sous {limit}." : $"Terminé : fichier prêt, sous {limit}."
                : $"Terminé : {succeeded} fichier(s) prêt(s), {failures} échec(s).";
    }

    /// <summary>
    /// Dossier contenant les fichiers produits
    /// </summary>
    public string? ResultsFolder
    {
        get
        {
            if (_outputLocation == OutputLocation.Folder && Directory.Exists(_outputFolder))
                return _outputFolder;
            var output = Files.FirstOrDefault(f => f.CanOpenOutput)?.OutputPath;
            return output != null ? Path.GetDirectoryName(output) : null;
        }
    }

    public void SaveSettings()
    {
        _settings.MaxSize = _maxSize ?? 1;
        _settings.Unit = _unit;
        _settings.ConvertImagesToPdf = _convertImagesToPdf;
        _settings.OutputLocation = _outputLocation;
        _settings.OutputFolder = _outputFolder;
        _settings.Save();
    }

    private void SetOutputLocation(OutputLocation location)
    {
        if (_outputLocation == location)
            return;
        _outputLocation = location;
        OnPropertyChanged(nameof(SaveNextToOriginal));
        OnPropertyChanged(nameof(SaveToFolder));
        OnSettingsChanged();
    }

    /// <summary>
    /// Les résultats obtenus avec les anciens réglages ne sont plus valables : les fichiers repassent en attente
    /// </summary>
    private void OnSettingsChanged()
    {
        if (!IsBusy)
        {
            foreach (var item in Files)
                item.Reset();
            Progress = 0;
            StatusMessage = "";
        }
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(HasValidationMessage));
        RefreshCommands();
        SaveSettings();
    }

    private void RefreshCommands()
    {
        OnPropertyChanged(nameof(CanCompress));
        OnPropertyChanged(nameof(CanClickCompress));
        OnPropertyChanged(nameof(CanClear));
        OnPropertyChanged(nameof(HasResults));
    }

    /// <summary>
    /// Supprime le fichier produit lors d'un précédent traitement du même document, pour ne pas accumuler les copies
    /// </summary>
    private static void DeletePreviousOutput(FileItemViewModel item)
    {
        var previous = item.OutputPath;
        if (previous == null || previous == item.Path || !File.Exists(previous))
            return;
        try
        {
            File.Delete(previous);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Fichier ouvert ailleurs : le nouveau résultat sera simplement numéroté
        }
    }
}
