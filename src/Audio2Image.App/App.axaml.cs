using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Audio2Image.App.ViewModels;
using Audio2Image.App.Views;
using Audio2Image.Core.Abstractions;
using Audio2Image.Core.Audio;
using Audio2Image.Core.Embeddings;
using Audio2Image.Core.Pipeline;
using Audio2Image.Core.Rendering;
using Audio2Image.Core.Scanning;
using Audio2Image.Core.Settings;
using Audio2Image.Core.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Audio2Image.App;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Configure DI container
            var services = new ServiceCollection();

            // Core services (singletons — shared across the app)
            services.AddSingleton<ISettingsService, SettingsServiceInstance>();
            services.AddSingleton<IAudioScanner, AudioScannerInstance>();
            services.AddSingleton<ISpectrogramPipeline, SpectrogramPipelineInstance>();
            services.AddSingleton<ISpectrogramRenderer, SpectrogramRendererInstance>();
            services.AddSingleton<IAudioDecoder, AudioDecoderInstance>();

            // Playback service — transient (each viewer gets its own instance)
            services.AddTransient<IAudioPlaybackService, AudioPlaybackService>();

            // Database — created from settings, registered after settings are available
            var provider = services.BuildServiceProvider();

            var settingsService = provider.GetRequiredService<ISettingsService>();
            var settings = settingsService.Load();

            // Apply theme from settings
            RequestedThemeVariant = settings.Theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
            if (string.IsNullOrEmpty(settings.DatabasePath))
            {
                settings.DatabasePath = System.IO.Path.Combine(
                    settingsService.GetDefaultLibraryPath(), "audio2image.db");
            }
            var database = new SpectrogramDatabase(settings.DatabasePath);

            var viewModel = new MainWindowViewModel(
                settingsService,
                provider.GetRequiredService<ISpectrogramPipeline>(),
                provider.GetRequiredService<IAudioScanner>(),
                () => provider.GetRequiredService<IAudioPlaybackService>());
            viewModel.SetDatabase(database);

            // Playlist service (shares SQLite connection with database)
            var playlistService = new PlaylistService(database.Connection);
            viewModel.SetPlaylistService(playlistService);

            // User tag service (shares SQLite connection with database)
            var userTagService = new UserTagService(database.Connection);
            viewModel.SetUserTagService(userTagService);

            // Embedding service (singleton — ONNX session should be shared)
            var embeddingService = new AudioEmbeddingService();
            viewModel.SetEmbeddingService(embeddingService);
            viewModel.InitEmbeddingService();

            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel
            };
            desktop.ShutdownRequested += (_, _) =>
            {
                viewModel.Dispose();
                embeddingService.Dispose();
                userTagService.Dispose();
                playlistService.Dispose();
                database.Dispose();
                (provider as IDisposable)?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
