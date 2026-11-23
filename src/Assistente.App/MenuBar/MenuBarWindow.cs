using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Assistente.App.Docking;
using Assistente.App.Services;
using Assistente.App.Tools;
using Assistente.Core.Text;
using Forms = System.Windows.Forms;

namespace Assistente.App.MenuBar;

/// <summary>
/// Barra fina no topo da tela (como a barra de menus do Mac). Aparece quando não há janela
/// por cima (área de trabalho) ou ao levar o mouse até o topo. Cada item abre um popover
/// com mais detalhes ao passar o mouse.
/// </summary>
public sealed class MenuBarWindow : Window
{
    public const double BarHeight = 34;

    public static MenuBarWindow? Current { get; private set; }

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly AppSettings _settings;
    private readonly Border _bar = new();
    private readonly TranslateTransform _slide = new();
    private readonly StackPanel _left = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _right = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly IslandPopover _popover;
    private readonly Grid _compact = new();
    private readonly ContentControl _detail = new() { Focusable = false, Visibility = Visibility.Collapsed, Margin = new Thickness(8, 4, 8, 10) };
    private readonly StackPanel _island = new();
    private readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromMilliseconds(120) };

    // Itens da direita
    private readonly Border _timerItem;
    private readonly TextBlock _timerText = Text("", 12.5, mono: true);
    private readonly TextBlock _timerGlyph = Glyph("");
    private readonly Border _musicItem;
    private readonly TextBlock _musicText = Text("", 12.5);
    private readonly Border _netItem;
    private readonly TextBlock _netText = Text("", 11.5);
    private readonly Border _cpuItem;
    private readonly LineGraph _cpuSpark = new() { Width = 46, Height = 14, ShowGrid = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _cpuText = Text("", 9.5);
    private readonly Border _ramItem;
    private readonly TextBlock _ramText = Text("", 12.5);
    private readonly Border _batteryItem;
    private readonly TextBlock _batteryGlyph = Glyph("");
    private readonly TextBlock _batteryText = Text("", 12.5);
    private readonly Border _clockItem;
    private readonly TextBlock _clockText = Text("", 12.5, bold: true);

    private bool _hidden;
    private DateTime _wantHideSince = DateTime.MaxValue;
    private static string? _cpuName;

    private MenuBarWindow(AppSettings settings)
    {
        _settings = settings;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        UseLayoutRounding = true;
        Title = "Assistente Barra";
        FontFamily = new FontFamily("Segoe UI");
        Foreground = Brushes.White;

        // "Dynamic Island": uma pílula preta no centro do topo que cresce para mostrar detalhes.
        _popover = new IslandPopover(this);
        _bar.Height = BarHeight;
        _bar.Width = 200;
        _bar.HorizontalAlignment = HorizontalAlignment.Center;
        _bar.VerticalAlignment = VerticalAlignment.Top;
        _bar.Margin = new Thickness(0, 6, 0, 0);
        _bar.Background = new SolidColorBrush(Color.FromArgb(0xFA, 0x00, 0x00, 0x00));
        _bar.BorderBrush = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
        _bar.BorderThickness = new Thickness(1);
        _bar.CornerRadius = new CornerRadius(BarHeight / 2);
        _bar.Padding = new Thickness(8, 0, 8, 0);
        _bar.ClipToBounds = true;
        _bar.RenderTransform = _slide;
        _bar.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.35 };

        _compact.Height = BarHeight - 2;
        _compact.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _compact.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 24 });
        _compact.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_right, 2);
        _compact.Children.Add(_left);
        _compact.Children.Add(_right);
        _island.Children.Add(_compact);
        _island.Children.Add(_detail);
        _bar.Child = _island;
        Content = _bar;

        BuildLeft();

        _timerItem = Item(Row(_timerGlyph, _timerText), TimerPopover);
        _musicItem = Item(Row(Glyph(""), _musicText), MusicPopover);
        _netItem = Item(Row(Glyph(""), _netText), NetworkPopover);
        var cpuLabel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) };
        cpuLabel.Children.Add(Text("CPU", 8.5, bold: true));
        cpuLabel.Children.Add(_cpuText);
        _cpuText.FontWeight = FontWeights.SemiBold;
        _cpuItem = Item(Row(_cpuSpark, cpuLabel), CpuPopover);
        _ramItem = Item(Row(Glyph(""), _ramText), CpuPopover);
        _batteryItem = Item(Row(_batteryGlyph, _batteryText), BatteryPopover);
        _clockItem = Item(Row(Glyph(""), _clockText), CalendarPopover);
        foreach (var item in new[] { _timerItem, _musicItem, _cpuItem, _batteryItem, _clockItem })
            _right.Children.Add(item);

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            MakeToolWindow(_hwnd);
            HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        };
        _watch.Tick += (_, _) => Watch();
        MetricsService.Instance.Sampled += Update;
        TimerService.Instance.Ticked += UpdateTimer;
        TimerService.Instance.Changed += UpdateTimer;
        StopwatchService.Instance.Ticked += UpdateTimer;
        StopwatchService.Instance.Changed += UpdateTimer;
        MediaService.Instance.Changed += UpdateMusic;
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.WorkArea) && !_appBar) Dispatcher.BeginInvoke(new Action(Place));
        };
        Closed += (_, _) =>
        {
            ReleaseSpace();
            _watch.Stop();
            _popover.Close();
            MetricsService.Instance.Sampled -= Update;
            TimerService.Instance.Ticked -= UpdateTimer;
            TimerService.Instance.Changed -= UpdateTimer;
            StopwatchService.Instance.Ticked -= UpdateTimer;
            StopwatchService.Instance.Changed -= UpdateTimer;
            MediaService.Instance.Changed -= UpdateMusic;
        };
    }

    // ---------- Ligar / desligar ----------

    public static void Apply(AppSettings settings)
    {
        if (!settings.MenuBarEnabled)
        {
            Current?.Close();
            Current = null;
            return;
        }
        if (Current is not null) return;
        Current = new MenuBarWindow(settings);
        Current.Place();
        Current.Show();
        Current.Update();
        Current.UpdateTimer();
        Current.UpdateMusic();
        Current._watch.Start();
        Current.Loaded += (_, _) => Current?.FitCompact();
        Current.BringAvatarToFront();
    }

    // ---------- Espaço reservado no topo ----------

    private const int AppBarCallback = 0x0400 + 0x52; // WM_USER + n
    private IntPtr _hwnd;
    private bool _appBar;
    private DateTime _lastBarChange = DateTime.MinValue;

    /// <summary>
    /// A barra fica no topo, por cima de tudo, mas só aparece na área de trabalho (sem nenhum
    /// app na frente). Não reserva espaço: com um app aberto ela some e o app usa a tela toda.
    /// </summary>
    private void Place()
    {
        if (_hwnd == IntPtr.Zero) _hwnd = new WindowInteropHelper(this).EnsureHandle();
        ReleaseSpace(); // versões antigas reservavam a faixa do topo
        var area = SystemParameters.WorkArea;
        Width = IslandMaxWidth + 40;
        Height = IslandMaxHeight + 40;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top;
    }

    private const double IslandMaxWidth = 560;
    private const double IslandMaxHeight = 520;

    private void ReleaseSpace()
    {
        if (_appBar && _hwnd != IntPtr.Zero) Native.UnregisterBar(_hwnd);
        _appBar = false;
    }

    /// <summary>Devolve o espaço ao fechar o app (chamado também em caso de erro).</summary>
    public static void ReleaseSafe()
    {
        try
        {
            Current?.ReleaseSpace();
        }
        catch (Exception)
        {
            // Saindo de qualquer jeito.
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == AppBarCallback && _appBar)
        {
            switch (wParam.ToInt32())
            {
                case 1 when DateTime.Now - _lastBarChange > TimeSpan.FromMilliseconds(500): // ABN_POSCHANGED
                    Dispatcher.BeginInvoke(new Action(Place));
                    break;
                case 2: // ABN_FULLSCREENAPP: vídeo/jogo em tela cheia
                    _fullscreen = lParam != IntPtr.Zero;
                    break;
            }
        }
        return IntPtr.Zero;
    }

    private bool _fullscreen;

    // ---------- Lado esquerdo: nome e atalhos ----------

    private void BuildLeft()
    {
        var logo = new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(5),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        logo.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        if (Avatars.Find(_settings.Avatar)?.Image is { } avatar)
        {
            logo.Background = Brushes.Transparent;
            logo.Child = new Image { Source = avatar, Width = 18, Height = 18 };
        }

        logo.Margin = new Thickness(0);
        logo.Width = logo.Height = 20;
        logo.CornerRadius = new CornerRadius(10);
        var home = Item(logo, AssistantPopover);
        home.ToolTip = null;
        home.MouseLeftButtonUp += (_, _) =>
        {
            _popover.Close();
            MainWindow.Current?.ExpandPinned();
        };
        _left.Children.Add(home);
    }

    // ---------- Atualizações ----------

    private void Update()
    {
        var m = MetricsService.Instance;
        _cpuSpark.SetValues(m.Cpu.Values);
        _cpuText.Text = $"{m.Cpu.Last:0}%";
        _ramText.Text = $"{m.LastMemory.UsedPercent:0}%";
        _netText.Text = "↓ " + Speed(m.NetReceive.Last);

        var power = Forms.SystemInformation.PowerStatus;
        var hasBattery = power.BatteryChargeStatus != Forms.BatteryChargeStatus.NoSystemBattery && power.BatteryLifePercent <= 1;
        _batteryItem.Visibility = hasBattery ? Visibility.Visible : Visibility.Collapsed;
        if (hasBattery)
        {
            _batteryText.Text = $"{power.BatteryLifePercent * 100:0}%";
            _batteryGlyph.Text = power.PowerLineStatus == Forms.PowerLineStatus.Online ? "" : "";
        }

        _clockText.Text = DateTime.Now.ToString("HH:mm", PtBr);
        _popover.Refresh();
    }

    private void UpdateTimer()
    {
        var timer = TimerService.Instance;
        var watch = StopwatchService.Instance;
        if (timer.State != TimerState.Idle)
        {
            _timerItem.Visibility = Visibility.Visible;
            _timerGlyph.Text = "";
            _timerText.Text = timer.State == TimerState.Finished ? "Acabou!" : TimerService.Format(timer.Remaining);
        }
        else if (watch.IsActive)
        {
            _timerItem.Visibility = Visibility.Visible;
            _timerGlyph.Text = "";
            _timerText.Text = StopwatchService.Short(watch.Elapsed);
        }
        else
        {
            _timerItem.Visibility = Visibility.Collapsed;
        }
        FitCompact();
    }

    private void UpdateMusic()
    {
        var media = MediaService.Instance;
        _musicItem.Visibility = media.HasSession ? Visibility.Visible : Visibility.Collapsed;
        _musicText.Text = media.Title.Length > 18 ? media.Title[..17] + "…" : media.Title;
        _popover.Refresh();
        FitCompact();
    }

    // ---------- Mostrar / esconder ----------

    private void Watch()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var islandLeft = Left + (Width - _bar.ActualWidth) / 2;
        var rect = new Rect(islandLeft * dpi.DpiScaleX, Top * dpi.DpiScaleY, _bar.ActualWidth * dpi.DpiScaleX, (_bar.ActualHeight + 8) * dpi.DpiScaleY);
        var (cx, cy) = Native.CursorPosition();
        // Com o painel do assistente aberto a ilha sai de cena (o painel "sai" dela).
        if (MainWindow.Current?.IsExpanded == true)
        {
            _wantHideSince = DateTime.MaxValue;
            if (!_hidden) Reveal(false);
            return;
        }
        // Na área de trabalho fica à vista; com app aberto aparece ao levar o mouse ao topo, no meio.
        var onDesktop = Native.ForegroundAppRect() is null && !Native.IsForegroundFullscreen();
        var atTopEdge = cy <= Top * dpi.DpiScaleY + 2 && cx >= rect.Left - 80 && cx <= rect.Right + 80 && !Native.IsForegroundFullscreen();
        var mouseHere = !_hidden && rect.Contains(cx, cy);

        if (onDesktop || mouseHere || atTopEdge || _popover.IsOpen)
        {
            _wantHideSince = DateTime.MaxValue;
            if (_hidden && (onDesktop || atTopEdge)) Reveal(true);
            return;
        }
        if (_hidden) return;
        if (_wantHideSince == DateTime.MaxValue) _wantHideSince = DateTime.Now;
        if (DateTime.Now - _wantHideSince > TimeSpan.FromMilliseconds(400)) Reveal(false);
    }

    private void Reveal(bool show)
    {
        if (_hidden == !show) return;
        _hidden = !show;
        if (!show) _popover.Close();
        var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = show ? System.Windows.Media.Animation.EasingMode.EaseOut : System.Windows.Media.Animation.EasingMode.EaseIn };
        _slide.BeginAnimation(TranslateTransform.YProperty, Anim.To(show ? 0 : -(_bar.ActualHeight + 14), show ? 260 : 180, ease));
        if (show) BringAvatarToFront();
    }

    /// <summary>O avatar e os widgets ficam por cima da barra.</summary>
    private void BringAvatarToFront()
    {
        foreach (Window w in Application.Current.Windows)
        {
            if (ReferenceEquals(w, this) || !w.IsVisible || w is SettingsWindow) continue;
            if (w is MainWindow || w is Widgets.MiniWidget)
            {
                var h = new WindowInteropHelper(w).Handle;
                if (h != IntPtr.Zero) SetWindowPos(h, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
            }
        }
    }

    // ---------- Popovers ----------

    private (UIElement, Action?) CpuPopover()
    {
        var m = MetricsService.Instance;
        _cpuName ??= ReadCpuName();
        var graph = new LineGraph { Height = 70, Width = 260, Margin = new Thickness(0, 6, 0, 8) };
        var cpu = Big("");
        var ram = new ProgressBar { Maximum = 100, Height = 6, Margin = new Thickness(0, 4, 0, 2) };
        var ramText = Muted("");
        var info = Muted("");
        var panel = Stack(
            Header("", "Processador"),
            Muted(_cpuName),
            cpu,
            graph,
            Header("", "Memória"),
            ram,
            ramText,
            info);
        return (panel, () =>
        {
            graph.SetValues(m.Cpu.Values);
            cpu.Text = $"{m.Cpu.Last:0}% em uso · {Environment.ProcessorCount} núcleos";
            ram.Value = m.LastMemory.UsedPercent;
            ramText.Text = $"{ByteSize.Format(m.LastMemory.UsedBytes, PtBr)} de {ByteSize.Format(m.LastMemory.TotalBytes, PtBr)} ({m.LastMemory.UsedPercent:0}%)";
            var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
            info.Text = $"Ligado há {(int)up.TotalDays}d {up.Hours}h {up.Minutes}min";
        });
    }

    private (UIElement, Action?) NetworkPopover()
    {
        var m = MetricsService.Instance;
        var graph = new LineGraph { Height = 70, Width = 260, Maximum = null, Margin = new Thickness(0, 6, 0, 8) };
        var down = Big("");
        var up = Muted("");
        return (Stack(Header("", "Rede"), down, up, graph, Muted("Azul: download · Amarelo: upload")), () =>
        {
            graph.SetValues(m.NetReceive.Values, m.NetSend.Values);
            down.Text = "↓ " + Speed(m.NetReceive.Last);
            up.Text = "↑ " + Speed(m.NetSend.Last) + " enviando";
        });
    }

    private (UIElement, Action?) BatteryPopover()
    {
        var big = Big("");
        var status = Muted("");
        var bar = new ProgressBar { Maximum = 100, Height = 6, Margin = new Thickness(0, 6, 0, 6) };
        return (Stack(Header("", "Bateria"), big, bar, status), () =>
        {
            var p = Forms.SystemInformation.PowerStatus;
            big.Text = $"{p.BatteryLifePercent * 100:0}%";
            bar.Value = p.BatteryLifePercent * 100;
            var charging = p.PowerLineStatus == Forms.PowerLineStatus.Online;
            status.Text = charging ? "Carregando na tomada" :
                p.BatteryLifeRemaining > 0 ? $"Restam cerca de {p.BatteryLifeRemaining / 3600}h {p.BatteryLifeRemaining % 3600 / 60}min" : "Na bateria";
        });
    }

    private (UIElement, Action?) CalendarPopover()
    {
        var today = DateTime.Today;
        var title = Text(Capital(today.ToString("MMMM 'de' yyyy", PtBr)), 15, bold: true);
        var sub = Muted(Capital(today.ToString("dddd, d 'de' MMMM", PtBr)));
        var clock = Big("");

        var grid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        for (var c = 0; c < 7; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        var first = new DateTime(today.Year, today.Month, 1);
        var offset = (int)first.DayOfWeek;
        var days = DateTime.DaysInMonth(today.Year, today.Month);
        var rows = (offset + days + 6) / 7 + 1;
        for (var r = 0; r < rows; r++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });

        var names = new[] { "D", "S", "T", "Q", "Q", "S", "S" };
        for (var c = 0; c < 7; c++)
        {
            var h = Text(names[c], 11, bold: true);
            h.Opacity = 0.5;
            h.HorizontalAlignment = HorizontalAlignment.Center;
            h.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(h, c);
            grid.Children.Add(h);
        }
        for (var d = 1; d <= days; d++)
        {
            var index = offset + d - 1;
            var isToday = d == today.Day;
            var cell = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Child = new TextBlock
                {
                    Text = d.ToString(),
                    FontSize = 12,
                    FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = Brushes.White,
                    Opacity = d < today.Day ? 0.45 : 1,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            if (isToday) cell.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            Grid.SetRow(cell, index / 7 + 1);
            Grid.SetColumn(cell, index % 7);
            grid.Children.Add(cell);
        }
        return (Stack(title, sub, clock, grid), () => clock.Text = DateTime.Now.ToString("HH:mm:ss"));
    }

    private (UIElement, Action?) TimerPopover()
    {
        var timer = TimerService.Instance;
        var watch = StopwatchService.Instance;
        var big = Big("");
        big.FontSize = 26;
        big.FontFamily = (FontFamily)Application.Current.Resources["MonoFont"];
        var label = Muted("");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var toggle = Ui.Btn("Pausar", () =>
        {
            if (timer.State != TimerState.Idle) timer.TogglePause();
            else watch.Toggle();
        }, primary: true);
        var stop = Ui.Btn("Parar", () =>
        {
            if (timer.State != TimerState.Idle) timer.Reset();
            else watch.Reset();
            _popover.Close();
        });
        stop.Margin = new Thickness(6, 0, 0, 0);
        buttons.Children.Add(toggle);
        buttons.Children.Add(stop);
        return (Stack(Header("", "Tempo"), big, label, buttons), () =>
        {
            if (timer.State != TimerState.Idle)
            {
                big.Text = timer.State == TimerState.Finished ? "Acabou!" : TimerService.Format(timer.Remaining);
                label.Text = timer.State switch
                {
                    TimerState.Running => $"Timer{(timer.Label.Length > 0 ? " · " + timer.Label : "")} · termina às {timer.EndsAt:HH:mm}",
                    TimerState.Paused => "Timer pausado",
                    _ => "Tempo esgotado",
                };
                toggle.Content = timer.State == TimerState.Running ? "Pausar" : "Continuar";
                toggle.Visibility = timer.State == TimerState.Finished ? Visibility.Collapsed : Visibility.Visible;
            }
            else
            {
                big.Text = StopwatchService.Short(watch.Elapsed);
                label.Text = watch.IsRunning ? "Cronômetro rodando" : "Cronômetro pausado";
                toggle.Content = watch.IsRunning ? "Pausar" : "Continuar";
                toggle.Visibility = Visibility.Visible;
            }
        });
    }

    private (UIElement, Action?) MusicPopover()
    {
        var media = MediaService.Instance;
        var cover = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 12, 0) };
        var title = Text("", 14, bold: true);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.MaxWidth = 200;
        var artist = Muted("");
        var source = Muted("");
        var progress = new ProgressBar { Maximum = 1, Height = 4, Margin = new Thickness(0, 10, 0, 8) };
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(title);
        info.Children.Add(artist);
        info.Children.Add(source);
        var top = new DockPanel();
        DockPanel.SetDock(cover, Dock.Left);
        top.Children.Add(cover);
        top.Children.Add(info);

        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var play = new Widgets.PillButton("", media.TogglePlayPause, true, 36);
        controls.Children.Add(new Widgets.PillButton("", media.Previous, false, 32));
        controls.Children.Add(play);
        controls.Children.Add(new Widgets.PillButton("", media.Next, false, 32));
        return (Stack(top, progress, controls), () =>
        {
            title.Text = media.Title;
            artist.Text = media.Artist;
            source.Text = media.AppName;
            if (media.Cover is { } img) cover.Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
            else cover.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            var (pos, dur) = media.GetTimeline();
            progress.Value = dur > TimeSpan.Zero ? pos.TotalSeconds / dur.TotalSeconds : 0;
            play.Glyph = media.IsPlaying ? "" : "";
        });
    }

    private (UIElement, Action?) AssistantPopover()
    {
        var panel = new StackPanel { MinWidth = 300 };
        var title = Row(Text("Assistente", 15, bold: true));
        title.Margin = new Thickness(2, 4, 0, 10);
        panel.Children.Add(title);
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        void Action(string glyph, string text, Action click)
        {
            var icon = Glyph(glyph);
            icon.Margin = new Thickness(0, 0, 8, 0);
            icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            var b = new Border
            {
                Margin = new Thickness(3),
                Padding = new Thickness(10, 9, 10, 9),
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF)),
                Cursor = Cursors.Hand,
                Child = Row(icon, Text(text, 12.5)),
            };
            b.MouseEnter += (_, _) => b.Background = new SolidColorBrush(Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF));
            b.MouseLeave += (_, _) => b.Background = new SolidColorBrush(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF));
            b.MouseLeftButtonUp += (_, _) => { _popover.Close(); click(); };
            grid.Children.Add(b);
        }
        Action("", "Ferramentas", () => MainWindow.Current?.ExpandPinned());
        Action("", "Sistema", () => MainWindow.Current?.ShowTool("Visão geral"));
        Action("", "Música", () => MainWindow.Current?.ShowTool("Tocando agora"));
        Action("", "Configurações", () => SettingsWindow.Open());
        panel.Children.Add(grid);
        return (panel, null);
    }

    /// <summary>
    /// Faz a ilha crescer (com animação "elástica", como a Dynamic Island) mostrando o
    /// conteúdo do item em que o mouse está, e voltar a ser pílula quando o mouse sai.
    /// </summary>
    private sealed class IslandPopover
    {
        private readonly MenuBarWindow _w;
        private readonly DispatcherTimer _close = new() { Interval = TimeSpan.FromMilliseconds(280) };
        private FrameworkElement? _owner;
        private Action? _update;
        private int _token;

        public IslandPopover(MenuBarWindow w)
        {
            _w = w;
            _close.Tick += (_, _) =>
            {
                _close.Stop();
                if (_w._bar.IsMouseOver) return;
                Close();
            };
            _w._bar.MouseLeave += (_, _) => { if (IsOpen) _close.Start(); };
            _w._bar.MouseEnter += (_, _) => _close.Stop();
        }

        public bool IsOpen { get; private set; }

        public void Attach(FrameworkElement owner, Func<(UIElement View, Action? Update)> build)
        {
            owner.MouseEnter += (_, _) => Open(owner, build);
        }

        public void Open(FrameworkElement owner, Func<(UIElement View, Action? Update)> build)
        {
            _close.Stop();
            if (IsOpen && ReferenceEquals(_owner, owner)) return;
            _owner = owner;
            var (view, update) = build();
            _update = update;
            _w._detail.Content = view;
            _w._detail.Visibility = Visibility.Visible;
            update?.Invoke();
            IsOpen = true;

            _w._island.Measure(new Size(IslandMaxWidth - 20, double.PositiveInfinity));
            var size = _w._island.DesiredSize;
            var width = Math.Min(IslandMaxWidth, Math.Max(_w.CompactWidth(), size.Width + 20));
            var height = Math.Min(IslandMaxHeight, size.Height + 2);
            _w._bar.CornerRadius = new CornerRadius(26);
            _w.ResizeIsland(width, height, expand: true);
            _w._detail.BeginAnimation(UIElement.OpacityProperty, Anim.FromTo(0, 1, 200, null, 90));
        }

        public void Refresh()
        {
            if (IsOpen) _update?.Invoke();
        }

        public void Close()
        {
            _close.Stop();
            if (!IsOpen) return;
            IsOpen = false;
            _owner = null;
            _update = null;
            var token = ++_token;
            var fade = Anim.To(0, 90);
            fade.Completed += (_, _) =>
            {
                if (token != _token || IsOpen) return;
                _w._detail.Visibility = Visibility.Collapsed;
                _w._detail.Content = null;
                _w._bar.CornerRadius = new CornerRadius(BarHeight / 2);
                _w.ResizeIsland(_w.CompactWidth(), BarHeight, expand: false);
            };
            _w._detail.BeginAnimation(UIElement.OpacityProperty, fade);
        }
    }

    /// <summary>Largura da pílula fechada (cabe o avatar e os itens visíveis).</summary>
    private double CompactWidth()
    {
        _left.Measure(new Size(double.PositiveInfinity, BarHeight));
        _right.Measure(new Size(double.PositiveInfinity, BarHeight));
        return Math.Max(180, _left.DesiredSize.Width + _right.DesiredSize.Width + 24 + 18);
    }

    private void ResizeIsland(double width, double height, bool expand)
    {
        System.Windows.Media.Animation.IEasingFunction ease = expand
            ? new System.Windows.Media.Animation.BackEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut, Amplitude = 0.25 }
            : new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
        var ms = expand ? 380 : 260;
        if (double.IsNaN(_bar.Width)) _bar.Width = _bar.ActualWidth > 0 ? _bar.ActualWidth : width;
        if (double.IsNaN(_bar.Height)) _bar.Height = _bar.ActualHeight > 0 ? _bar.ActualHeight : height;
        _bar.BeginAnimation(WidthProperty, Anim.To(width, ms, ease));
        _bar.BeginAnimation(HeightProperty, Anim.To(height, ms, ease));
    }

    /// <summary>Pílula fechada acompanha os itens que aparecem/somem (timer, música).</summary>
    private void FitCompact()
    {
        if (_popover.IsOpen || !IsLoaded) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_popover.IsOpen) return;
            var w = CompactWidth();
            if (Math.Abs(w - _bar.ActualWidth) > 1) ResizeIsland(w, BarHeight, expand: false);
        }), DispatcherPriority.Loaded);
    }

    // ---------- Peças ----------

    private Border Item(UIElement content, Func<(UIElement, Action?)>? popover)
    {
        var hover = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
        var item = new Border
        {
            Child = content,
            Padding = new Thickness(7, 0, 7, 0),
            Margin = new Thickness(1, 3, 1, 3),
            CornerRadius = new CornerRadius(10),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        item.MouseEnter += (_, _) => item.Background = hover;
        item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;
        if (popover is not null) _popover.Attach(item, popover);
        return item;
    }

    private static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var child in children) row.Children.Add(child);
        return row;
    }

    private static StackPanel Stack(params UIElement[] children)
    {
        var s = new StackPanel();
        foreach (var child in children) s.Children.Add(child);
        return s;
    }

    private static TextBlock Text(string text, double size, bool bold = false, bool mono = false)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (mono) t.FontFamily = (FontFamily)Application.Current.Resources["MonoFont"];
        return t;
    }

    private static TextBlock Glyph(string glyph) => new()
    {
        Text = glyph,
        FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
        FontSize = 13,
        Foreground = Brushes.White,
        Margin = new Thickness(0, 0, 6, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static TextBlock Big(string text) => new() { Text = text, FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(0, 4, 0, 0) };

    private static TextBlock Muted(string text) => new() { Text = text, FontSize = 12, Foreground = Brushes.White, Opacity = 0.6, TextWrapping = TextWrapping.Wrap, MaxWidth = 280 };

    private static StackPanel Header(string glyph, string text)
    {
        var icon = Glyph(glyph);
        icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        var row = Row(icon, Text(text, 13, bold: true));
        row.Margin = new Thickness(0, 4, 0, 2);
        return row;
    }

    private static string Speed(double bytesPerSecond) => ByteSize.Format(bytesPerSecond, PtBr) + "/s";

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpper(s[0], PtBr) + s[1..];

    private static string ReadCpuName()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "Processador";
        }
        catch (Exception)
        {
            return "Processador";
        }
    }

    // ---------- Win32 ----------

    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoActivate = 0x0010;
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExAppWindow = 0x00040000;

    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private static void MakeToolWindow(IntPtr hwnd)
    {
        var style = GetWindowLong(hwnd, GwlExStyle);
        SetWindowLong(hwnd, GwlExStyle, (style | WsExToolWindow | WsExNoActivate) & ~WsExAppWindow);
    }
}
