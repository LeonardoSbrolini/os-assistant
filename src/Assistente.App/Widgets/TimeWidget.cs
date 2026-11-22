using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Assistente.App.Services;
using Assistente.App.Tools.Tempo;

namespace Assistente.App.Widgets;

/// <summary>
/// Mini widget à esquerda do avatar com o timer (ou o cronômetro, se não houver timer):
/// tempo, pausar/continuar e, quando o timer acaba, recomeçar ou silenciar o alarme.
/// </summary>
public sealed class TimeWidget : MiniWidget
{
    private static TimerService Timer => TimerService.Instance;
    private static StopwatchService Watch => StopwatchService.Instance;

    private readonly TextBlock _icon = new()
    {
        FontSize = 14,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(6, 0, 6, 0),
    };
    private readonly TextBlock _time = new()
    {
        FontSize = 15,
        FontWeight = FontWeights.SemiBold,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 6, 0),
    };
    private readonly TextBlock _label = new()
    {
        FontSize = 11,
        MaxWidth = 110,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 6, 0),
    };
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal };
    private readonly Button _pause;
    private readonly Button _restart;
    private readonly Button _mute;
    private readonly Button _close;
    private bool _showingTimer;
    private bool _pulsing;

    public TimeWidget() : base(WidgetSide.Before)
    {
        _icon.FontFamily = (FontFamily)Application.Current.Resources["IconFont"];
        _icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        _time.FontFamily = (FontFamily)Application.Current.Resources["MonoFont"];
        _label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        _pause = IconButton("", "Pausar", TogglePause, primary: true);
        _restart = IconButton("", "Recomeçar", Timer.Restart, primary: true);
        _mute = IconButton("", "Silenciar alarme", Timer.MuteAlarm);
        _close = IconButton("", "Parar", Stop);
        _buttons.Children.Add(_pause);
        _buttons.Children.Add(_restart);
        _buttons.Children.Add(_mute);
        _buttons.Children.Add(_close);

        // Clicar no tempo abre a aba correspondente.
        var info = new StackPanel { Orientation = Orientation.Horizontal, Cursor = Cursors.Hand, Background = Brushes.Transparent };
        info.Children.Add(_icon);
        info.Children.Add(_time);
        info.Children.Add(_label);
        info.MouseLeftButtonUp += (_, _) =>
            MainWindow.Current?.ShowTool(_showingTimer ? TimerTool.ToolName : "Cronômetro");

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(info);
        row.Children.Add(_buttons);
        Pill.Child = row;
        Pill.Padding = new Thickness(4, 4, 4, 4);

        Timer.Changed += Refresh;
        Timer.Ticked += UpdateTime;
        Watch.Changed += Refresh;
        Watch.Ticked += UpdateTime;
        Refresh();
    }

    public override bool HasSomething => Timer.State != TimerState.Idle || Watch.IsActive;

    private void TogglePause()
    {
        if (_showingTimer) Timer.TogglePause();
        else Watch.Toggle();
    }

    private void Stop()
    {
        if (_showingTimer) Timer.Reset();
        else Watch.Reset();
    }

    private void Refresh()
    {
        _showingTimer = Timer.State != TimerState.Idle;
        var finished = _showingTimer && Timer.State == TimerState.Finished;
        var running = _showingTimer ? Timer.State == TimerState.Running : Watch.IsRunning;

        _icon.Text = _showingTimer ? (finished ? "" : "") : "";
        _label.Text = _showingTimer && Timer.Label.Length > 0 ? Timer.Label : "";
        _label.Visibility = _label.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        _pause.Visibility = finished ? Visibility.Collapsed : Visibility.Visible;
        _pause.Content = running ? "" : "";
        _pause.ToolTip = running ? "Pausar" : "Continuar";
        _restart.Visibility = finished ? Visibility.Visible : Visibility.Collapsed;
        _mute.Visibility = finished && Timer.AlarmRinging ? Visibility.Visible : Visibility.Collapsed;
        _close.ToolTip = finished ? "Fechar" : _showingTimer ? "Parar o timer" : "Zerar o cronômetro";

        _time.SetResourceReference(TextBlock.ForegroundProperty, finished ? "ErrorBrush" : "TextBrush");
        var pulse = finished && Timer.AlarmRinging || !running && !finished;
        if (pulse != _pulsing)
        {
            _pulsing = pulse;
            Pulse(_time, pulse);
        }
        UpdateTime();
        WidgetManager.Refresh();
    }

    private void UpdateTime()
    {
        string text;
        if (_showingTimer)
            text = Timer.State == TimerState.Finished ? "Acabou!" : TimerService.Format(Timer.Remaining);
        else
            text = StopwatchService.Short(Watch.Elapsed);
        if (_time.Text != text) _time.Text = text;
    }
}
