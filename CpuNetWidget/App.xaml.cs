using System.Windows;

namespace CpuNetWidget;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            AppDiagnostics.Log("未处理的界面线程异常。", args.Exception);
            System.Windows.MessageBox.Show(
                $"程序遇到无法恢复的错误，即将退出。\n\n诊断日志：\n{AppDiagnostics.LogPath}",
                "CPU 网速悬浮窗",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            args.Handled = true;
            Shutdown(1);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
                AppDiagnostics.Log("未处理的后台异常。", exception);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppDiagnostics.Log("未观察到的任务异常。", args.Exception);
            args.SetObserved();
        };

        var settings = AppSettings.Load();
        if (settings.RunAsAdministrator && !PrivilegeHelper.IsAdministrator()
            && PrivilegeHelper.TryRestartAsAdministrator())
        {
            Shutdown();
            return;
        }

        new MainWindow().Show();
    }
}
