using System;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using VoxMemo.ViewModels;

namespace VoxMemo.Views;

public partial class SettingsView : UserControl
{
    public static readonly IValueConverter IsOllamaConverter =
        new FuncValueConverter<string?, bool>(provider => provider == "Ollama");

    public static readonly IValueConverter IsOpenAiConverter =
        new FuncValueConverter<string?, bool>(provider => provider == "OpenAI");

    public static readonly IValueConverter IsAnthropicConverter =
        new FuncValueConverter<string?, bool>(provider => provider == "Anthropic");

    public SettingsView()
    {
        InitializeComponent();
    }

    private async void OnBrowseStoragePath(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Storage Location",
            AllowMultiple = false,
        });

        if (folders.Count > 0 && DataContext is SettingsViewModel vm)
        {
            var path = folders[0].TryGetLocalPath();
            if (path != null)
            {
                vm.StoragePath = path;
            }
        }
    }
}
