using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Threading;

namespace Assistente.App.Services;

/// <summary>Cronômetro compartilhado pela aba "Cronômetro" e pelo mini widget.</summary>
public sealed class StopwatchService
{
    public static StopwatchService Instance { get; } = new();

    private readonly Stopwatch _watch = new();
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly List<string> _laps = new();
    private TimeSpan _lastLap;

    private StopwatchService()
    {
        _tick.Tick += (_, _) => Ticked?.Invoke();
    }

    public bool IsRunning => _watch.IsRunning;
    public TimeSpan Elapsed => _watch.Elapsed;

    /// <summary>Foi iniciado e ainda não foi zerado.</summary>
    public bool IsActive => _watch.IsRunning || _watch.Elapsed > TimeSpan.Zero;

    /// <summary>Voltas, a mais recente primeiro.</summary>
    public IReadOnlyList<string> Laps => _laps;

    public event Action? Changed;
    public event Action? Ticked;

    public void Toggle()
    {
        if (_watch.IsRunning)
        {
            _watch.Stop();
            _tick.Stop();
        }
        else
        {
            _watch.Start();
            _tick.Start();
        }
        Changed?.Invoke();
    }

    public void Lap()
    {
        if (!_watch.IsRunning) return;
        var now = _watch.Elapsed;
        _laps.Insert(0, $"Volta {_laps.Count + 1,2}   {Format(now - _lastLap)}   total {Format(now)}");
        _lastLap = now;
        Changed?.Invoke();
    }

    public void Reset()
    {
        _watch.Reset();
        _tick.Stop();
        _lastLap = TimeSpan.Zero;
        _laps.Clear();
        Changed?.Invoke();
    }

    /// <summary>mm:ss (ou h:mm:ss), para o widget.</summary>
    public static string Short(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";

    public static string Format(TimeSpan t) =>
        t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds / 10:00}"
            : $"{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds / 10:00}";
}
