using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Assistente.App.Services;
using Assistente.App.Tools.Tempo;

namespace Assistente.App.Widgets;

/// <summary>
/// Mini widget à esquerda do avatar com o timer (ou o cronômetro, se não houver timer):
/// anel de progresso colorido pelo estado, tempo, pausar/continuar e, quando o timer acaba,
/// recomeçar ou silenciar o alarme.
/// </summary>
public sealed class TimeWidget : MiniWidget
{
    private const double RingSize = 22;
    private const double RingThickness = 2.5;

    private static TimerService Timer => TimerService.Instance;
    private static StopwatchService Watch => StopwatchService.Instance;

    private readonly Path _arc = new() { StrokeThickness = RingThickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    private readonly TextBlock _icon = new()
    {
        FontSize = 9,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _time = new()
    {
        FontSize = 13.5,
        FontWeight = FontWeights.SemiBold,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(8, 0, 6, 0),
    };
    private readonly TextBlock _label = new()
    {
        FontSize = 11.5,
        Opacity = 0.7,
        MaxWidth = 110,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 6, 0),
    };
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _pause;
    private readonly Button _restart;
    private readonly Button _mute;
    private readonly Button _close;
    private bool _showingTimer;
    private bool _pulsing;
    private string _stateKey = "";
    private Brush _stateBrush = Brushes.White;

    public TimeWidget() : base(WidgetSide.Before)
    {
        _icon.FontFamily = (FontFamily)Application.Current.Resources["IconFont"];
        _time.FontFamily = (FontFamily)Application.Current.Resources["MonoFont"];

        _pause = IconButton("", "Pausar", TogglePause, primary: true);
        _restart = IconButton("", "Recomeçar", Timer.Restart, primary: true);
        _mute = IconButton("", "Silenciar alarme", Timer.MuteAlarm);
        _close = IconButton("", "Parar", Stop, size: 20);
        _close.Opacity = 0.6;
        _close.Margin = new Thickness(4, 0, 0, 0);
        _buttons.Children.Add(_pause);
        _buttons.Children.Add(_restart);
        _buttons.Children.Add(_mute);
        _buttons.Children.Add(_close);

        // Anel: trilho apagado + arco do progresso + ícone no meio.
        var ring = new Grid { Width = RingSize, Height = RingSize, VerticalAlignment = VerticalAlignment.Center };
        ring.Children.Add(new Ellipse { Stroke = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), StrokeThickness = RingThickness });
        ring.Children.Add(_arc);
        ring.Children.Add(_icon);

        // Clicar no tempo abre a aba correspondente.
        var info = new StackPanel { Orientation = Orientation.Horizontal, Cursor = Cursors.Hand, Background = Brushes.Transparent };
        info.Children.Add(ring);
        info.Children.Add(_time);
        info.Children.Add(_label);
        Tip(info, "Abrir a aba");
        info.MouseLeftButtonUp += (_, _) =>
            MainWindow.Current?.ShowTool(_showingTimer ? TimerTool.ToolName : "Cronômetro");

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(info);
        row.Children.Add(_buttons);
        Body = row;

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
        Tip(_pause, running ? "Pausar" : "Continuar");
        _restart.Visibility = finished ? Visibility.Visible : Visibility.Collapsed;
        _mute.Visibility = finished && Timer.AlarmRinging ? Visibility.Visible : Visibility.Collapsed;
        Tip(_close, finished ? "Fechar" : _showingTimer ? "Parar o timer" : "Zerar o cronômetro");

        // Cor pelo estado: rodando = destaque, pausado = amarelo, acabou = vermelho.
        var key = finished ? "ErrorBrush" : running ? "AccentBrush" : "WarningBrush";
        if (key != _stateKey)
        {
            _stateKey = key;
            _stateBrush = (Brush)Application.Current.Resources[key];
            _arc.Stroke = _stateBrush;
            _icon.Foreground = _stateBrush;
            var color = _stateBrush is SolidColorBrush solid ? solid.Color : Colors.White;
            Backdrop.Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromArgb(0x40, color.R, color.G, color.B), 0),
                    new(Color.FromArgb(0x00, color.R, color.G, color.B), 0.8),
                }, 0);
        }
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

    private string _lastText = "";
    private int _lastArc = -1;

    private void UpdateTime()
    {
        string text;
        double fraction;
        if (_showingTimer)
        {
            var finished = Timer.State == TimerState.Finished;
            text = finished ? "Acabou!" : TimerService.Format(Timer.Remaining);
            // Anel esvazia conforme o tempo passa.
            fraction = finished ? 1 : 1 - Timer.Progress;
        }
        else
        {
            text = StopwatchService.Short(Watch.Elapsed);
            // Cronômetro: o anel dá uma volta por minuto.
            fraction = Watch.Elapsed.TotalSeconds % 60 / 60;
        }
        if (_lastText != text)
        {
            _lastText = text;
            _time.Text = text;
        }

        var step = (int)Math.Round(fraction * 240); // redesenha só quando muda de verdade
        if (step != _lastArc)
        {
            _lastArc = step;
            _arc.Data = Arc(step / 240.0);
        }
    }

    /// <summary>Arco a partir do topo, no sentido horário (fração 0 a 1).</summary>
    private static Geometry Arc(double fraction)
    {
        var r = (RingSize - RingThickness) / 2;
        var c = RingSize / 2;
        if (fraction >= 0.999) return new EllipseGeometry(new Point(c, c), r, r);
        if (fraction <= 0.001) return Geometry.Empty;
        var angle = fraction * 2 * Math.PI;
        var start = new Point(c, c - r);
        var end = new Point(c + r * Math.Sin(angle), c - r * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry { Figures = { figure } };
        geometry.Freeze();
        return geometry;
    }
}
