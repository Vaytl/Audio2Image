using System;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Audio2Image.App.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var versionStr = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
        VersionText.Text = $"Version {versionStr}";
        CopyrightText.Text = $"\u00a9 {DateTime.Now.Year} Audio2Image";
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnGitHubClick(object? sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "https://github.com/Vaytl",
            UseShellExecute = true
        });
    }
}
