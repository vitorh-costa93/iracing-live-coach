using System.Windows;

namespace Ams2.ControlCenter;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            MessageBox.Show(e.Exception.Message, "AMS2 Control Center", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
    }
}
