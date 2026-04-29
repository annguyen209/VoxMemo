using Avalonia.Controls;

namespace VoxMemo.Views.Dialogs;

public record SmartProcessOptions(bool Transcribe, bool Speakers, bool Summarize, bool DontAskAgain);

public partial class SmartProcessDialog : Window
{
    public SmartProcessOptions? Options { get; private set; }

    public SmartProcessDialog() => InitializeComponent();

    public SmartProcessDialog(string savedSteps)
    {
        InitializeComponent();
        ChkTranscribe.IsChecked = savedSteps.Contains('t');
        ChkSpeakers.IsChecked = savedSteps.Contains('s');
        ChkSummarize.IsChecked = savedSteps.Contains('m');
    }

    private void OnCancel(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void OnStart(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Options = new SmartProcessOptions(
            Transcribe: ChkTranscribe.IsChecked == true,
            Speakers: ChkSpeakers.IsChecked == true,
            Summarize: ChkSummarize.IsChecked == true,
            DontAskAgain: ChkDontAsk.IsChecked == true);
        Close();
    }
}
