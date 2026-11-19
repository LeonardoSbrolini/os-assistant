using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Assistente.App.Services;
using Assistente.Core.Text;

namespace Assistente.App.Tools.Sistema;

public sealed class SystemOverviewTool : ITool
{
    public string Name => "Visão geral";
    public string Category => "Sistema";
    public string Glyph => "";
    public int Order => 0;
    public FrameworkElement CreateView() => new SystemOverviewView();
}

internal sealed class SystemOverviewView : UserControl
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly TextBlock _cpuText = new();
    private readonly ProgressBar _cpuBar = new() { Maximum = 100 };
    private readonly TextBlock _ramText = new();
    private readonly ProgressBar _ramBar = new() { Maximum = 100 };
    private readonly LineGraph _cpuGraph = new() { Height = 60, ShowGrid = false };
    private readonly LineGraph _ramGraph = new() { Height = 60, ShowGrid = false };
    private readonly TextBlock _uptime = Ui.Caption("");
    private readonly StackPanel _disks = new();
    private int _ticks;

    public SystemOverviewView()
    {
        var info = SystemInfo.GetStatic();

        Content = Ui.Scroll(Ui.Stack(
            Ui.Title("Visão geral"),
            Ui.Card("Este computador", "\uE7F4", Ui.KeyValues(
                ("Computador", info.MachineName),
                ("Usuário", info.UserName),
                ("Sistema", info.OsName),
                ("Processador", info.CpuName),
                ("Núcleos lógicos", info.LogicalCores.ToString())), _uptime),
            Ui.Row(
                Ui.Card("Processador", "\uE950", _cpuText, _cpuBar, _cpuGraph),
                Ui.Card("Memória RAM", "\uE964", _ramText, _ramBar, _ramGraph)),
            Ui.Card("Discos e volumes", "\uEDA2", _disks)));

        _timer.Tick += (_, _) => Refresh();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _ticks = 0;
                Refresh();
                _timer.Start();
            }
            else
            {
                _timer.Stop();
            }
        };
    }

    private void Refresh()
    {
        var metrics = MetricsService.Instance;
        var cpu = metrics.Cpu.Last;
        _cpuGraph.SetValues(metrics.Cpu.Values);
        _ramGraph.SetValues(metrics.Memory.Values);
        _cpuText.Text = $"{cpu:0}% em uso";
        SetBar(_cpuBar, cpu);

        var mem = SystemInfo.GetMemory();
        _ramText.Text = $"{ByteSize.Format(mem.UsedBytes, Ui.PtBr)} de {ByteSize.Format(mem.TotalBytes, Ui.PtBr)} " +
                        $"({mem.UsedPercent:0}%) · {ByteSize.Format(mem.AvailableBytes, Ui.PtBr)} livres";
        SetBar(_ramBar, mem.UsedPercent);

        var up = SystemInfo.Uptime;
        _uptime.Text = $"Ligado há {(int)up.TotalDays}d {up.Hours}h {up.Minutes}min";

        // Discos mudam pouco: atualiza a cada 5 segundos.
        if (_ticks++ % 5 == 0) RefreshDisks();
    }

    private void RefreshDisks()
    {
        _disks.Children.Clear();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady) continue;
                var used = drive.TotalSize - drive.TotalFreeSpace;
                var percent = drive.TotalSize == 0 ? 0 : used * 100.0 / drive.TotalSize;
                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? DriveTypeName(drive.DriveType) : drive.VolumeLabel;

                var bar = new ProgressBar { Maximum = 100, Margin = new Thickness(0, 4, 0, 12) };
                SetBar(bar, percent);

                _disks.Children.Add(new TextBlock
                {
                    Text = $"{drive.Name.TrimEnd('\\')}  {label} ({drive.DriveFormat})",
                    FontWeight = FontWeights.SemiBold,
                });
                _disks.Children.Add(Ui.Caption(
                    $"{ByteSize.Format(used, Ui.PtBr)} usados de {ByteSize.Format(drive.TotalSize, Ui.PtBr)} · " +
                    $"{ByteSize.Format(drive.TotalFreeSpace, Ui.PtBr)} livres ({percent:0}%)"));
                _disks.Children.Add(bar);
            }
            catch (Exception)
            {
                // Unidade removida ou sem permissão: ignora.
            }
        }
    }

    private static void SetBar(ProgressBar bar, double percent)
    {
        Anim.Bar(bar, percent);
        bar.Foreground = Ui.Res(percent >= 90 ? "ErrorBrush" : percent >= 75 ? "WarningBrush" : "AccentBrush");
    }

    private static string DriveTypeName(DriveType type) => type switch
    {
        DriveType.Fixed => "Disco local",
        DriveType.Removable => "Removível",
        DriveType.Network => "Rede",
        DriveType.CDRom => "CD/DVD",
        DriveType.Ram => "RAM disk",
        _ => "Unidade",
    };
}
