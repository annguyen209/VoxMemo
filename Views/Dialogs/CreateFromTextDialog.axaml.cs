using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace VoxMemo.Views.Dialogs;

public record CreateFromTextResult(string Title, string Transcript, string Language);

public partial class CreateFromTextDialog : Window
{
    public CreateFromTextResult? Result { get; private set; }

    public CreateFromTextDialog(string defaultLanguage, List<string> languages)
    {
        InitializeComponent();
        LangCombo.ItemsSource = languages;
        LangCombo.SelectedItem = defaultLanguage;
    }

    private void OnCancel(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void OnCreate(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var transcript = TranscriptBox.Text ?? "";
        if (string.IsNullOrWhiteSpace(transcript)) return;
        var title = string.IsNullOrWhiteSpace(TitleBox.Text)
            ? $"Text Import {System.DateTime.Now:MMM dd, yyyy HH:mm}"
            : TitleBox.Text;
        Result = new CreateFromTextResult(
            Title: title,
            Transcript: transcript,
            Language: LangCombo.SelectedItem?.ToString() ?? "en");
        Close();
    }

    private async void OnImportFile(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select text file",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Text files")
                {
                    Patterns = ["*.txt", "*.md", "*.srt", "*.vtt", "*.csv", "*.log"]
                }
            ]
        });
        if (files.Count > 0)
        {
            var filePath = files[0].Path.LocalPath;
            TranscriptBox.Text = await File.ReadAllTextAsync(filePath);
            if (string.IsNullOrWhiteSpace(TitleBox.Text))
                TitleBox.Text = Path.GetFileNameWithoutExtension(filePath);
        }
    }
}
