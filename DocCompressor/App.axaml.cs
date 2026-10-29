using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DocCompressor.ViewModels;

namespace DocCompressor;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainViewModel(AppSettings.Load());
            // Fichiers passés en ligne de commande (« Ouvrir avec », glisser sur l'icône du programme)
            viewModel.AddFiles(desktop.Args ?? []);
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            desktop.ShutdownRequested += (_, _) => viewModel.SaveSettings();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
