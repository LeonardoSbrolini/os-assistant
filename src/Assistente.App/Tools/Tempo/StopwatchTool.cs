using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Assistente.App.Tools.Tempo;

public sealed class StopwatchTool : ITool
{
    public string Name => "Cronômetro";
    public string Category => "Tempo";
    public string Glyph => "\uE916";
    public int Order => 1;
    public FrameworkElement CreateView() => new StopwatchView();
}

/// <summary>Cronômetro com voltas. Continua contando com o painel recolhido.</summary>
internal sealed class StopwatchView : UserControl
{
    private readonly Stopwatch _watch = new();
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly TextBlock _display = new()
    {
        Text = Format(TimeSpan.Zero),
        FontSize = 52,
        FontWeight = FontWeights.Light,
        HorizontalAlignment = HorizontalAlignment.Center,
        FontFamily = (System.Windows.Media.FontFamily)Application.Current.Resources["MonoFont"],
    };
    private readonly TextBox _laps = Ui.Output(120);
    private readonly Button _startPause;
    private readonly Button _lap;
    private TimeSpan _lastLap;
    private int _lapCount;

    public StopwatchView()
    {
        _startPause = Ui.Btn("Iniciar", StartPause, primary: true);
        _lap = Ui.Btn("Volta", Lap);
        _lap.IsEnabled = false;
        _tick.Tick += (_, _) => _display.Text = Format(_watch.Elapsed);

        Content = Ui.Scroll(Ui.Stack(
            Ui.Title("Cronômetro"),
            _display,
            Ui.Row(_startPause, _lap, Ui.Btn("Zerar", Reset)),
            Ui.Inline(Ui.Btn("Copiar voltas", () => Ui.Copy(_laps.Text))),
            Ui.Field("Voltas", _laps)));
    }

    private void StartPause()
    {
        if (_watch.IsRunning)
        {
            _watch.Stop();
            _tick.Stop();
            _display.Text = Format(_watch.Elapsed);
            _startPause.Content = "Continuar";
            _lap.IsEnabled = false;
        }
        else
        {
            _watch.Start();
            _tick.Start();
            _startPause.Content = "Pausar";
            _lap.IsEnabled = true;
        }
    }

    private void Lap()
    {
        var now = _watch.Elapsed;
        _lapCount++;
        var line = $"Volta {_lapCount,2}   {Format(now - _lastLap)}   total {Format(now)}";
        _laps.Text = line + (_laps.Text.Length > 0 ? Environment.NewLine + _laps.Text : "");
        _lastLap = now;
    }

    private void Reset()
    {
        _watch.Reset();
        _tick.Stop();
        _lastLap = TimeSpan.Zero;
        _lapCount = 0;
        _laps.Clear();
        _display.Text = Format(TimeSpan.Zero);
        _startPause.Content = "Iniciar";
        _lap.IsEnabled = false;
    }

    private static string Format(TimeSpan t) =>
        t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds / 10:00}"
            : $"{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds / 10:00}";
}
