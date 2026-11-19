using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Assistente.Core.Text;

namespace Assistente.App.Tools.Sistema;

public sealed class ProcessesTool : ITool
{
    public string Name => "Processos";
    public string Category => "Sistema";
    public string Glyph => "";
    public int Order => 2;
    public FrameworkElement CreateView() => new ProcessesView();
}

public sealed class ProcessRow : INotifyPropertyChanged
{
    private double _memoryMb;
    private double _cpu;

    public ProcessRow(int pid, string name)
    {
        Pid = pid;
        Name = name;
    }

    public int Pid { get; }
    public string Name { get; }

    public double MemoryMb
    {
        get => _memoryMb;
        set => Set(ref _memoryMb, value);
    }

    public double Cpu
    {
        get => _cpu;
        set => Set(ref _cpu, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set(ref double field, double value, [CallerMemberName] string? name = null)
    {
        if (Math.Abs(field - value) < 0.05) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

internal sealed class ProcessesView : UserControl
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly ObservableCollection<ProcessRow> _items = new();
    private readonly Dictionary<int, ProcessRow> _rows = new();
    private readonly Dictionary<int, TimeSpan> _lastCpuTime = new();
    private readonly ICollectionView _view;
    private readonly DataGrid _grid;
    private readonly TextBox _filter = Ui.Input();
    private readonly TextBlock _summary = Ui.Caption("");
    private readonly TextBlock _error = Ui.Error();
    private DateTime _lastSample = DateTime.UtcNow;

    public ProcessesView()
    {
        _view = CollectionViewSource.GetDefaultView(_items);
        _view.Filter = o => _filter.Text.Length == 0 ||
                            ((ProcessRow)o).Name.Contains(_filter.Text, StringComparison.OrdinalIgnoreCase);
        _view.SortDescriptions.Add(new SortDescription(nameof(ProcessRow.MemoryMb), ListSortDirection.Descending));
        if (_view is ICollectionViewLiveShaping live && live.CanChangeLiveSorting)
        {
            foreach (var p in new[] { nameof(ProcessRow.MemoryMb), nameof(ProcessRow.Cpu), nameof(ProcessRow.Name), nameof(ProcessRow.Pid) })
                live.LiveSortingProperties.Add(p);
            live.IsLiveSorting = true;
        }
        _filter.TextChanged += (_, _) => _view.Refresh();

        _grid = new DataGrid { ItemsSource = _view };
        _grid.Columns.Add(Column("Processo", nameof(ProcessRow.Name), null, new DataGridLength(1, DataGridLengthUnitType.Star)));
        _grid.Columns.Add(Column("PID", nameof(ProcessRow.Pid), null, new DataGridLength(64)));
        _grid.Columns.Add(Column("Memória (MB)", nameof(ProcessRow.MemoryMb), "N1", new DataGridLength(100)));
        _grid.Columns.Add(Column("CPU %", nameof(ProcessRow.Cpu), "N1", new DataGridLength(64)));

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var buttons = Ui.Inline(
            Ui.Btn("Abrir local", OpenLocation),
            Ui.Btn("Finalizar", KillSelected, primary: true));
        buttons.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(buttons, Dock.Right);
        top.Children.Add(buttons);
        top.Children.Add(_filter);
        _filter.ToolTip = "Filtrar por nome";

        var layout = new DockPanel();
        var header = Ui.Stack(Ui.Title("Processos"), _summary, top, _error);
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(_grid);
        Content = layout;

        _timer.Tick += (_, _) => Refresh();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                Refresh();
                _timer.Start();
            }
            else
            {
                _timer.Stop();
            }
        };
    }

    private static DataGridTextColumn Column(string header, string path, string? format, DataGridLength width) => new()
    {
        Header = header,
        Width = width,
        Binding = new Binding(path) { StringFormat = format, ConverterCulture = Ui.PtBr },
    };

    private void Refresh()
    {
        var now = DateTime.UtcNow;
        var elapsedMs = (now - _lastSample).TotalMilliseconds;
        _lastSample = now;
        var seen = new HashSet<int>();
        long totalMemory = 0;

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var pid = process.Id;
                    seen.Add(pid);
                    var memory = process.WorkingSet64;
                    totalMemory += memory;

                    double cpu = 0;
                    try
                    {
                        var cpuTime = process.TotalProcessorTime;
                        if (_lastCpuTime.TryGetValue(pid, out var previous) && elapsedMs > 0)
                            cpu = (cpuTime - previous).TotalMilliseconds / elapsedMs / Environment.ProcessorCount * 100;
                        _lastCpuTime[pid] = cpuTime;
                    }
                    catch (Exception)
                    {
                        // Processos do sistema podem negar acesso ao tempo de CPU.
                    }

                    if (!_rows.TryGetValue(pid, out var row))
                    {
                        row = new ProcessRow(pid, process.ProcessName);
                        _rows[pid] = row;
                        _items.Add(row);
                    }
                    row.MemoryMb = memory / 1024.0 / 1024.0;
                    row.Cpu = Math.Clamp(cpu, 0, 100);
                }
                catch (Exception)
                {
                    // O processo terminou durante a leitura.
                }
            }
        }

        foreach (var pid in _rows.Keys.Where(p => !seen.Contains(p)).ToList())
        {
            _items.Remove(_rows[pid]);
            _rows.Remove(pid);
            _lastCpuTime.Remove(pid);
        }

        _summary.Text = $"{_items.Count} processos · {ByteSize.Format(totalMemory, Ui.PtBr)} em uso (working set)";
    }

    private ProcessRow? Selected => _grid.SelectedItem as ProcessRow;

    private void KillSelected() => Ui.Safe(_error, () =>
    {
        if (Selected is not { } row) throw new InvalidOperationException("Selecione um processo na lista.");
        if (!Ui.Confirm($"Finalizar \"{row.Name}\" (PID {row.Pid})?\nAlterações não salvas nesse programa serão perdidas."))
            return;

        try
        {
            using var process = Process.GetProcessById(row.Pid);
            process.Kill();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("Sem permissão para finalizar este processo. Execute o Assistente como administrador.");
        }
        catch (ArgumentException)
        {
            // O processo já terminou.
        }
        Refresh();
    });

    private void OpenLocation() => Ui.Safe(_error, () =>
    {
        if (Selected is not { } row) throw new InvalidOperationException("Selecione um processo na lista.");
        string? path;
        try
        {
            using var process = Process.GetProcessById(row.Pid);
            path = process.MainModule?.FileName;
        }
        catch (Exception)
        {
            path = null;
        }
        if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("Não foi possível obter o caminho deste processo.");
        Process.Start("explorer.exe", $"/select,\"{path}\"");
    });
}
