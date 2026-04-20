using Avalonia.Controls;
using VoxMemo.ViewModels;

namespace VoxMemo.Views;

public partial class MainWindow : Window
{
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    public void PrepareForExit()
    {
        _allowClose = true;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose)
            return;

        e.Cancel = true;
        Hide();

        MainWindowViewModel.ShowTrayNotification("VoxMemo", "VoxMemo is still running in the system tray.");
    }
}
