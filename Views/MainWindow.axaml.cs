using Avalonia.Controls;
using VoxMemo.ViewModels;

namespace VoxMemo.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        e.Cancel = true;
        Hide();

        MainWindowViewModel.ShowTrayNotification("VoxMemo", "VoxMemo is still running in the system tray.");
    }
}
