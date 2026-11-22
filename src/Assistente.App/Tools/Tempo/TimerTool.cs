using System;
using System.Windows;
using System.Windows.Controls;
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
/// Contagem regressiva. A contagem fica no TimerService, então continua com o painel
/// recolhido e pode ser controlada pelo mini widget ao lado do avatar.
/// </summary>
internal sealed class TimerView : UserControl
{
    private static TimerService Timer => TimerService.Instance;

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
    private readonly Button _mute;
    private readonly Button _restart;

    public TimerView()
    {
        _startPause = Ui.Btn("Iniciar", StartPause, primary: true);
        _mute = Ui.Btn("Silenciar alarme", Timer.MuteAlarm);
        _restart = Ui.Btn("Recomeçar", Timer.Restart);
        _status.HorizontalAlignment = HorizontalAlignment.Center;
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
            Ui.Row(_startPause, _restart, Ui.Btn("Zerar", Timer.Reset)),
            Ui.Inline(_mute),
            _error,
            Ui.Caption("O timer continua contando com o painel recolhido e aparece num mini widget à esquerda do avatar, " +
                       "onde dá para pausar. Quando acabar, o alarme toca até você silenciar (no máximo 1 minuto).")));

        Loaded += (_, _) =>
        {
            Timer.Changed += OnChanged;
            Timer.Ticked += OnTick;
            OnChanged();
        };
        Unloaded += (_, _) =>
        {
            Timer.Changed -= OnChanged;
            Timer.Ticked -= OnTick;
        };
    }

    private void SetPreset(int minutes)
    {
        if (Timer.State is TimerState.Running or TimerState.Paused) return;
        _minutes.Text = minutes.ToString();
        _seconds.Text = "0";
        StartPause();
    }

    private void PreviewInput()
    {
        if (Timer.State != TimerState.Idle) return;
        if (TryReadDuration(out var d)) _display.Text = TimerService.Format(d);
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
        switch (Timer.State)
        {
            case TimerState.Running:
            case TimerState.Paused:
                Timer.TogglePause();
                break;
            default:
                if (!TryReadDuration(out var duration))
                    throw new FormatException("Informe um tempo válido (minutos e/ou segundos).");
                Timer.Start(duration, _label.Text);
                break;
        }
    });

    private void OnChanged()
    {
        var state = Timer.State;
        _startPause.Content = state switch
        {
            TimerState.Running => "Pausar",
            TimerState.Paused => "Continuar",
            _ => "Iniciar",
        };
        _restart.Visibility = state == TimerState.Idle ? Visibility.Collapsed : Visibility.Visible;
        _mute.Visibility = Timer.AlarmRinging ? Visibility.Visible : Visibility.Collapsed;
        _display.Foreground = Ui.Res(state == TimerState.Finished ? "ErrorBrush" : "TextBrush");

        var name = Timer.Label.Length > 0 ? Timer.Label : "Timer";
        _status.Text = state switch
        {
            TimerState.Running => $"Termina às {Timer.EndsAt:HH:mm:ss}",
            TimerState.Paused => "Pausado",
            TimerState.Finished => $"{name}: tempo esgotado" + (Timer.AlarmRinging ? " · alarme tocando" : ""),
            _ => "",
        };

        if (state == TimerState.Idle)
        {
            _progress.Value = 0;
            if (TryReadDuration(out var d)) _display.Text = TimerService.Format(d);
        }
        else
        {
            OnTick();
        }
    }

    private void OnTick()
    {
        _display.Text = TimerService.Format(Timer.Remaining);
        _progress.Value = Timer.State == TimerState.Finished ? 1 : Timer.Progress;
    }
}
