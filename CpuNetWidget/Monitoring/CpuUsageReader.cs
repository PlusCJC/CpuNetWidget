using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CpuNetWidget.Monitoring;

internal sealed class CpuUsageReader
{
    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;
    private bool _hasPreviousSample;
    private bool _readFailureLogged;

    public double? ReadUsage()
    {
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
        {
            if (!_readFailureLogged)
            {
                AppDiagnostics.Log("读取 Windows CPU 时间失败。",
                    new Win32Exception(Marshal.GetLastWin32Error()));
                _readFailureLogged = true;
            }
            return null;
        }
        _readFailureLogged = false;

        var idle = ToUInt64(idleTime);
        var kernel = ToUInt64(kernelTime);
        var user = ToUInt64(userTime);

        if (!_hasPreviousSample)
        {
            _previousIdle = idle;
            _previousKernel = kernel;
            _previousUser = user;
            _hasPreviousSample = true;
            return null;
        }

        if (idle < _previousIdle || kernel < _previousKernel || user < _previousUser)
        {
            ResetTo(idle, kernel, user);
            return null;
        }

        var idleDelta = idle - _previousIdle;
        var kernelDelta = kernel - _previousKernel;
        var userDelta = user - _previousUser;
        if (ulong.MaxValue - kernelDelta < userDelta)
        {
            ResetTo(idle, kernel, user);
            return null;
        }
        var total = kernelDelta + userDelta;

        ResetTo(idle, kernel, user);

        if (total == 0 || idleDelta > total)
        {
            return null;
        }

        return Math.Clamp((total - idleDelta) * 100.0 / total, 0, 100);
    }

    public void Reset()
    {
        _hasPreviousSample = false;
        _readFailureLogged = false;
    }

    private void ResetTo(ulong idle, ulong kernel, ulong user)
    {
        _previousIdle = idle;
        _previousKernel = kernel;
        _previousUser = user;
        _hasPreviousSample = true;
    }

    private static ulong ToUInt64(FILETIME time) =>
        ((ulong)time.dwHighDateTime << 32) | time.dwLowDateTime;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out FILETIME lpIdleTime,
        out FILETIME lpKernelTime,
        out FILETIME lpUserTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }
}
