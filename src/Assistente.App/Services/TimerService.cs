using System;
using System.Media;
using System.Windows.Threading;

namespace Assistente.App.Services;

public enum TimerState { Idle, Running, Paused, Finished }

/// <summary>
/// Timer (contagem regressiva) compartilhado pela aba "Timer" e pelo mini widget ao lado do avatar.
/// Ao terminar, o alarme toca em repetição até ser silenciado (no máximo 1 minuto).
/// </summary>
public sealed class TimerService
{
    public static TimerService Instance { get; } = new();

    private static readonly TimeSpan AlarmLimit = TimeSpan.FromSeconds(60);

    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer _alarm = new() { Interval = TimeSpan.FromSeconds(1.2) };
    private DateTime _endsAt;
    private DateTime _alarmStarted;
    private TimeSpan _remaining;

    private TimerService()
    {
        _tick.Tick += (_, _) => OnTick();
        _alarm.Tick += (_, _) => Ring();
    }

    public TimerState State { get; private set; } = TimerState.Idle;
    public TimeSpan Total { get; private set; }
    public string Label { get; private set; } = "";
    public bool AlarmRinging => _alarm.IsEnabled;
    public DateTime EndsAt => _endsAt;

    public TimeSpan Remaining => State switch
    {
        TimerState.Running => Max(_endsAt - DateTime.Now, TimeSpan.Zero),
        TimerState.Paused => _remaining,
        _ => TimeSpan.Zero,
    };

    public double Progress => Total.TotalSeconds > 0 ? Math.Clamp(1 - Remaining.TotalSeconds / Total.TotalSeconds, 0, 1) : 0;

    /// <summary>Mudou de estado (iniciou, pausou, terminou, silenciou...).</summary>
    public event Action? Changed;

    /// <summary>A cada ~200 ms enquanto conta.</summary>
    public event Action? Ticked;

    public void Start(TimeSpan duration, string label)
    {
        StopAlarm();
        Total = duration;
        Label = label.Trim();
        _remaining = duration;
        Run();
    }

    public void Pause()
    {
        if (State != TimerState.Running) return;
        _remaining = Max(_endsAt - DateTime.Now, TimeSpan.Zero);
        State = TimerState.Paused;
        _tick.Stop();
        Changed?.Invoke();
    }

    public void Resume()
    {
        if (State != TimerState.Paused) return;
        Run();
    }

    public void TogglePause()
    {
        if (State == TimerState.Running) Pause();
        else if (State == TimerState.Paused) Resume();
    }

    /// <summary>Começa de novo com o mesmo tempo.</summary>
    public void Restart()
    {
        if (Total <= TimeSpan.Zero) return;
        Start(Total, Label);
    }

    public void Reset()
    {
        StopAlarm();
        _tick.Stop();
        _remaining = TimeSpan.Zero;
        State = TimerState.Idle;
        Changed?.Invoke();
    }

    public void MuteAlarm()
    {
        if (!AlarmRinging) return;
        StopAlarm();
        Changed?.Invoke();
    }

    private void Run()
    {
        _endsAt = DateTime.Now + _remaining;
        State = TimerState.Running;
        _tick.Start();
        Changed?.Invoke();
        Ticked?.Invoke();
    }

    private void OnTick()
    {
        if (State != TimerState.Running) return;
        if (DateTime.Now >= _endsAt)
        {
            Finish();
            return;
        }
        Ticked?.Invoke();
    }

    private void Finish()
    {
        _tick.Stop();
        _remaining = TimeSpan.Zero;
        State = TimerState.Finished;

        var name = Label.Length > 0 ? Label : "Timer";
        TrayIcon.Notify("⏰ Tempo esgotado", $"{name} ({Format(Total)}) terminou.");

        _alarmStarted = DateTime.Now;
        _alarm.Start();
        Ring();
        Changed?.Invoke();
    }

    private void Ring()
    {
        if (DateTime.Now - _alarmStarted > AlarmLimit)
        {
            MuteAlarm();
            return;
        }
        SystemSounds.Exclamation.Play();
    }

    private void StopAlarm() => _alarm.Stop();

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    public static string Format(TimeSpan t)
    {
        var seconds = (long)Math.Ceiling(t.TotalSeconds);
        var h = seconds / 3600;
        var m = seconds % 3600 / 60;
        var s = seconds % 60;
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m:00}:{s:00}";
    }
}
