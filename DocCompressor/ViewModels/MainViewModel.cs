using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
    private bool _mergeMode;
    private bool _twoImagesPerPage;
    private string _mergeName = "";
    private bool _watermarkEnabled;
    private string _watermarkInput = "";
    private bool _watermarkAddDate;
    // PDF fusionné produit pour la liste actuelle : remplacé si la fusion est relancée
    private string? _mergedOutputPath;
    private bool _isBusy;
    private bool _isIndeterminate;
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
        _twoImagesPerPage = settings.TwoImagesPerPage;
        _watermarkAddDate = settings.WatermarkAddDate;

        Files.CollectionChanged += (_, e) =>
        {
            // Nouvelle liste de fichiers : un nouveau PDF fusionné ne remplacera pas le précédent
            if (e.Action != NotifyCollectionChangedAction.Move)
                _mergedOutputPath = null;
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(FileCountText));
            OnPropertyChanged(nameof(SuggestedMergeName));
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

    /// <summary>
    /// Vrai pour regrouper tous les fichiers dans un seul PDF, au lieu de les compresser un par un
    /// </summary>
    public bool MergeMode
    {
        get => _mergeMode;
        set
        {
            if (!SetField(ref _mergeMode, value))
                return;
            OnPropertyChanged(nameof(SeparateMode));
            OnPropertyChanged(nameof(CompressButtonText));
            OnPropertyChanged(nameof(SaveNextToOriginalText));
            OnSettingsChanged();
        }
    }

    public bool SeparateMode => !_mergeMode;

    /// <summary>
    /// Recto et verso sur la même page : les images sont placées deux par page dans le PDF fusionné
    /// </summary>
    public bool TwoImagesPerPage
    {
        get => _twoImagesPerPage;
        set
        {
            if (SetField(ref _twoImagesPerPage, value))
                OnSettingsChanged();
        }
    }

    /// <summary>
    /// Nom choisi pour le PDF fusionné ; vide pour utiliser le nom proposé
    /// </summary>
    public string MergeName
    {
        get => _mergeName;
        set
        {
            if (SetField(ref _mergeName, value ?? ""))
                OnSettingsChanged();
        }
    }

    public string SuggestedMergeName =>
        FileCompressor.SuggestMergedName(MergeCandidates.Select(f => f.Path).ToList());

    private IEnumerable<FileItemViewModel> MergeCandidates => Files.Where(f => f.Status != FileStatus.Unsupported);

    /// <summary>
    /// Ajout d'un filigrane (le texte n'est pas mémorisé : il dépend du destinataire)
    /// </summary>
    public bool WatermarkEnabled
    {
        get => _watermarkEnabled;
        set
        {
            if (SetField(ref _watermarkEnabled, value))
                OnWatermarkChanged();
        }
    }

    public string WatermarkInput
    {
        get => _watermarkInput;
        set
        {
            if (SetField(ref _watermarkInput, value ?? ""))
                OnWatermarkChanged();
        }
    }

    public bool WatermarkAddDate
    {
        get => _watermarkAddDate;
        set
        {
            if (SetField(ref _watermarkAddDate, value))
                OnWatermarkChanged();
        }
    }

    /// <summary>
    /// Texte qui sera réellement incrusté, ou null si pas de filigrane
    /// </summary>
    private string? WatermarkText => _watermarkEnabled && !string.IsNullOrWhiteSpace(_watermarkInput)
        ? Core.WatermarkText.Compose(_watermarkInput, _watermarkAddDate, DateTime.Today)
        : null;

    public string WatermarkPreview => WatermarkText is string text ? $"Texte incrusté : « {text} »" : "";

    public bool ShowWatermarkPreview => WatermarkText != null;

    private void OnWatermarkChanged()
    {
        OnPropertyChanged(nameof(WatermarkPreview));
        OnPropertyChanged(nameof(ShowWatermarkPreview));
        OnSettingsChanged();
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

    public string SaveNextToOriginalText => _mergeMode
        ? "Dans le dossier du premier fichier de la liste"
        : $"À côté de chaque original, avec le suffixe « {OutputSettings.DefaultSuffix} »";

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
            if (_watermarkEnabled && string.IsNullOrWhiteSpace(_watermarkInput))
                return "Saisissez le texte du filigrane (destinataire et motif).";
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

    public string CompressButtonText => IsBusy ? "Annuler" : _mergeMode ? "Fusionner" : "Compresser";

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

    /// <summary>
    /// Progression non mesurable (fusion : une seule opération)
    /// </summary>
    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        private set
        {
            if (SetField(ref _isIndeterminate, value))
                OnPropertyChanged(nameof(ShowProgressText));
        }
    }

    public bool ShowProgressText => !IsIndeterminate;

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

    /// <summary>
    /// Déplace un fichier dans la liste (ordre des pages du PDF fusionné)
    /// </summary>
    public void Move(FileItemViewModel item, int offset)
    {
        int index = Files.IndexOf(item);
        int target = index + offset;
        if (IsBusy || index < 0 || target < 0 || target >= Files.Count)
            return;
        Files.Move(index, target);
        OnPropertyChanged(nameof(SuggestedMergeName));
        if (_mergeMode)
            OnSettingsChanged();
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

        var options = new CompressionOptions
        {
            MaxBytes = maxBytes,
            ConvertImagesToPdf = ConvertImagesToPdf,
            WatermarkText = WatermarkText,
        };
        var output = new OutputSettings { Location = _outputLocation, Folder = _outputFolder };

        _cancellation = new CancellationTokenSource();
        IsBusy = true;
        Progress = 0;
        try
        {
            if (_mergeMode)
                await MergeAsync(options, output, _cancellation.Token);
            else
                await CompressEachAsync(options, output, _cancellation.Token);
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            IsBusy = false;
        }
    }

    /// <summary>
    /// Compresse les fichiers un par un
    /// </summary>
    private async Task CompressEachAsync(CompressionOptions options, OutputSettings output, CancellationToken token)
    {
        var items = Files.Where(f => f.CanProcess).ToList();

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

        int succeeded = processed - failures;
        string limit = SizeFormatter.Format(options.MaxBytes);
        StatusMessage = cancelled
            ? $"Compression annulée ({succeeded} fichier(s) prêt(s))."
            : failures == 0
                ? succeeded > 1 ? $"Terminé : {succeeded} fichiers prêts, tous sous {limit}." : $"Terminé : fichier prêt, sous {limit}."
                : $"Terminé : {succeeded} fichier(s) prêt(s), {failures} échec(s).";
    }

    /// <summary>
    /// Regroupe tous les fichiers pris en charge, dans l'ordre de la liste, en un seul PDF
    /// </summary>
    private async Task MergeAsync(CompressionOptions options, OutputSettings output, CancellationToken token)
    {
        var items = MergeCandidates.ToList();
        var paths = items.Select(f => f.Path).ToList();
        var name = string.IsNullOrWhiteSpace(_mergeName) ? SuggestedMergeName : _mergeName;
        bool twoImagesPerPage = _twoImagesPerPage;

        foreach (var item in items)
            item.MarkProcessing();
        StatusMessage = items.Count > 1 ? $"Fusion de {items.Count} fichiers…" : "Conversion en PDF…";
        IsIndeterminate = true;
        try
        {
            DeleteFile(_mergedOutputPath, paths);
            var result = await Task.Run(
                () => FileCompressor.MergeFiles(paths, name, options, output, twoImagesPerPage, token), token);
            _mergedOutputPath = result.OutputPath;
            foreach (var item in items)
                item.MarkMerged(result.OutputPath);
            Progress = 100;
            StatusMessage = $"Terminé : « {Path.GetFileName(result.OutputPath)} », " +
                            $"{SizeFormatter.Format(result.FinalSize)}. {result.Details}";
        }
        catch (OperationCanceledException)
        {
            foreach (var item in items)
                item.Reset();
            StatusMessage = "Fusion annulée.";
        }
        catch (Exception exception)
        {
            string message = exception is CompressionException ? exception.Message : "Erreur inattendue : " + exception.Message;
            foreach (var item in items)
                item.MarkError(message);
            StatusMessage = "Échec de la fusion : " + message;
        }
        finally
        {
            IsIndeterminate = false;
        }
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
        _settings.TwoImagesPerPage = _twoImagesPerPage;
        _settings.WatermarkAddDate = _watermarkAddDate;
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
    private static void DeletePreviousOutput(FileItemViewModel item) =>
        DeleteFile(item.IndividualOutputPath, [item.Path]);

    /// <summary>
    /// Supprime un ancien résultat, sauf s'il s'agit d'un des fichiers d'origine
    /// </summary>
    private static void DeleteFile(string? path, IReadOnlyList<string> originals)
    {
        if (path == null || originals.Contains(path) || !File.Exists(path))
            return;
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Fichier ouvert ailleurs : le nouveau résultat sera simplement numéroté
        }
    }
}
