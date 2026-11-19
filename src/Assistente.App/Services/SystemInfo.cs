using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Assistente.App.Services;

public sealed record StaticSystemInfo(string MachineName, string UserName, string OsName, string CpuName, int LogicalCores);

public readonly record struct MemoryInfo(ulong TotalBytes, ulong AvailableBytes)
{
    public ulong UsedBytes => TotalBytes - AvailableBytes;
    public double UsedPercent => TotalBytes == 0 ? 0 : UsedBytes * 100.0 / TotalBytes;
}

/// <summary>Leituras do sistema via APIs nativas do Windows (sem dependências externas).</summary>
public static class SystemInfo
{
    public static StaticSystemInfo GetStatic()
    {
        var cpu = Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0",
            "ProcessorNameString", null) as string;

        return new StaticSystemInfo(
            Environment.MachineName,
            Environment.UserName,
            GetOsName(),
            cpu?.Trim() ?? "Desconhecido",
            Environment.ProcessorCount);
    }

    private static string GetOsName()
    {
        const string key = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        var build = Environment.OSVersion.Version.Build;
        // ProductName continua dizendo "Windows 10" no Windows 11; o build é mais confiável.
        var product = Registry.GetValue(key, "ProductName", null) as string ?? "Windows";
        if (build >= 22000) product = product.Replace("Windows 10", "Windows 11");
        var display = Registry.GetValue(key, "DisplayVersion", null) as string;
        return display is null ? $"{product} (build {build})" : $"{product} {display} (build {build})";
    }

    public static MemoryInfo GetMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status)
            ? new MemoryInfo(status.TotalPhys, status.AvailPhys)
            : default;
    }

    public static TimeSpan Uptime => TimeSpan.FromMilliseconds(Environment.TickCount64);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}

/// <summary>Uso total de CPU, calculado pela diferença entre duas leituras de GetSystemTimes.</summary>
public sealed class CpuUsageSampler
{
    private long _idle, _kernel, _user;

    public CpuUsageSampler() => Sample();

    /// <returns>Percentual de uso (0–100) desde a última chamada.</returns>
    public double Sample()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;

        var idleDelta = idle - _idle;
        var total = (kernel - _kernel) + (user - _user); // "kernel" já inclui o tempo ocioso
        _idle = idle;
        _kernel = kernel;
        _user = user;

        return total <= 0 ? 0 : Math.Clamp((1.0 - (double)idleDelta / total) * 100.0, 0, 100);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);
}
