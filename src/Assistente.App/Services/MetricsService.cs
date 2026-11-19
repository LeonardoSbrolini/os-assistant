using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Assistente.App.Services;

/// <summary>
/// Coleta CPU (total e por núcleo), memória, disco e rede uma vez por segundo,
/// guardando o último minuto para os gráficos. Roda desde a abertura do app,
/// então os gráficos já aparecem com histórico.
/// </summary>
public sealed class MetricsService
{
    public const int HistoryLength = 60;

    public static MetricsService Instance { get; } = new();

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly CpuUsageSampler _cpu = new();
    private readonly PerCoreSampler _cores = new();
    private PerformanceCounter? _diskIdle;
    private bool _diskTried;
    private long _lastRx, _lastTx;
    private DateTime _lastNet;

    public History Cpu { get; } = new();
    public History Memory { get; } = new();
    public History Disk { get; } = new();
    public History NetReceive { get; } = new(); // bytes/s
    public History NetSend { get; } = new();    // bytes/s
    public List<History> Cores { get; } = new();

    public bool DiskAvailable => _diskIdle is not null;
    public MemoryInfo LastMemory { get; private set; }

    /// <summary>Disparado depois de cada coleta (na thread da interface).</summary>
    public event Action? Sampled;

    public void Start()
    {
        (_lastRx, _lastTx) = ReadNetworkBytes();
        _lastNet = DateTime.UtcNow;
        _timer.Tick += (_, _) => Sample();
        _timer.Start();
    }

    private void Sample()
    {
        Cpu.Add(_cpu.Sample());

        var cores = _cores.Sample();
        while (Cores.Count < cores.Length) Cores.Add(new History());
        for (var i = 0; i < cores.Length; i++) Cores[i].Add(cores[i]);

        LastMemory = SystemInfo.GetMemory();
        Memory.Add(LastMemory.UsedPercent);

        SampleDisk();
        SampleNetwork();

        Sampled?.Invoke();
    }

    private void SampleDisk()
    {
        if (!_diskTried)
        {
            _diskTried = true;
            try
            {
                // "% Idle Time" é mais confiável que "% Disk Time" (que passa de 100%).
                _diskIdle = new PerformanceCounter("PhysicalDisk", "% Idle Time", "_Total", readOnly: true);
                _diskIdle.NextValue();
            }
            catch (Exception)
            {
                _diskIdle = null; // Contadores indisponíveis (ex.: desativados no sistema).
            }
        }
        if (_diskIdle is null) return;
        try
        {
            Disk.Add(Math.Clamp(100 - _diskIdle.NextValue(), 0, 100));
        }
        catch (Exception)
        {
            _diskIdle = null;
        }
    }

    private void SampleNetwork()
    {
        var (rx, tx) = ReadNetworkBytes();
        var now = DateTime.UtcNow;
        var seconds = Math.Max(0.001, (now - _lastNet).TotalSeconds);
        NetReceive.Add(Math.Max(0, (rx - _lastRx) / seconds));
        NetSend.Add(Math.Max(0, (tx - _lastTx) / seconds));
        _lastRx = rx;
        _lastTx = tx;
        _lastNet = now;
    }

    private static (long Rx, long Tx) ReadNetworkBytes()
    {
        long rx = 0, tx = 0;
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;
                var stats = nic.GetIPStatistics();
                rx += stats.BytesReceived;
                tx += stats.BytesSent;
            }
        }
        catch (Exception)
        {
            // Ignora interfaces que somem durante a leitura.
        }
        return (rx, tx);
    }

    /// <summary>Histórico circular dos últimos 60 valores.</summary>
    public sealed class History
    {
        private readonly List<double> _values = new(HistoryLength);

        public IReadOnlyList<double> Values => _values;
        public double Last => _values.Count == 0 ? 0 : _values[^1];
        public double Peak => _values.Count == 0 ? 0 : _values.Max();

        public void Add(double value)
        {
            if (_values.Count == HistoryLength) _values.RemoveAt(0);
            _values.Add(value);
        }
    }
}

/// <summary>Uso de cada núcleo lógico via NtQuerySystemInformation.</summary>
internal sealed class PerCoreSampler
{
    private const int SystemProcessorPerformanceInformation = 8;
    private readonly int _count = Environment.ProcessorCount;
    private Sppi[] _previous;

    public PerCoreSampler() => _previous = Read() ?? Array.Empty<Sppi>();

    public double[] Sample()
    {
        var current = Read();
        if (current is null || current.Length != _previous.Length)
        {
            _previous = current ?? Array.Empty<Sppi>();
            return new double[current?.Length ?? 0];
        }

        var result = new double[current.Length];
        for (var i = 0; i < current.Length; i++)
        {
            var idle = current[i].IdleTime - _previous[i].IdleTime;
            var total = (current[i].KernelTime - _previous[i].KernelTime) + (current[i].UserTime - _previous[i].UserTime);
            result[i] = total <= 0 ? 0 : Math.Clamp((1.0 - (double)idle / total) * 100, 0, 100);
        }
        _previous = current;
        return result;
    }

    private Sppi[]? Read()
    {
        try
        {
            var buffer = new Sppi[_count];
            var size = Marshal.SizeOf<Sppi>() * _count;
            var status = NtQuerySystemInformation(SystemProcessorPerformanceInformation, buffer, size, out var returned);
            if (status != 0) return null;
            var n = returned / Marshal.SizeOf<Sppi>();
            return n == _count ? buffer : buffer.Take(n).ToArray();
        }
        catch (Exception)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Sppi
    {
        public long IdleTime;
        public long KernelTime;
        public long UserTime;
        public long DpcTime;
        public long InterruptTime;
        public int InterruptCount;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int infoClass, [Out] Sppi[] info, int length, out int returnLength);
}
