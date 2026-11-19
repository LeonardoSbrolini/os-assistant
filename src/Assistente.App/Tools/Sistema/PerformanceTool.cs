using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Assistente.App.Services;
using Assistente.Core.Text;

namespace Assistente.App.Tools.Sistema;

public sealed class PerformanceTool : ITool
{
    public string Name => "Desempenho";
    public string Category => "Sistema";
    public string Glyph => "";
    public int Order => 1;
    public FrameworkElement CreateView() => new PerformanceView();
}

/// <summary>Gráficos ao vivo no estilo da aba Desempenho do Gerenciador de Tarefas.</summary>
internal sealed class PerformanceView : UserControl
{
    private readonly MetricsService _metrics = MetricsService.Instance;

    private readonly TextBlock _cpuValue = Ui.Big();
    private readonly TextBlock _cpuInfo = Ui.Caption("");
    private readonly LineGraph _cpuGraph = new() { Height = 110 };
    private readonly UniformGrid _coreGrid = new() { Columns = 4 };
    private readonly List<(LineGraph Graph, TextBlock Label)> _cores = new();

    private readonly TextBlock _memValue = Ui.Big();
    private readonly TextBlock _memInfo = Ui.Caption("");
    private readonly LineGraph _memGraph = new() { Height = 90 };

    private readonly TextBlock _diskValue = Ui.Big();
    private readonly LineGraph _diskGraph = new() { Height = 80 };
    private readonly Border _diskCard;

    private readonly TextBlock _netValue = Ui.Big();
    private readonly TextBlock _netInfo = Ui.Caption("");
    private readonly LineGraph _netGraph = new() { Height = 80, Maximum = null };

    public PerformanceView()
    {
        _cpuValue.FontSize = _memValue.FontSize = _diskValue.FontSize = _netValue.FontSize = 20;
        _coreGrid.Columns = Environment.ProcessorCount switch { <= 4 => 2, <= 8 => 4, <= 16 => 4, _ => 6 };

        _diskCard = Ui.Card("Disco (atividade)", "", _diskValue, _diskGraph);
        _diskCard.Visibility = Visibility.Collapsed;

        Content = Ui.Scroll(Ui.Stack(
            Ui.Title("Desempenho"),
            Ui.Caption("Último minuto, atualizado a cada segundo."),
            Ui.Card("Processador", "", _cpuValue, _cpuInfo, _cpuGraph,
                Ui.Caption("Uso por núcleo lógico"), _coreGrid),
            Ui.Card("Memória", "", _memValue, _memInfo, _memGraph),
            _diskCard,
            Ui.Card("Rede", "", _netValue, _netInfo, _netGraph)));

        _metrics.Sampled += OnSampled;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) Refresh();
        };
    }

    private void OnSampled()
    {
        if (IsVisible) Refresh();
    }

    private void Refresh()
    {
        // CPU
        _cpuValue.Text = $"{_metrics.Cpu.Last:0}%";
        _cpuInfo.Text = $"Pico no último minuto: {_metrics.Cpu.Peak:0}% · {Environment.ProcessorCount} núcleos lógicos";
        _cpuGraph.SetValues(_metrics.Cpu.Values);

        while (_cores.Count < _metrics.Cores.Count)
        {
            var label = new TextBlock { FontSize = 10, Margin = new Thickness(2, 0, 0, 1) };
            label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            var graph = new LineGraph { Height = 34, ShowGrid = false };
            var cell = new StackPanel { Margin = new Thickness(0, 0, 6, 6) };
            cell.Children.Add(label);
            cell.Children.Add(graph);
            _coreGrid.Children.Add(cell);
            _cores.Add((graph, label));
            Anim.FadeUp(cell, _cores.Count * 15, 6);
        }
        for (var i = 0; i < _cores.Count && i < _metrics.Cores.Count; i++)
        {
            var core = _metrics.Cores[i];
            _cores[i].Label.Text = $"Núcleo {i} · {core.Last:0}%";
            _cores[i].Graph.SetValues(core.Values);
        }

        // Memória
        var mem = _metrics.LastMemory;
        _memValue.Text = $"{ByteSize.Format(mem.UsedBytes, Ui.PtBr)} / {ByteSize.Format(mem.TotalBytes, Ui.PtBr)}";
        _memInfo.Text = $"{mem.UsedPercent:0}% em uso · {ByteSize.Format(mem.AvailableBytes, Ui.PtBr)} disponíveis";
        _memGraph.SetValues(_metrics.Memory.Values);

        // Disco
        if (_metrics.DiskAvailable)
        {
            _diskCard.Visibility = Visibility.Visible;
            _diskValue.Text = $"{_metrics.Disk.Last:0}% ativo";
            _diskGraph.SetValues(_metrics.Disk.Values);
        }

        // Rede
        _netValue.Text = $"↓ {Speed(_metrics.NetReceive.Last)}   ↑ {Speed(_metrics.NetSend.Last)}";
        _netInfo.Text = "Azul: recebendo · Laranja: enviando";
        _netGraph.SetValues(_metrics.NetReceive.Values, _metrics.NetSend.Values);
    }

    private static string Speed(double bytesPerSecond) => ByteSize.Format(bytesPerSecond, Ui.PtBr) + "/s";
}
