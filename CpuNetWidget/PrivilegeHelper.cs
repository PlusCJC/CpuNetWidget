using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;

namespace CpuNetWidget;

internal static class PrivilegeHelper
{
    public static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception exception)
        {
            AppDiagnostics.Log("检测管理员权限失败。", exception);
            return false;
        }
    }

    public static bool TryRestartAsAdministrator()
    {
        try
        {
            var executable = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("无法确定程序路径。");
            executable = Path.GetFullPath(executable);
            if (!File.Exists(executable))
                throw new FileNotFoundException("找不到当前程序文件。", executable);
            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory
            });
            return true;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            AppDiagnostics.Log("用户取消了管理员权限请求。");
            return false; // User cancelled the UAC prompt.
        }
        catch (Exception exception)
        {
            AppDiagnostics.Log("使用管理员权限重启失败。", exception);
            return false;
        }
    }
}
