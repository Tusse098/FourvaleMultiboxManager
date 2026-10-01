using System.ComponentModel;
using System.Windows;

namespace Multibox.App;

public partial class LiveStateWindow : Window
{
    public LiveStateWindow(LiveStateViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    // Closing only hides it; the main window can show it again.
    protected override void OnClosing(CancelEventArgs e)
    {
        if (Application.Current.MainWindow?.IsLoaded == true && !Application.Current.Dispatcher.HasShutdownStarted)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }
}
