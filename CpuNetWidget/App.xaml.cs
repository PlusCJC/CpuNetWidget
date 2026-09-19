using System.Windows;

namespace CpuNetWidget;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settings = AppSettings.Load();
        if (settings.RunAsAdministrator && !PrivilegeHelper.IsAdministrator()
            && PrivilegeHelper.TryRestartAsAdministrator())
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            System.Windows.MessageBox.Show(
                $"程序发生未处理错误：\n\n{args.Exception.Message}",
                "CPU 网速悬浮窗",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            args.Handled = true;
        };

        new MainWindow().Show();
    }
}
