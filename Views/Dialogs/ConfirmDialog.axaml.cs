using Avalonia.Controls;
using Avalonia.Media;

namespace VoxMemo.Views.Dialogs;

public partial class ConfirmDialog : Window
{
    public bool Confirmed { get; private set; }

    public ConfirmDialog(
        string title,
        string message,
        string confirmText,
        string confirmColor,
        string? detail = null)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        ConfirmButton.Background = Brush.Parse(confirmColor);
        if (!string.IsNullOrEmpty(detail))
        {
            DetailText.IsVisible = true;
            DetailText.Text = detail;
        }
    }

    private void OnCancel(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void OnConfirm(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }
}
