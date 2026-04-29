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

    private VoxMemo.ViewModels.SettingsViewModel? _previousSettingsVm;

    private void OnSettingsVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(VoxMemo.ViewModels.SettingsViewModel.SelectedTheme) &&
            DataContext is VoxMemo.ViewModels.SettingsViewModel vm)
        {
            DarkRadio.IsChecked = vm.SelectedTheme == "dark";
            LightRadio.IsChecked = vm.SelectedTheme == "light";
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_previousSettingsVm != null)
            _previousSettingsVm.PropertyChanged -= OnSettingsVmPropertyChanged;

        base.OnDataContextChanged(e);

        if (DataContext is VoxMemo.ViewModels.SettingsViewModel vm)
        {
            _previousSettingsVm = vm;
            vm.PropertyChanged += OnSettingsVmPropertyChanged;
            DarkRadio.IsChecked = vm.SelectedTheme == "dark";
            LightRadio.IsChecked = vm.SelectedTheme == "light";
        }
        else
        {
            _previousSettingsVm = null;
        }
    }

    private void OnDarkThemeChecked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is VoxMemo.ViewModels.SettingsViewModel vm)
            vm.SelectedTheme = "dark";
    }

    private void OnLightThemeChecked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is VoxMemo.ViewModels.SettingsViewModel vm)
            vm.SelectedTheme = "light";
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
