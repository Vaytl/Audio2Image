using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Audio2Image.App.Models;
using Audio2Image.App.ViewModels;

namespace Audio2Image.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);

        // Gallery item click with modifier support (PointerPressed carries key modifiers)
        AddHandler(PointerPressedEvent, OnGalleryPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private void OnGalleryPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (vm.IsViewerOpen) return;

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;

        // Find the SpectrogramItem from the pressed element
        var item = FindSpectrogramItem(e.Source as Control);
        if (item == null) return;

        // Check for tag-pill or context menu — don't handle
        if (e.Source is Control source && IsInsideTagPill(source)) return;

        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        vm.HandleItemClick(item, ctrl, shift);
        e.Handled = true;
    }

    private static bool IsInsideTagPill(Control? control)
    {
        var current = control;
        while (current != null)
        {
            if (current.Classes.Contains("tag-pill")) return true;
            current = current.Parent as Control;
        }
        return false;
    }

    private static SpectrogramItem? FindSpectrogramItem(Control? control)
    {
        var current = control;
        while (current != null)
        {
            if (current.DataContext is SpectrogramItem item) return item;
            current = current.Parent as Control;
        }
        return null;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MainWindowViewModel vm)
        {
            vm.FolderPicker = PickFolderAsync;
            vm.FilePicker = PickFilesAsync;
            vm.SettingsOpener = OpenSettingsDialog;
            vm.ConfirmAction = ConfirmDialog;
            vm.AboutOpener = OpenAboutDialog;
            vm.InputDialog = InputDialogAsync;
            vm.FileSavePicker = SaveFileAsync;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (DataContext is not MainWindowViewModel vm) return;

        // F1 help toggle (only when viewer is not open)
        if (e.Key == Key.F1 && !vm.IsViewerOpen)
        {
            var overlay = this.FindControl<Avalonia.Controls.Border>("ShortcutsOverlay");
            if (overlay != null)
                overlay.IsVisible = !overlay.IsVisible;
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Key == Key.O)
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    vm.SelectFolderCommand.Execute().Subscribe();
                else
                    vm.SelectFilesCommand.Execute().Subscribe();
                e.Handled = true;
            }
            else if (e.Key == Key.OemComma)
            {
                vm.OpenSettingsCommand.Execute().Subscribe();
                e.Handled = true;
            }
            else if (e.Key == Key.F)
            {
                // Focus search box
                var searchBox = this.FindControl<TextBox>("SearchBox");
                searchBox?.Focus();
                e.Handled = true;
            }
        }
        else if (e.Key == Key.F5)
        {
            vm.RefreshLibraryCommand.Execute().Subscribe();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && vm.IsMultiSelectMode)
        {
            vm.DeselectAllCommand.Execute().Subscribe();
            e.Handled = true;
        }
        else if (e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Control) && !vm.IsViewerOpen)
        {
            vm.SelectAllCommand.Execute().Subscribe();
            e.Handled = true;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files)
            ? DragDropEffects.Copy
            : DragDropEffects.None;

        if (DataContext is MainWindowViewModel vm)
            vm.IsDropTargetActive = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.IsDropTargetActive = false;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        vm.IsDropTargetActive = false;

        if (!e.Data.Contains(DataFormats.Files)) return;

        var storageItems = e.Data.GetFiles();
        if (storageItems == null) return;

        var audioExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".ogg" };
        var audioFiles = new List<string>();
        string? folderPath = null;

        foreach (var item in storageItems)
        {
            var path = item.Path.LocalPath;

            if (Directory.Exists(path))
            {
                // If a folder is dropped, process it as folder
                folderPath = path;
                break;
            }

            if (File.Exists(path) && audioExtensions.Contains(Path.GetExtension(path)))
            {
                audioFiles.Add(path);
            }
        }

        if (folderPath != null)
        {
            vm.HandleDroppedFolder(folderPath);
        }
        else if (audioFiles.Count > 0)
        {
            vm.HandleDroppedFiles(audioFiles);
        }
    }

    private async Task OpenAboutDialog()
    {
        var aboutWindow = new AboutWindow();
        await aboutWindow.ShowDialog(this);
    }

    private async Task OpenSettingsDialog()
    {
        var mainVm = (MainWindowViewModel)DataContext!;
        var settingsVm = new SettingsViewModel(mainVm.SettingsService);
        var settingsWindow = new SettingsWindow
        {
            DataContext = settingsVm
        };
        await settingsWindow.ShowDialog(this);
    }

    private async Task<bool> ConfirmDialog(string title, string message)
    {
        bool result = false;
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var yesBtn = new Button
        {
            Content = title,
            Padding = new Thickness(20, 8),
            Foreground = Brushes.White
        };
        var noBtn = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(20, 8)
        };
        yesBtn.Click += (_, _) => { result = true; dialog.Close(); };
        noBtn.Click += (_, _) => { result = false; dialog.Close(); };

        var buttonsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        buttonsPanel.Children.Add(yesBtn);
        buttonsPanel.Children.Add(noBtn);
        DockPanel.SetDock(buttonsPanel, Dock.Bottom);

        var messageBlock = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 16)
        };

        var dockPanel = new DockPanel
        {
            Margin = new Thickness(20)
        };
        dockPanel.Children.Add(buttonsPanel);
        dockPanel.Children.Add(messageBlock);

        dialog.Content = dockPanel;

        await dialog.ShowDialog(this);
        return result;
    }

    private async Task<string?> InputDialogAsync(string title, string defaultValue)
    {
        string? result = null;
        var dialog = new Window
        {
            Title = title,
            Width = 400,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var textBox = new TextBox
        {
            Text = defaultValue,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 12)
        };
        textBox.SelectAll();

        var okBtn = new Button
        {
            Content = "OK",
            Padding = new Thickness(20, 8),
            Foreground = Brushes.White
        };
        var cancelBtn = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(20, 8)
        };
        okBtn.Click += (_, _) => { result = textBox.Text; dialog.Close(); };
        cancelBtn.Click += (_, _) => { dialog.Close(); };

        var buttonsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        buttonsPanel.Children.Add(okBtn);
        buttonsPanel.Children.Add(cancelBtn);
        DockPanel.SetDock(buttonsPanel, Dock.Bottom);

        var dockPanel = new DockPanel { Margin = new Thickness(20) };
        dockPanel.Children.Add(buttonsPanel);
        dockPanel.Children.Add(textBox);

        dialog.Content = dockPanel;
        dialog.Opened += (_, _) => textBox.Focus();

        await dialog.ShowDialog(this);
        return result;
    }

    private async Task<string?> SaveFileAsync(string defaultName, string extension)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return null;

        var fileType = new FilePickerFileType($"{extension.ToUpperInvariant()} files")
        {
            Patterns = new[] { $"*.{extension}" }
        };

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Export as .{extension}",
            SuggestedFileName = $"{defaultName}.{extension}",
            FileTypeChoices = new[] { fileType }
        });

        return file?.Path.LocalPath;
    }

    private async Task<string?> PickFolderAsync()
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return null;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select audio folder",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            return folders[0].Path.LocalPath;
        }

        return null;
    }

    private async Task<IReadOnlyList<string>?> PickFilesAsync()
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return null;

        var audioFilter = new FilePickerFileType("Audio files")
        {
            Patterns = new[] { "*.mp3", "*.wav", "*.ogg" }
        };

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select audio files",
            AllowMultiple = true,
            FileTypeFilter = new[] { audioFilter }
        });

        if (files.Count == 0) return null;

        var paths = new List<string>();
        foreach (var file in files)
        {
            paths.Add(file.Path.LocalPath);
        }
        return paths;
    }
}
