using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using VoxMemo.ViewModels;

namespace VoxMemo.Views;

/// <summary>Helper that converts CurrentStep (int) to a brush using a ConverterParameter step number.</summary>
file sealed class StepBrushConverter : IValueConverter
{
    private readonly string _activeBrush;
    private readonly string _doneBrush;
    private readonly string _upcomingBrush;

    public StepBrushConverter(string activeBrush, string doneBrush, string upcomingBrush)
    {
        _activeBrush = activeBrush;
        _doneBrush = doneBrush;
        _upcomingBrush = upcomingBrush;
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (!int.TryParse(value?.ToString(), out int current)) return Brush.Parse(_upcomingBrush);
        if (!int.TryParse(parameter?.ToString(), out int stepNum)) return Brush.Parse(_upcomingBrush);
        if (current == stepNum) return Brush.Parse(_activeBrush);
        if (current > stepNum)  return Brush.Parse(_doneBrush);
        return Brush.Parse(_upcomingBrush);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Converts CurrentStep (int) to step circle text using ConverterParameter step number.</summary>
file sealed class StepTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (!int.TryParse(value?.ToString(), out int current)) return parameter?.ToString() ?? "";
        if (!int.TryParse(parameter?.ToString(), out int stepNum)) return parameter?.ToString() ?? "";
        return current > stepNum ? "✓" : stepNum.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Converts selected provider string + ConverterParameter to a brush.</summary>
file sealed class ProviderBrushConverter : IValueConverter
{
    private readonly string _matchBrush;
    private readonly string _noMatchBrush;

    public ProviderBrushConverter(string matchBrush, string noMatchBrush)
    {
        _matchBrush = matchBrush;
        _noMatchBrush = noMatchBrush;
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString() == parameter?.ToString()
            ? Brush.Parse(_matchBrush)
            : Brush.Parse(_noMatchBrush);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public partial class OnboardingWindow : Window
{
    private bool _closeInitiatedByVm;

    // Welcome circle: active (step 0) = purple, done (step > 0) = green
    public static readonly IValueConverter WelcomeBgConverter =
        new FuncValueConverter<int, IBrush>(step =>
            step == 0 ? Brush.Parse("#cba6f7") :
            step > 0  ? Brush.Parse("#a6e3a1") :
                        Brush.Parse("#313244"));

    // Active label FG for welcome (bool IsStep0)
    public static readonly IValueConverter ActiveFgConverter =
        new FuncValueConverter<bool, IBrush>(isActive =>
            isActive ? Brush.Parse("#cba6f7") : Brush.Parse("#585b70"));

    // Numbered step circle background: active=purple, done=green, upcoming=surface
    public static readonly IValueConverter StepBgConverter =
        new StepBrushConverter("#cba6f7", "#a6e3a1", "#313244");

    // Numbered step circle text: done=✓, else step number
    public static readonly IValueConverter StepTextConverter =
        new StepTextConverter();

    // Numbered step label foreground
    public static readonly IValueConverter StepFgConverter =
        new StepBrushConverter("#cba6f7", "#a6e3a1", "#585b70");

    // Provider card background
    public static readonly IValueConverter ProviderBgConverter =
        new ProviderBrushConverter("#313244", "#181825");

    // Provider card border
    public static readonly IValueConverter ProviderBorderConverter =
        new ProviderBrushConverter("#cba6f7", "Transparent");

    public OnboardingWindow()
    {
        InitializeComponent();
        var vm = new OnboardingViewModel();
        DataContext = vm;
        vm.CloseRequested += (_, _) => { _closeInitiatedByVm = true; Close(); };

        // X-button close: mark onboarding done so it doesn't reappear
        Closing += (_, e) =>
        {
            if (_closeInitiatedByVm) return;
            e.Cancel = true;
            if (DataContext is OnboardingViewModel closingVm)
                _ = closingVm.SkipCommand.ExecuteAsync(null);
        };
    }

    private void OnOllamaTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (DataContext is OnboardingViewModel vm) vm.SelectedAiProvider = "Ollama";
    }

    private void OnOpenAiTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (DataContext is OnboardingViewModel vm) vm.SelectedAiProvider = "OpenAI";
    }

    private void OnAnthropicTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (DataContext is OnboardingViewModel vm) vm.SelectedAiProvider = "Anthropic";
    }
}
