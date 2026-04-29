using Avalonia.Controls;

namespace VoxMemo.Views.Dialogs;

public partial class TranscriptOverwriteDialog : Window
{
    public bool ShouldOverwrite { get; private set; }

    public TranscriptOverwriteDialog() => InitializeComponent();

    private void OnKeep(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void OnOverwrite(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ShouldOverwrite = true;
        Close();
    }
}
