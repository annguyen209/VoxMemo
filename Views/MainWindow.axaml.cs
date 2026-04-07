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
        if (DataContext is MainWindowViewModel vm && vm.Recording.IsRecording)
        {
            // Minimize to system tray while recording
            e.Cancel = true;
            Hide();
        }
        else
        {
            // If not recording, hide to tray (user can exit from tray menu)
            e.Cancel = true;
            Hide();
        }
    }
}
