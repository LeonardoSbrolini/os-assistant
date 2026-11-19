using System;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Assistente.App.Services;
using Assistente.Core.Text;

namespace Assistente.App.Tools.Tempo;

public sealed class TimerTool : ITool
{
    public const string ToolName = "Timer";
    public string Name => ToolName;
    public string Category => "Tempo";
    public string Glyph => "\uE823";
    public int Order => 0;
    public FrameworkElement CreateView() => new TimerView();
}

/// <summary>
/// Contagem regressiva. Continua rodando com o painel recolhido e,
/// ao terminar, toca um som, mostra uma notificação e abre o painel.
/// </summary>
internal sealed class TimerView : UserControl
{
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer _alarm = new() { Interval = TimeSpan.FromSeconds(1.2) };
    private readonly TextBox _minutes = Ui.Input("5");
    private readonly TextBox _seconds = Ui.Input("0");
    private readonly TextBox _label = Ui.Input();
    private readonly TextBlock _display = new()
    {
        Text = "05:00",
        FontSize = 56,
        FontWeight = FontWeights.Light,
        HorizontalAlignment = HorizontalAlignment.Center,
        FontFamily = (System.Windows.Media.FontFamily)Application.Current.Resources["MonoFont"],
    };
    private readonly ProgressBar _progress = new() { Maximum = 1 };
    private readonly TextBlock _status = Ui.Caption("");
    private readonly TextBlock _error = Ui.Error();
    private readonly Button _startPause;

    private TimeSpan _total;
    private TimeSpan _remaining;
    private DateTime _endsAt;
    private bool _running;
    private int _alarmCount;

    public TimerView()
    {
        _startPause = Ui.Btn("Iniciar", StartPause, primary: true);
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _tick.Tick += (_, _) => Update();
        _alarm.Tick += (_, _) => RingAgain();
        Ui.OnEnter(StartPause, _minutes, _seconds);
        _minutes.TextChanged += (_, _) => PreviewInput();
        _seconds.TextChanged += (_, _) => PreviewInput();

        var presets = Ui.Inline();
        foreach (var m in new[] { 1, 3, 5, 10, 15, 25, 30, 60 })
        {
            var minutes = m;
            var button = Ui.Btn($"{m} min", () => SetPreset(minutes));
            button.Margin = new Thickness(0, 0, 6, 6);
            presets.Children.Add(button);
        }

        _label.ToolTip = "Ex.: Café, Reunião, Forno";

        Content = Ui.Scroll(Ui.Stack(
            Ui.Title("Timer"),
            _display,
            _progress,
            _status,
            Ui.Row(Ui.Field("Minutos", _minutes), Ui.Field("Segundos", _seconds), Ui.Field("Nome (opcional)", _label)),
            presets,
            Ui.Row(_startPause, Ui.Btn("Zerar", Reset)),
            _error,
            Ui.Caption("O timer continua contando com o painel recolhido. Quando acabar, você recebe um aviso e o painel abre sozinho.")));
    }

    private void SetPreset(int minutes)
    {
        if (_running) return;
        _minutes.Text = minutes.ToString();
        _seconds.Text = "0";
        StartPause();
    }

    private void PreviewInput()
    {
        if (_running || _remaining > TimeSpan.Zero) return;
        if (TryReadDuration(out var d)) _display.Text = Format(d);
    }

    private bool TryReadDuration(out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        var m = 0.0;
        var s = 0.0;
        if (_minutes.Text.Trim().Length > 0 && !NumberParser.TryParse(_minutes.Text, out m)) return false;
        if (_seconds.Text.Trim().Length > 0 && !NumberParser.TryParse(_seconds.Text, out s)) return false;
        if (m < 0 || s < 0) return false;
        duration = TimeSpan.FromSeconds(Math.Round(m * 60 + s));
        return duration > TimeSpan.Zero && duration < TimeSpan.FromHours(100);
    }

    private void StartPause() => Ui.Safe(_error, () =>
    {
        StopAlarm();
        if (_running)
        {
            // Pausar
            _remaining = _endsAt - DateTime.Now;
            _running = false;
            _tick.Stop();
            _startPause.Content = "Continuar";
            _status.Text = "Pausado";
            return;
        }

        if (_remaining <= TimeSpan.Zero)
        {
            if (!TryReadDuration(out var duration))
                throw new FormatException("Informe um tempo válido (minutos e/ou segundos).");
            _total = duration;
            _remaining = duration;
        }

        _endsAt = DateTime.Now + _remaining;
        _running = true;
        _tick.Start();
        _startPause.Content = "Pausar";
        _status.Text = $"Termina às {_endsAt:HH:mm:ss}";
        Update();
    });

    private void Reset()
    {
        StopAlarm();
        _running = false;
        _tick.Stop();
        _remaining = TimeSpan.Zero;
        _progress.Value = 0;
        _startPause.Content = "Iniciar";
        _status.Text = "";
        _display.Foreground = Ui.Res("TextBrush");
        if (TryReadDuration(out var d)) _display.Text = Format(d);
    }

    private void Update()
    {
        var left = _endsAt - DateTime.Now;
        if (left <= TimeSpan.Zero)
        {
            Finish();
            return;
        }
        _display.Text = Format(left);
        _progress.Value = _total.TotalSeconds > 0 ? 1 - left.TotalSeconds / _total.TotalSeconds : 0;
    }

    private void Finish()
    {
        _running = false;
        _tick.Stop();
        _remaining = TimeSpan.Zero;
        _display.Text = "00:00";
        _display.Foreground = Ui.Res("ErrorBrush");
        _progress.Value = 1;
        _startPause.Content = "Iniciar";

        var name = string.IsNullOrWhiteSpace(_label.Text) ? "Timer" : _label.Text.Trim();
        _status.Text = $"{name}: tempo esgotado às {DateTime.Now:HH:mm}";

        TrayIcon.Notify("⏰ Tempo esgotado", $"{name} ({Format(_total)}) terminou.");
        MainWindow.Current?.ShowTool(TimerTool.ToolName);

        _alarmCount = 0;
        RingAgain();
        _alarm.Start();
    }

    private void RingAgain()
    {
        SystemSounds.Exclamation.Play();
        if (++_alarmCount >= 5) _alarm.Stop();
    }

    private void StopAlarm()
    {
        _alarm.Stop();
        _display.Foreground = Ui.Res("TextBrush");
    }

    private static string Format(TimeSpan t)
    {
        var seconds = (long)Math.Ceiling(t.TotalSeconds);
        var h = seconds / 3600;
        var m = seconds % 3600 / 60;
        var s = seconds % 60;
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m:00}:{s:00}";
    }
}
