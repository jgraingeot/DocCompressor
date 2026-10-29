using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DocCompressor.Core;
using DocCompressor.ViewModels;

namespace DocCompressor;

public partial class MainWindow : Window
{
    private static readonly FilePickerFileType DocumentsFileType = new("Images et PDF")
    {
        // Les deux casses : les sélecteurs de fichiers Linux distinguent majuscules et minuscules
        Patterns = DocumentCompressor.SupportedExtensions
            .SelectMany(extension => new[] { "*" + extension, "*" + extension.ToUpperInvariant() })
            .ToList(),
        MimeTypes = ["application/pdf", "image/jpeg", "image/png", "image/webp", "image/bmp", "image/gif"],
        AppleUniformTypeIdentifiers = ["com.adobe.pdf", "public.image"],
    };

    public MainWindow()
    {
        InitializeComponent();
        DropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        DropZone.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        DropZone.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext!;

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        bool accept = ViewModel.IsIdle && e.Data.Contains(DataFormats.Files);
        e.DragEffects = accept ? DragDropEffects.Copy : DragDropEffects.None;
        DropZone.Classes.Set("dragover", accept);
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        DropZone.Classes.Remove("dragover");
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        DropZone.Classes.Remove("dragover");
        var paths = e.Data.GetFiles()?.Select(item => item.TryGetLocalPath()).OfType<string>();
        if (paths != null)
            ViewModel.AddFiles(paths);
    }

    private async void OnAddFilesClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choisir les documents à compresser",
                AllowMultiple = true,
                FileTypeFilter = [DocumentsFileType, FilePickerFileTypes.All],
            });
            ViewModel.AddFiles(files.Select(file => file.TryGetLocalPath()).OfType<string>());
        }
        catch (Exception exception)
        {
            // Sous Linux, le sélecteur dépend de GTK ou du portail du bureau : sans eux, il reste le glisser-déposer
            ViewModel.ReportError("Impossible d'ouvrir le sélecteur de fichiers (" + exception.Message + ") : glissez-déposez les fichiers dans la fenêtre.");
        }
    }

    private async void OnBrowseFolderClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Dossier où enregistrer les fichiers compressés",
                AllowMultiple = false,
            });
            var path = folders.FirstOrDefault()?.TryGetLocalPath();
            if (path == null)
                return;
            ViewModel.OutputFolder = path;
            ViewModel.SaveToFolder = true;
        }
        catch (Exception exception)
        {
            ViewModel.ReportError("Impossible d'ouvrir le sélecteur de dossier (" + exception.Message + ") : saisissez le chemin du dossier.");
        }
    }

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.Clear();
    }

    private async void OnCompressClick(object? sender, RoutedEventArgs e)
    {
        await ViewModel.CompressOrCancelAsync();
    }

    private void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is FileItemViewModel item)
            ViewModel.Remove(item);
    }

    private void OnMoveUpClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is FileItemViewModel item)
            ViewModel.Move(item, -1);
    }

    private void OnMoveDownClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is FileItemViewModel item)
            ViewModel.Move(item, +1);
    }

    private void OnOpenOutputClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is FileItemViewModel { OutputPath: string path })
            Open(path);
    }

    private void OnOpenFolderClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.ResultsFolder is string folder)
            Open(folder);
    }

    /// <summary>
    /// Ouvre un fichier ou un dossier avec l'application par défaut du système
    /// </summary>
    private static void Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            // Aucune application associée : rien à faire
        }
    }
}
