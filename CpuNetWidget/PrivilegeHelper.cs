using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace CpuNetWidget;

internal static class PrivilegeHelper
{
    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static bool TryRestartAsAdministrator()
    {
        try
        {
            var executable = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("无法确定程序路径。");
            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = true,
                Verb = "runas"
            });
            return true;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return false; // User cancelled the UAC prompt.
        }
        catch
        {
            return false;
        }
    }
}
