using System;
using System.Windows;
using System.Windows.Controls;
using Assistente.App.Services;

namespace Assistente.App.Tools.Tempo;

public sealed class StopwatchTool : ITool
{
    public string Name => "Cronômetro";
    public string Category => "Tempo";
    public string Glyph => "\uE916";
    public int Order => 1;
    public FrameworkElement CreateView() => new StopwatchView();
}

/// <summary>Cronômetro com voltas. A contagem fica no StopwatchService (continua com o painel recolhido).</summary>
internal sealed class StopwatchView : UserControl
{
    private static StopwatchService Watch => StopwatchService.Instance;

    private readonly TextBlock _display = new()
    {
        Text = StopwatchService.Format(TimeSpan.Zero),
        FontSize = 52,
        FontWeight = FontWeights.Light,
        HorizontalAlignment = HorizontalAlignment.Center,
        FontFamily = (System.Windows.Media.FontFamily)Application.Current.Resources["MonoFont"],
    };
    private readonly TextBox _laps = Ui.Output(120);
    private readonly Button _startPause;
    private readonly Button _lap;

    public StopwatchView()
    {
        _startPause = Ui.Btn("Iniciar", Watch.Toggle, primary: true);
        _lap = Ui.Btn("Volta", Watch.Lap);

        Content = Ui.Scroll(Ui.Stack(
            Ui.Title("Cronômetro"),
            _display,
            Ui.Row(_startPause, _lap, Ui.Btn("Zerar", Watch.Reset)),
            Ui.Inline(Ui.Btn("Copiar voltas", () => Ui.Copy(_laps.Text))),
            Ui.Field("Voltas", _laps),
            Ui.Caption("Com o painel recolhido, o cronômetro aparece num mini widget à esquerda do avatar.")));

        Loaded += (_, _) =>
        {
            Watch.Changed += OnChanged;
            Watch.Ticked += OnTick;
            OnChanged();
        };
        Unloaded += (_, _) =>
        {
            Watch.Changed -= OnChanged;
            Watch.Ticked -= OnTick;
        };
    }

    private void OnChanged()
    {
        _startPause.Content = Watch.IsRunning ? "Pausar" : Watch.IsActive ? "Continuar" : "Iniciar";
        _lap.IsEnabled = Watch.IsRunning;
        _laps.Text = string.Join(Environment.NewLine, Watch.Laps);
        OnTick();
    }

    private void OnTick() => _display.Text = StopwatchService.Format(Watch.Elapsed);
}
