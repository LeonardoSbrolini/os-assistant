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

    /// <summary>A ilha da tela principal (o painel do assistente "sai" dela). Nula se a principal não tiver ilha.</summary>
    public static MenuBarWindow? Current => All.FirstOrDefault(w => w._screen.Primary);

    /// <summary>Uma ilha por tela escolhida (principal, todas ou uma específica).</summary>
    private static readonly List<MenuBarWindow> All = new();

    private readonly Forms.Screen _screen;
    private readonly IntPtr _monitor;

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

    private readonly TopBar _topBar;
    private bool _onDesktop;
    private bool _roomToOpen = true;
    private bool _hasBattery;

    private bool _hidden;
    private DateTime _wantHideSince = DateTime.MaxValue;
    private static string? _cpuName;

    private MenuBarWindow(AppSettings settings, Forms.Screen screen)
    {
        _settings = settings;
        _screen = screen;
        var sb = screen.Bounds;
        _monitor = Native.MonitorOf(new Rect(sb.X, sb.Y, sb.Width, sb.Height));
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
        // Grudada no topo como o notch: cantos de cima retos e "orelhas" curvas que se
        // encontram com a borda da tela; só os cantos de baixo são arredondados.
        _bar.Margin = new Thickness(0);
        _bar.Background = IslandBlack;
        _bar.BorderThickness = new Thickness(0);
        _bar.CornerRadius = CompactCorners;
        _bar.Padding = new Thickness(10, 0, 10, 0);
        _bar.ClipToBounds = true;

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

        var shell = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, RenderTransform = _slide };
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Ear) });
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Ear) });
        var leftEar = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse($"M0,0 L{Ear},0 L{Ear},{Ear} A{Ear},{Ear} 0 0 0 0,0 Z"),
            Fill = IslandBlack,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var rightEar = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse($"M{Ear},0 L0,0 L0,{Ear} A{Ear},{Ear} 0 0 1 {Ear},0 Z"),
            Fill = IslandBlack,
            VerticalAlignment = VerticalAlignment.Top,
        };
        Grid.SetColumn(_bar, 1);
        Grid.SetColumn(rightEar, 2);
        shell.Children.Add(leftEar);
        shell.Children.Add(_bar);
        shell.Children.Add(rightEar);

        // Barra de menus de ponta a ponta, atrás da ilha (desce e sobe junto com ela).
        _topBar = new TopBar(settings, OpenIslandAt, BarHeight) { RenderTransform = _slide };
        _topBar.Visibility = settings.TopBarEnabled ? Visibility.Visible : Visibility.Collapsed;
        var root = new Grid();
        root.Children.Add(_topBar);
        root.Children.Add(shell);
        Content = root;

        BuildLeft();

        // Cada item abre o menu dele: timer → Tempo, música → Início, CPU/memória/bateria → Sistema.
        _timerItem = Item(Row(_timerGlyph, _timerText), () => OpenTab("tempo"));
        _musicItem = Item(Row(Glyph(""), _musicText), () => OpenTab("inicio"));
        _netItem = Item(Row(Glyph(""), _netText), () => OpenTab("sistema"));
        var cpuLabel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) };
        cpuLabel.Children.Add(Text("CPU", 8.5, bold: true));
        cpuLabel.Children.Add(_cpuText);
        _cpuText.FontWeight = FontWeights.SemiBold;
        _cpuItem = Item(Row(_cpuSpark, cpuLabel), () => OpenTab("sistema"));
        _ramItem = Item(Row(Glyph(""), _ramText), () => OpenTab("sistema"));
        _batteryItem = Item(Row(_batteryGlyph, _batteryText), () => OpenTab("sistema"));
        _clockItem = Item(Row(Glyph(""), _clockText), null);
        CalendarPopup.Attach(_clockItem);
        foreach (var item in new[] { _timerItem, _musicItem, _cpuItem, _batteryItem, _clockItem })
            _right.Children.Add(item);
        ApplyCompactItems();

        // Clique na ilha (fechada ou um pouco aberta) abre por completo.
        _bar.MouseLeftButtonUp += (_, e) =>
        {
            if (e.Handled || _popover.State == IslandState.Full) return;
            _tab = "inicio";
            _popover.Open();
        };

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
            All.Remove(this);
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

    public static void Apply(AppSettings settings) => Apply(settings, rebuild: false);

    private static bool _listening;

    private static void Apply(AppSettings settings, bool rebuild)
    {
        if (!_listening)
        {
            _listening = true;
            ScreenChoice.OnChanged(() => { if (All.Count > 0) Apply(settings, rebuild: true); });
        }
        if (!settings.MenuBarEnabled)
        {
            foreach (var w in All.ToList()) w.Close();
            All.Clear();
            return;
        }
        var targets = ScreenChoice.Targets(settings.IslandScreens);
        var want = string.Join("|", targets.Select(t => t.DeviceName + t.Bounds));
        var have = string.Join("|", All.Select(w => w._screen.DeviceName + w._screen.Bounds));
        if (!rebuild && want == have) return;
        foreach (var w in All.ToList()) w.Close();
        All.Clear();
        foreach (var screen in targets)
        {
            var w = new MenuBarWindow(settings, screen);
            All.Add(w);
            w.Place();
            w.Show();
            w.Update();
            w.UpdateTimer();
            w.UpdateMusic();
            w._watch.Start();
            w.Loaded += (_, _) =>
            {
                w.FitCompact();
                w._popover.GoRest();
            };
            w.BringAvatarToFront();
        }
    }

    /// <summary>Mudou algo da barra de menus ou da ilha nas Configurações.</summary>
    public static void Reconfigure()
    {
        foreach (var w in All.ToList()) w.Reconfigure1();
    }

    /// <summary>Avatar trocado: refaz o avatar da ilha em todas as telas.</summary>
    public static void RefreshAvatar()
    {
        foreach (var w in All.ToList())
        {
            w._left.Children.Clear();
            w.BuildLeft();
            w.FitCompact();
            if (w._popover.IsOpen) w._popover.Reflow();
        }
    }

    private void Reconfigure1()
    {
        var w = this;
        var want = w._settings.TopBarEnabled && w._onDesktop;
        w._topBarShown = !want; // força reaplicar
        w.ShowTopBar(want);
        w._topBar.Rebuild();
        w.ApplyCompactItems();
        w.FitCompact();
        if (!w._bar.IsMouseOver) w._popover.GoRest();
    }

    private bool _topBarShown = true;

    /// <summary>Mostra/esconde a barra de menus com um fade (a ilha continua).</summary>
    private void ShowTopBar(bool show)
    {
        if (show == _topBarShown) return;
        _topBarShown = show;
        ApplyCompactItems();
        FitCompact();
        if (show)
        {
            _topBar.Visibility = Visibility.Visible;
            _topBar.BeginAnimation(OpacityProperty, Anim.To(1, 200));
        }
        else
        {
            var fade = Anim.To(0, 150);
            fade.Completed += (_, _) => { if (!_topBarShown) _topBar.Visibility = Visibility.Collapsed; };
            _topBar.BeginAnimation(OpacityProperty, fade);
        }
    }

    /// <summary>Com a barra de menus ligada, relógio/CPU/bateria saem da ilha (já estão na barra).</summary>
    private void ApplyCompactItems()
    {
        var bar = _settings.TopBarEnabled && _topBarShown;
        _cpuItem.Visibility = bar ? Visibility.Collapsed : Visibility.Visible;
        // A hora fica sempre na ilha (mesmo com a barra de menus à vista).
        _clockItem.Visibility = Visibility.Visible;
        _batteryItem.Visibility = !bar && _hasBattery ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Abre a ilha por completo numa aba (usado pela barra de menus).</summary>
    private void OpenIslandAt(string tab)
    {
        if (_hidden) Reveal(true);
        _tab = tab;
        if (_popover.State == IslandState.Full) _popover.Reflow(); else _popover.Open();
        _popover.ArmClose();
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
        // Fica no topo da tela escolhida (DIPs a partir dos pixels da tela).
        var dpi = VisualTreeHelper.GetDpi(this);
        var wa = _screen.WorkingArea;
        var area = new Rect(wa.Left / dpi.DpiScaleX, wa.Top / dpi.DpiScaleY, wa.Width / dpi.DpiScaleX, wa.Height / dpi.DpiScaleY);
        // Largura da tela inteira: a barra de menus vai de ponta a ponta (a parte transparente
        // da janela deixa o clique passar para o que está embaixo).
        Width = area.Width;
        Height = IslandMaxHeight + 40;
        Left = area.Left;
        Top = area.Top;
    }

    private const double IslandMaxWidth = 680;
    private const double IslandMaxHeight = 520;
    private const double Ear = 12;
    private static readonly Brush IslandBlack = Frozen(new SolidColorBrush(Color.FromRgb(0, 0, 0)));
    private static readonly CornerRadius CompactCorners = new(0, 0, 16, 16);
    private static readonly CornerRadius ExpandedCorners = new(0, 0, 30, 30);
    private static readonly CornerRadius PeekCorners = new(0, 0, 24, 24);

    private enum IslandState { Closed, Peek, Full }

    private static Brush Frozen(Brush b)
    {
        b.Freeze();
        return b;
    }

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
            foreach (var w in All) w.ReleaseSpace();
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
        // Avatar: abre a ilha na página inicial (onde fica o avatar). Não abre as ferramentas.
        var logo = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            VerticalAlignment = VerticalAlignment.Center,
        };
        logo.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        if (Avatars.Find(_settings.Avatar) is { } avatar)
        {
            logo.Background = Brushes.Transparent;
            logo.Child = Avatars.View(avatar, 20);
        }
        var home = Item(logo, () => OpenTab("inicio"));
        home.ToolTip = "Início";
        _left.Children.Add(home);

        // Ferramentas por escrito (no lugar do ícone): abre o painel de ferramentas.
        var tools = Item(Text("Ferramentas", 12.5, bold: true), () =>
        {
            _popover.Close();
            MainWindow.Current?.ExpandPinned();
        });
        _left.Children.Add(tools);
    }

    /// <summary>Abre a ilha por completo direto numa aba.</summary>
    private void OpenTab(string tab)
    {
        _tab = tab;
        if (_popover.State == IslandState.Full) _popover.Reflow();
        else _popover.Open();
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
        _hasBattery = hasBattery;
        _batteryItem.Visibility = hasBattery && !(_settings.TopBarEnabled && _topBarShown) ? Visibility.Visible : Visibility.Collapsed;
        if (hasBattery)
        {
            _batteryText.Text = $"{power.BatteryLifePercent * 100:0}%";
            _batteryGlyph.Text = power.PowerLineStatus == Forms.PowerLineStatus.Online ? "" : "";
        }

        _clockText.Text = DateTime.Now.ToString("HH:mm", PtBr);
        _topBar.Refresh();
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
        _topBar.Refresh();
        if (_tab == "tempo") _popover.Refresh();
        FitCompact();
    }

    private void UpdateMusic()
    {
        var media = MediaService.Instance;
        _musicItem.Visibility = media.HasSession ? Visibility.Visible : Visibility.Collapsed;
        _musicText.Text = media.Title.Length > 18 ? media.Title[..17] + "…" : media.Title;
        _topBar.Refresh();
        _popover.Refresh();
        FitCompact();
    }

    // ---------- Mostrar / esconder ----------

    private void Watch()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var islandLeft = Left + (Width - _bar.ActualWidth) / 2;
        var rect = new Rect(islandLeft * dpi.DpiScaleX, Top * dpi.DpiScaleY, _bar.ActualWidth * dpi.DpiScaleX, (_bar.ActualHeight + 4) * dpi.DpiScaleY);
        var (cx, cy) = Native.CursorPosition();
        // Com o painel do assistente aberto a ilha sai de cena (o painel "sai" dela).
        if (ReferenceEquals(this, Current) && MainWindow.Current?.IsExpanded == true)
        {
            _wantHideSince = DateTime.MaxValue;
            if (!_hidden) Reveal(false);
            return;
        }
        // Alt+Tab / Win+Tab: é só a troca de janela; não abre nem fecha nada nesse instante.
        if (Native.IsSwitcherForeground())
        {
            _wantHideSince = DateTime.MaxValue;
            return;
        }
        // A ilha fica à vista e só sai de cena (com transição) quando uma janela encosta no topo,
        // onde ela fica (ex.: janela maximizada) ou em tela cheia — como o dock do Mac.
        var bar = _settings.TopBarEnabled;
        // Cada tela decide sozinha: clicar ou usar a outra tela não mostra a ilha desta.
        var fullscreen = Native.IsFullscreenOn(_monitor);
        var fg = Native.FrontWindowOn(_monitor)?.Rect;
        var stripTop = Top * dpi.DpiScaleY;
        var stripBottom = stripTop + (BarHeight + 2) * dpi.DpiScaleY;
        var stripLeft = rect.Left - 4;
        var stripRight = rect.Right + 4;
        var touchesTop = Native.AnyWindowTouches(_monitor, new Rect(stripLeft, stripTop, Math.Max(1, stripRight - stripLeft), Math.Max(1, stripBottom - stripTop)));
        // "Área de trabalho" de verdade nesta tela: nenhum app aberto nela, ou clicou no fundo (Win+D).
        var desktop = !fullscreen && Native.IsDesktopOn(_monitor);
        var onDesktop = desktop;
        // Espaço para a ilha ficar aberta embaixo do topo sem cobrir a janela da frente.
        var centerX = (Left + Width / 2) * dpi.DpiScaleX;
        var openArea = new Rect(centerX - 310 * dpi.DpiScaleX, stripTop, 620 * dpi.DpiScaleX, 300 * dpi.DpiScaleY);
        var roomToOpen = fg is not { } f || f.Width <= 0 || !openArea.IntersectsWith(f);
        if (roomToOpen != _roomToOpen)
        {
            _roomToOpen = roomToOpen;
            if (!_hidden && !_bar.IsMouseOver && !_dragging) _popover.GoRest();
        }
        // A barra de menus só aparece na área de trabalho, nada além disso.
        ShowTopBar(bar && desktop && !_hidden);
        var atTopEdge = cy >= Top * dpi.DpiScaleY && cy <= Top * dpi.DpiScaleY + 2 && !fullscreen &&
            cx >= rect.Left - 80 && cx <= rect.Right + 80;
        var barRect = new Rect(Left * dpi.DpiScaleX, Top * dpi.DpiScaleY, Width * dpi.DpiScaleX, (BarHeight + 4) * dpi.DpiScaleY);
        var mouseHere = !_hidden && (rect.Contains(cx, cy) || _topBarShown && barRect.Contains(cx, cy));

        // Saiu/voltou para a área de trabalho: a ilha volta ao jeito escolhido (fechada, um pouco aberta, aberta).
        if (onDesktop != _onDesktop)
        {
            _onDesktop = onDesktop;
            if (!_hidden && !_bar.IsMouseOver && !_dragging) _popover.GoRest();
        }

        var stays = !touchesTop && !fullscreen;
        if (stays || mouseHere || atTopEdge || _popover.IsExpanded || _topBar.MenuOpen || CalendarPopup.IsOpen)
        {
            _wantHideSince = DateTime.MaxValue;
            if (_hidden && (stays || atTopEdge))
            {
                Reveal(true);
                // Veio pelo mouse no topo (com um app na frente): vai direto para o menu escolhido,
                // sem mostrar primeiro a pílula e depois o menu.
                if (atTopEdge && !stays) _popover.HoverOpen();
            }
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
        _slide.BeginAnimation(TranslateTransform.YProperty, Anim.To(show ? 0 : -(_bar.ActualHeight + 4), show ? 260 : 180, ease));
        if (show)
        {
            BringAvatarToFront();
            _popover.GoRest();
        }
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
        private readonly DispatcherTimer _close = new() { Interval = TimeSpan.FromMilliseconds(300) };
        private readonly DispatcherTimer _open = new() { Interval = TimeSpan.FromMilliseconds(140) };
        private Action? _update;
        private int _token;

        public IslandPopover(MenuBarWindow w)
        {
            _w = w;
            _close.Tick += (_, _) =>
            {
                _close.Stop();
                _close.Interval = TimeSpan.FromMilliseconds(300);
                if (_w._bar.IsMouseOver || _w._dragging) return;
                // Calendário aberto a partir da ilha: ela espera ele fechar.
                if (CalendarPopup.IsOpen)
                {
                    _close.Start();
                    return;
                }
                GoRest();
            };
            _open.Tick += (_, _) =>
            {
                _open.Stop();
                if (!_w._bar.IsMouseOver || State == HoverState || State == IslandState.Full) return;
                // Com um app aberto: um menu OU o outro (o pequeno não vira o grande sozinho).
                if (!_w._onDesktop && State == IslandState.Peek) return;
                if (HoverState == IslandState.Full) { _w._tab = "inicio"; Open(); } else Peek();
            };
            _w._bar.MouseLeave += (_, _) =>
            {
                _open.Stop();
                if (State != RestState) _close.Start();
            };
            _w._bar.MouseEnter += (_, _) =>
            {
                _close.Stop();
                // Passar o mouse abre do jeito escolhido (um pouco ou tudo); clicar abre tudo.
                if (State != IslandState.Full && State != HoverState) _open.Start();
            };
        }

        public IslandState State { get; private set; } = IslandState.Closed;

        /// <summary>Totalmente aberta.</summary>
        public bool IsOpen => State == IslandState.Full;

        /// <summary>Um pouco ou totalmente aberta.</summary>
        public bool IsExpanded => State != IslandState.Closed;

        /// <summary>Como a ilha deve ficar parada agora (só fica aberta na área de trabalho).</summary>
        private IslandState RestState =>
            !_w._onDesktop || !_w._roomToOpen || _w._hidden ? IslandState.Closed : _w._settings.IslandRest switch
            {
                "peek" => IslandState.Peek,
                "aberta" => IslandState.Full,
                _ => IslandState.Closed,
            };

        /// <summary>
        /// Como a ilha abre ao passar o mouse: na área de trabalho vale "Ao passar o mouse"; com algum
        /// app aberto na frente vale "Com um app aberto" (menu pequeno OU grande, nunca os dois).
        /// </summary>
        private IslandState HoverState =>
            (_w._onDesktop ? _w._settings.IslandHover : _w._settings.IslandHoverBusy) == "peek" ? IslandState.Peek : IslandState.Full;

        /// <summary>Abre já no jeito de "mouse em cima" (sem passar pelo outro).</summary>
        public void HoverOpen()
        {
            _close.Stop();
            _open.Stop();
            if (State == HoverState) return;
            if (HoverState == IslandState.Full) { _w._tab = "inicio"; Open(); } else Peek();
        }

        /// <summary>Volta ao jeito escolhido nas configurações.</summary>
        public void GoRest()
        {
            switch (RestState)
            {
                case IslandState.Peek:
                    Peek();
                    break;
                case IslandState.Full:
                    if (State == IslandState.Full && _w._tab == "inicio") return;
                    _w._tab = "inicio";
                    if (State == IslandState.Full) Reflow(); else Open();
                    break;
                default:
                    Close();
                    break;
            }
        }

        /// <summary>Aberta por fora (barra de menus): fecha sozinha se o mouse não vier até ela.</summary>
        public void ArmClose()
        {
            _close.Stop();
            _close.Interval = TimeSpan.FromMilliseconds(1800);
            _close.Start();
        }

        public void Open()
        {
            _close.Stop();
            _open.Stop();
            ++_token;
            var wasFull = State == IslandState.Full;
            State = IslandState.Full;
            _w._compact.Visibility = Visibility.Collapsed;
            Reflow();
            if (!wasFull) _w.PopIn(100);
        }

        /// <summary>Um pouco aberta (como ao passar o mouse): saudação ou música e a hora.</summary>
        public void Peek()
        {
            _open.Stop();
            if (State == IslandState.Peek) return;
            ++_token;
            State = IslandState.Peek;
            var (view, update) = _w.PeekView();
            _update = update;
            _w._compact.Visibility = Visibility.Collapsed;
            _w._detail.Content = view;
            _w._detail.Visibility = Visibility.Visible;
            update?.Invoke();
            _w._island.Measure(new Size(IslandMaxWidth - 24, double.PositiveInfinity));
            var size = _w._island.DesiredSize;
            _w.ResizeIsland(Math.Min(IslandMaxWidth, size.Width + 24), size.Height + 2, expand: true, PeekCorners.BottomLeft);
            _w.PopIn(80);
        }

        /// <summary>Remonta o conteúdo (ex.: trocou de aba) e ajusta o tamanho da ilha.</summary>
        public void Reflow()
        {
            if (State != IslandState.Full) return;
            var (view, update) = _w.Dashboard();
            _update = update;
            _w._detail.Content = view;
            _w._detail.Visibility = Visibility.Visible;
            update?.Invoke();

            _w._island.Measure(new Size(IslandMaxWidth - 24, double.PositiveInfinity));
            var size = _w._island.DesiredSize;
            var width = Math.Min(IslandMaxWidth, Math.Max(560, size.Width + 24));
            var height = Math.Min(IslandMaxHeight, size.Height + 4);
            _w.ResizeIsland(width, height, expand: true, ExpandedCorners.BottomLeft);
        }

        public void Refresh()
        {
            if (State != IslandState.Closed) _update?.Invoke();
        }

        public void Close()
        {
            _close.Stop();
            _open.Stop();
            if (State == IslandState.Closed) return;
            State = IslandState.Closed;
            _update = null;
            var token = ++_token;
            var fade = Anim.To(0, 90);
            fade.Completed += (_, _) =>
            {
                if (token != _token || State != IslandState.Closed) return;
                _w._detail.Visibility = Visibility.Collapsed;
                _w._detail.Content = null;
                _w._compact.Visibility = Visibility.Visible;
                _w._compact.BeginAnimation(UIElement.OpacityProperty, Anim.FromTo(0, 1, 200, null, 120));
                _w.ResizeIsland(_w.CompactWidth(), BarHeight, expand: false, CompactCorners.BottomLeft);
            };
            _w._detail.BeginAnimation(UIElement.OpacityProperty, fade);
        }
    }

    // Um pouco aberta: avatar (ou capa da música), saudação/música e a hora.
    private (UIElement, Action?) PeekView()
    {
        var media = MediaService.Instance;
        var grid = new Grid { Width = 380, Margin = new Thickness(2, 6, 2, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var pic = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(pic);
        var title = Text("", 14, bold: true);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        var sub = Muted("");
        sub.TextWrapping = TextWrapping.NoWrap;
        sub.TextTrimming = TextTrimming.CharacterEllipsis;
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        mid.Children.Add(title);
        mid.Children.Add(sub);
        Grid.SetColumn(mid, 1);
        grid.Children.Add(mid);

        var clock = Text("", 22, bold: true);
        clock.Margin = new Thickness(12, 0, 4, 0);
        CalendarPopup.Attach(clock);
        var play = new Widgets.PillButton("\uE768", media.TogglePlayPause, false, 30);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(play);
        right.Children.Add(clock);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        var avatar = Avatars.Find(_settings.Avatar);
        return (grid, () =>
        {
            var now = DateTime.Now;
            clock.Text = now.ToString("HH:mm", PtBr);
            if (media.HasSession)
            {
                title.Text = media.Title;
                sub.Text = media.Artist.Length > 0 ? media.Artist : media.AppName;
                play.Visibility = Visibility.Visible;
                play.Glyph = media.IsPlaying ? "\uE769" : "\uE768";
                if (media.Cover is { } img) pic.Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
                else pic.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                pic.Child = null;
            }
            else
            {
                title.Text = now.Hour < 12 ? "Bom dia" : now.Hour < 18 ? "Boa tarde" : "Boa noite";
                var t = TimerService.Instance;
                var sw = StopwatchService.Instance;
                sub.Text = t.State != TimerState.Idle ? "Timer: " + (t.State == TimerState.Finished ? "acabou!" : TimerService.Format(t.Remaining))
                    : sw.IsActive ? "Cronômetro: " + StopwatchService.Short(sw.Elapsed)
                    : Capital(now.ToString("dddd, d 'de' MMMM", PtBr));
                play.Visibility = Visibility.Collapsed;
                pic.Background = Brushes.Transparent;
                if (pic.Child is null && avatar is not null) pic.Child = Avatars.View(avatar, 38);
            }
        });
    }

    /// <summary>Largura da pílula fechada (cabe o avatar e os itens visíveis).</summary>
    private double CompactWidth()
    {
        _left.Measure(new Size(double.PositiveInfinity, BarHeight));
        _right.Measure(new Size(double.PositiveInfinity, BarHeight));
        return Math.Max(200, _left.DesiredSize.Width + _right.DesiredSize.Width + 24 + 20);
    }

    private Spring? _shape;

    /// <summary>
    /// Muda o formato da ilha com uma mola de verdade, como a Dynamic Island: ao abrir passa um
    /// pouquinho do tamanho e volta; ao fechar encolhe firme de volta à pílula. Se mudar de ideia no
    /// meio (mouse saiu e voltou), a mola continua de onde está, sem recomeçar.
    /// </summary>
    private void ResizeIsland(double width, double height, bool expand, double radius)
    {
        if (_shape is null)
        {
            _bar.BeginAnimation(WidthProperty, null);
            _bar.BeginAnimation(HeightProperty, null);
            var w0 = !double.IsNaN(_bar.Width) ? _bar.Width : _bar.ActualWidth > 0 ? _bar.ActualWidth : width;
            var h0 = !double.IsNaN(_bar.Height) ? _bar.Height : _bar.ActualHeight > 0 ? _bar.ActualHeight : height;
            _shape = new Spring(3, v =>
            {
                _bar.Width = Math.Max(1, v[0]);
                _bar.Height = Math.Max(1, v[1]);
                var r = Math.Clamp(v[2], 0, Math.Max(0, Math.Min(v[0], v[1]) / 2));
                _bar.CornerRadius = new CornerRadius(0, 0, r, r);
            });
            _shape.Snap(w0, h0, _bar.CornerRadius.BottomLeft);
        }
        if (expand) _shape.To(0.46, 0.7, null, width, height, radius);
        else _shape.To(0.36, 0.92, null, width, height, radius);
    }

    /// <summary>O conteúdo "nasce" de dentro da ilha: aparece crescendo um pouco, com fade.</summary>
    private void PopIn(double delayMs)
    {
        var scale = new ScaleTransform(0.92, 0.92);
        _detail.RenderTransformOrigin = new Point(0.5, 0);
        _detail.RenderTransform = scale;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.To(1, 340, Anim.EaseOut, delayMs));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.To(1, 340, Anim.EaseOut, delayMs));
        _detail.BeginAnimation(UIElement.OpacityProperty, Anim.FromTo(0, 1, 220, null, delayMs));
    }

    // ---------- Painel do assistente sai da ilha ----------

    /// <summary>Onde a ilha está agora na tela (DIPs), se estiver à vista.</summary>
    public static Rect? VisibleIslandRect
    {
        get
        {
            var w = Current;
            if (w is null || !w.IsVisible || w._hidden || w._bar.ActualWidth <= 0) return null;
            var bw = w._bar.ActualWidth;
            return new Rect(w.Left + (w.Width - bw) / 2, w.Top + w._slide.Y, bw, w._bar.ActualHeight);
        }
    }

    /// <summary>Onde fica a pílula fechada (DIPs): é para lá que o painel volta ao fechar.</summary>
    public static Rect? PillRect
    {
        get
        {
            var w = Current;
            if (w is null || !w.IsVisible) return null;
            var cw = w.CompactWidth();
            return new Rect(w.Left + (w.Width - cw) / 2, w.Top, cw, BarHeight);
        }
    }

    /// <summary>O painel do assistente "virou" a ilha: ela sai de cena por baixo dele.</summary>
    public static void HandOffToPanel()
    {
        var w = Current;
        if (w is null) return;
        w._wantHideSince = DateTime.MaxValue;
        w.Reveal(false);
    }

    /// <summary>O painel encolheu de volta até a pílula: a ilha reaparece ali mesmo, sem deslizar.</summary>
    public static void TakeBackFromPanel()
    {
        var w = Current;
        if (w is null || !w._hidden) return;
        w._hidden = false;
        w._slide.BeginAnimation(TranslateTransform.YProperty, null);
        w._slide.Y = 0;
        if (w._topBarShown) w._topBar.BeginAnimation(OpacityProperty, Anim.FromTo(0, 1, 220));
        w.BringAvatarToFront();
        w._popover.GoRest();
    }

    /// <summary>Pílula fechada acompanha os itens que aparecem/somem (timer, música).</summary>
    private void FitCompact()
    {
        if (_popover.IsExpanded || !IsLoaded) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_popover.IsExpanded) return;
            var w = CompactWidth();
            if (Math.Abs(w - _bar.ActualWidth) > 1) ResizeIsland(w, BarHeight, expand: false, CompactCorners.BottomLeft);
        }), DispatcherPriority.Loaded);
    }

    // ---------- Ilha aberta (estilo NotchNook) ----------

    private string _tab = "inicio";
    private bool _dragging;

    /// <summary>Conteúdo da ilha aberta: abas à esquerda, engrenagem à direita e a aba escolhida.</summary>
    private (UIElement, Action?) Dashboard()
    {
        var root = new StackPanel { Width = 600 };

        // Cabeçalho
        var header = new DockPanel { Margin = new Thickness(2, 8, 2, 10) };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        // Hora sempre à vista no menu aberto (clique abre o calendário).
        var headerClock = Text(DateTime.Now.ToString("HH:mm", PtBr), 15, bold: true);
        headerClock.Margin = new Thickness(0, 0, 10, 0);
        CalendarPopup.Attach(headerClock);
        right.Children.Add(headerClock);
        // Ferramentas por escrito (sem ícone).
        var toolsPill = new Border
        {
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(0, 0, 6, 0),
            CornerRadius = new CornerRadius(14),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            Child = Text("Ferramentas", 12.5, bold: true),
        };
        toolsPill.MouseEnter += (_, _) => toolsPill.Background = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
        toolsPill.MouseLeave += (_, _) => toolsPill.Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
        toolsPill.MouseLeftButtonUp += (_, e) => { e.Handled = true; _popover.Close(); MainWindow.Current?.ExpandPinned(); };
        right.Children.Add(toolsPill);
        right.Children.Add(new Widgets.PillButton("", () => { _popover.Close(); SettingsWindow.Open(); }, false, 28) { ToolTip = "Configurações" });
        DockPanel.SetDock(right, Dock.Right);
        header.Children.Add(right);
        var tabs = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (key, glyph, text) in new[] { ("inicio", "", "Início"), ("copiados", "\uE8C8", "Copiados"), ("sistema", "", "Sistema"), ("tempo", "\uE916", "Tempo") })
        {
            var selected = _tab == key;
            var icon = Glyph(glyph);
            icon.FontSize = 12;
            var label = Text(text, 12.5, bold: selected);
            var pill = new Border
            {
                Padding = new Thickness(10, 5, 12, 5),
                Margin = new Thickness(0, 0, 4, 0),
                CornerRadius = new CornerRadius(9),
                Cursor = Cursors.Hand,
                Background = selected ? new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent,
                Opacity = selected ? 1 : 0.6,
                Child = Row(icon, label),
            };
            var k = key;
            pill.MouseEnter += (_, _) => { if (_tab != k) pill.Opacity = 0.9; };
            pill.MouseLeave += (_, _) => { if (_tab != k) pill.Opacity = 0.6; };
            pill.MouseLeftButtonUp += (_, _) =>
            {
                if (_tab == k) return;
                _tab = k;
                _popover.Reflow();
            };
            tabs.Children.Add(pill);
        }
        header.Children.Add(tabs);
        root.Children.Add(header);

        (UIElement View, Action? Update) body = _tab switch
        {
            "copiados" => ClipView(),
            "sistema" => SystemView(),
            "tempo" => TimeView(),
            _ => HomeView(),
        };
        root.Children.Add(body.View);
        var bodyUpdate = body.Update;
        return (root, () =>
        {
            headerClock.Text = DateTime.Now.ToString("HH:mm", PtBr);
            bodyUpdate?.Invoke();
        });
    }

    private static Border Card(UIElement child, double width = double.NaN) => new()
    {
        Width = width,
        CornerRadius = new CornerRadius(18),
        Padding = new Thickness(12),
        Margin = new Thickness(0, 0, 8, 0),
        Background = new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)),
        Child = child,
    };

    // Início: música (ou timer/saudação) + semana + avatar
    private (UIElement, Action?) HomeView()
    {
        var media = MediaService.Instance;
        var grid = new Grid { Height = 108 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Action? update = null;

        // Coluna 1
        FrameworkElement first;
        if (media.HasSession)
        {
            var cover = new Border { Width = 62, Height = 62, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 12, 0) };
            var title = Text("", 13.5, bold: true);
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            var artist = Muted("");
            artist.TextTrimming = TextTrimming.CharacterEllipsis;
            artist.TextWrapping = TextWrapping.NoWrap;
            var play = new Widgets.PillButton("", media.TogglePlayPause, false, 28);
            var controls = Row(new Widgets.PillButton("", media.Previous, false, 26), play, new Widgets.PillButton("", media.Next, false, 26));
            controls.Margin = new Thickness(-6, 4, 0, 0);
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(title);
            info.Children.Add(artist);
            info.Children.Add(controls);
            var dock = new DockPanel();
            DockPanel.SetDock(cover, Dock.Left);
            dock.Children.Add(cover);
            dock.Children.Add(info);
            first = Card(dock);
            update += () =>
            {
                title.Text = media.Title;
                artist.Text = media.Artist;
                if (media.Cover is { } img) cover.Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
                else cover.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                play.Glyph = media.IsPlaying ? "" : "";
            };
        }
        else
        {
            var hour = DateTime.Now.Hour;
            var hello = Text(hour < 12 ? "Bom dia" : hour < 18 ? "Boa tarde" : "Boa noite", 15, bold: true);
            hello.Opacity = 0.85;
            var bigClock = Text("", 34, bold: true);
            bigClock.Margin = new Thickness(0, -4, 0, -2);
            var timerLine = Muted("");
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(hello);
            stack.Children.Add(bigClock);
            stack.Children.Add(timerLine);
            first = Card(stack);
            CalendarPopup.Attach(first);
            update += () =>
            {
                bigClock.Text = DateTime.Now.ToString("HH:mm", PtBr);
                var t = TimerService.Instance;
                var sw = StopwatchService.Instance;
                timerLine.Text = t.State != TimerState.Idle ? "Timer: " + (t.State == TimerState.Finished ? "acabou!" : TimerService.Format(t.Remaining))
                    : sw.IsActive ? "Cronômetro: " + StopwatchService.Short(sw.Elapsed)
                    : "Nada tocando agora";
            };
        }
        grid.Children.Add(first);

        // Coluna 2: calendário da semana, com a hora no começo.
        var week = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var monthRow = new DockPanel();
        var weekClock = Text(DateTime.Now.ToString("HH:mm", PtBr), 22, bold: true);
        weekClock.Margin = new Thickness(0, -2, 10, 0);
        DockPanel.SetDock(weekClock, Dock.Left);
        monthRow.Children.Add(weekClock);
        var month = Text(Capital(DateTime.Now.ToString("MMM", PtBr).TrimEnd('.')), 11, bold: true);
        month.Opacity = 0.6;
        month.Margin = new Thickness(0, 0, 0, 6);
        week.Children.Add(month);
        update += () => weekClock.Text = DateTime.Now.ToString("HH:mm", PtBr);
        var days = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Columns = 7 };
        var today = DateTime.Today;
        for (var i = -3; i <= 3; i++)
        {
            var d = today.AddDays(i);
            var isToday = i == 0;
            var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var dow = Text(d.ToString("ddd", PtBr).TrimEnd('.')[..Math.Min(3, d.ToString("ddd", PtBr).TrimEnd('.').Length)].ToUpper(PtBr), 8.5, bold: true);
            dow.HorizontalAlignment = HorizontalAlignment.Center;
            dow.Opacity = isToday ? 1 : 0.45;
            var num = Text(d.Day.ToString("00"), 14, bold: isToday);
            num.HorizontalAlignment = HorizontalAlignment.Center;
            num.Opacity = isToday ? 1 : 0.45;
            if (isToday)
            {
                dow.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                num.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            }
            col.Children.Add(dow);
            col.Children.Add(num);
            days.Children.Add(col);
        }
        monthRow.Children.Add(days);
        week.Children.Add(monthRow);
        var sub = Muted(Capital(DateTime.Now.ToString("dddd, d 'de' MMMM", PtBr)));
        sub.HorizontalAlignment = HorizontalAlignment.Center;
        sub.Margin = new Thickness(0, 6, 0, 0);
        week.Children.Add(sub);
        var weekCard = Card(week);
        weekCard.Cursor = Cursors.Hand;
        CalendarPopup.Attach(weekCard);
        Grid.SetColumn(weekCard, 1);
        grid.Children.Add(weekCard);

        // Coluna 3: avatar (abre a escolha de avatar: personagens, animados ou sua foto)
        var avatar = new Border { Width = 70, Height = 70, CornerRadius = new CornerRadius(35), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Trocar avatar" };
        if (Avatars.Find(_settings.Avatar) is { } chosen) avatar.Child = Avatars.View(chosen, 70);
        else
        {
            avatar.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            var g = Glyph("");
            g.FontSize = 26;
            g.Margin = new Thickness(0);
            g.HorizontalAlignment = HorizontalAlignment.Center;
            avatar.Child = g;
        }
        avatar.MouseLeftButtonUp += (_, e) => { e.Handled = true; ShowAvatarPicker(avatar); };
        var avatarCard = Card(avatar);
        avatarCard.Margin = new Thickness(0);
        avatarCard.Padding = new Thickness(14);
        Grid.SetColumn(avatarCard, 2);
        grid.Children.Add(avatarCard);

        return (grid, update);
    }

    // ---------- Escolher avatar (clique no avatar da ilha) ----------

    private System.Windows.Controls.Primitives.Popup? _avatarPopup;

    private void ShowAvatarPicker(FrameworkElement anchor)
    {
        _avatarPopup?.SetCurrentValue(System.Windows.Controls.Primitives.Popup.IsOpenProperty, false);
        var wrap = new WrapPanel { Width = 6 * 52 };
        void Option(string? id, string name, Avatar? a)
        {
            var selected = (_settings.Avatar ?? "") == (id ?? "");
            FrameworkElement pic = a is not null ? Avatars.View(a, 40) : new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(20),
                Child = Glyph("\uE76C"),
            };
            if (a is null) ((Border)pic).SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            var cell = new Border
            {
                Width = 48,
                Height = 48,
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(24),
                BorderThickness = new Thickness(2),
                Cursor = Cursors.Hand,
                ToolTip = name,
                Child = pic,
            };
            if (selected) cell.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            else cell.BorderBrush = Brushes.Transparent;
            cell.MouseEnter += (_, _) => Anim.Pop(pic);
            cell.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                ChooseAvatar(id);
            };
            wrap.Children.Add(cell);
        }
        Option(null, "Sem avatar", null);
        if (Avatars.Photo is { } photo) Option(Avatars.PhotoId, "Minha foto", photo);
        foreach (var a in Avatars.All) Option(a.Id, a.Name + (a.IsAnimated ? " (animado)" : ""), a);

        var photoButton = Ui.Btn("Usar minha foto…", () => PickPhoto(), primary: true);
        var more = Ui.Btn("Mais opções", () =>
        {
            if (_avatarPopup is not null) _avatarPopup.IsOpen = false;
            _popover.Close();
            SettingsWindow.Open("Avatar");
        });
        more.Margin = new Thickness(6, 0, 0, 0);
        var buttons = Row(photoButton, more);
        buttons.Margin = new Thickness(0, 10, 0, 0);

        var content = Stack(Text("Escolha seu avatar", 14, bold: true), wrap, buttons);
        ((FrameworkElement)content.Children[0]).Margin = new Thickness(2, 0, 0, 8);
        var card = new Border
        {
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(22),
            Background = IslandBlack,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            Child = content,
        };
        System.Windows.Documents.TextElement.SetForeground(card, Brushes.White);
        _avatarPopup = new System.Windows.Controls.Primitives.Popup
        {
            PlacementTarget = anchor,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            HorizontalOffset = -(6 * 52 + 28 - anchor.ActualWidth) / 2,
            VerticalOffset = 18,
            AllowsTransparency = true,
            StaysOpen = false,
            Child = card,
        };
        // Enquanto escolhe, a ilha não fecha.
        _dragging = true;
        _avatarPopup.Closed += (_, _) =>
        {
            _dragging = false;
            if (!_bar.IsMouseOver) _popover.Close();
        };
        _avatarPopup.IsOpen = true;
        card.BeginAnimation(OpacityProperty, Anim.FromTo(0, 1, 160));
    }

    private void PickPhoto()
    {
        if (_avatarPopup is not null) _avatarPopup.IsOpen = false;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Escolha uma foto para o avatar",
            Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|Todos os arquivos|*.*",
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            _settings.AvatarPhoto = Avatars.SavePhoto(dialog.FileName);
            Avatars.PhotoPath = _settings.AvatarPhoto;
            if (Avatars.Photo is null)
            {
                TrayIcon.Notify("Não deu para usar essa foto", "Escolha um arquivo de imagem (PNG ou JPG).");
                return;
            }
            ChooseAvatar(Avatars.PhotoId);
        }
        catch (Exception ex)
        {
            TrayIcon.Notify("Não deu para usar essa foto", ex.Message);
        }
    }

    /// <summary>Troca o avatar e atualiza tudo que mostra ele (alça, ilha em todas as telas).</summary>
    private void ChooseAvatar(string? id)
    {
        if (_avatarPopup is not null) _avatarPopup.IsOpen = false;
        _settings.Avatar = id;
        _settings.Save();
        MainWindow.Current?.ApplySettings();
        RefreshAvatar();
    }

    // Copiados: histórico do Ctrl+C (clique copia de novo).
    private (UIElement, Action?) ClipView()
    {
        var header = new DockPanel { Margin = new Thickness(4, 0, 4, 8) };
        var count = Muted("");
        DockPanel.SetDock(count, Dock.Right);
        header.Children.Add(count);
        header.Children.Add(Text("Clique em um item para copiar de novo", 12.5, bold: true));
        var list = new ClipboardQuick(4, island: true);
        list.MenuOpenChanged += open => _dragging = open;
        var root = new StackPanel();
        root.Children.Add(header);
        root.Children.Add(list);
        return (root, () =>
        {
            var n = ClipboardHistory.Instance.Entries.Count;
            count.Text = n == 1 ? "1 item" : $"{n} itens";
        });
    }

    private string _timeMode = "timer";

    // Tempo: temporizador (padrão) ou cronômetro.
    private (UIElement, Action?) TimeView()
    {
        var timer = TimerService.Instance;
        var watch = StopwatchService.Instance;
        var grid = new Grid { Height = 118 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Esquerda: escolher o modo e o mostrador grande.
        var modes = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var (key, text) in new[] { ("timer", "Temporizador"), ("cronometro", "Cronômetro") })
        {
            var selected = _timeMode == key;
            var pill = new Border
            {
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 4, 0),
                CornerRadius = new CornerRadius(8),
                Cursor = Cursors.Hand,
                Background = selected ? new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent,
                Opacity = selected ? 1 : 0.6,
                Child = Text(text, 12, bold: selected),
            };
            var k = key;
            pill.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                if (_timeMode == k) return;
                _timeMode = k;
                _popover.Reflow();
            };
            modes.Children.Add(pill);
        }
        var big = new TextBlock
        {
            FontSize = 38,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            FontFamily = (FontFamily)Application.Current.Resources["MonoFont"],
            Margin = new Thickness(2, 8, 0, 0),
        };
        var progress = new Border { Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Width = 0, Margin = new Thickness(2, 6, 0, 0) };
        progress.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        var left = new StackPanel();
        left.Children.Add(modes);
        left.Children.Add(big);
        left.Children.Add(progress);
        var leftCard = Card(left);
        grid.Children.Add(leftCard);

        // Direita: atalhos de tempo e botões.
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var play = new Widgets.PillButton("\uE768", () => { }, true, 34);
        var reset = new Widgets.PillButton("\uE72C", () => { }, false, 34) { ToolTip = "Zerar" };
        var controls = Row(play, reset);
        controls.Margin = new Thickness(0, 6, 0, 0);
        Action update;
        if (_timeMode == "timer")
        {
            var presets = new WrapPanel();
            foreach (var min in new[] { 1, 3, 5, 10, 15, 25, 30, 60 })
            {
                var m = min;
                var chip = new Border
                {
                    Padding = new Thickness(9, 4, 9, 4),
                    Margin = new Thickness(0, 0, 5, 5),
                    CornerRadius = new CornerRadius(8),
                    Cursor = Cursors.Hand,
                    Background = new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF)),
                    Child = Text(m < 60 ? $"{m} min" : "1 h", 12),
                };
                chip.MouseEnter += (_, _) => chip.Background = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
                chip.MouseLeave += (_, _) => chip.Background = new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF));
                chip.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    timer.Start(TimeSpan.FromMinutes(m), m < 60 ? $"{m} min" : "1 hora");
                };
                presets.Children.Add(chip);
            }
            right.Children.Add(presets);
            play.Clicked += () =>
            {
                if (timer.State == TimerState.Idle) timer.Start(TimeSpan.FromMinutes(5), "5 min");
                else if (timer.State == TimerState.Finished) { timer.MuteAlarm(); timer.Reset(); }
                else timer.TogglePause();
            };
            reset.Clicked += () => { timer.MuteAlarm(); timer.Reset(); };
            update = () =>
            {
                big.Text = timer.State == TimerState.Idle ? "05:00"
                    : timer.State == TimerState.Finished ? "Acabou!" : TimerService.Format(timer.Remaining);
                play.Glyph = timer.State == TimerState.Running ? "\uE769" : "\uE768";
                var w = left.ActualWidth > 0 ? left.ActualWidth - 4 : 220;
                progress.Width = timer.State == TimerState.Idle ? 0 : Math.Max(0, (1 - timer.Progress) * w);
            };
        }
        else
        {
            var laps = Muted("");
            laps.MaxWidth = 300;
            right.Children.Add(laps);
            var lap = new Widgets.PillButton("\uE7C1", watch.Lap, false, 34) { ToolTip = "Volta" };
            controls.Children.Add(lap);
            play.Clicked += watch.Toggle;
            reset.Clicked += watch.Reset;
            update = () =>
            {
                big.Text = StopwatchService.Format(watch.Elapsed);
                play.Glyph = watch.IsRunning ? "\uE769" : "\uE768";
                laps.Text = watch.Laps.Count == 0 ? "Aperte play para começar; a bandeira marca uma volta."
                    : string.Join("   ", watch.Laps.TakeLast(3));
                progress.Width = 0;
            };
        }
        right.Children.Add(controls);
        var rightCard = Card(right);
        rightCard.Margin = new Thickness(0);
        Grid.SetColumn(rightCard, 1);
        grid.Children.Add(rightCard);
        return (grid, update);
    }

    // Sistema: quatro cartões pequenos lado a lado (CPU, memória, rede, bateria ou disco).
    private (UIElement, Action?) SystemView()
    {
        var m = MetricsService.Instance;
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Height = 92 };

        (Border Card, TextBlock Value, TextBlock Sub) Stat(string glyph, string name, UIElement visual)
        {
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            var g = new TextBlock
            {
                Text = glyph,
                FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
                FontSize = 11,
                Foreground = Brushes.White,
                Opacity = 0.6,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            };
            head.Children.Add(g);
            var label = Text(name, 11);
            label.Opacity = 0.6;
            head.Children.Add(label);
            var value = new TextBlock { FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(0, 3, 0, 0) };
            var sub = new TextBlock { FontSize = 10.5, Foreground = Brushes.White, Opacity = 0.5, TextTrimming = TextTrimming.CharacterEllipsis };
            var stack = new StackPanel();
            stack.Children.Add(head);
            stack.Children.Add(value);
            stack.Children.Add(visual);
            stack.Children.Add(sub);
            var card = Card(stack);
            card.Padding = new Thickness(11, 9, 11, 8);
            card.CornerRadius = new CornerRadius(16);
            card.Margin = new Thickness(0, 0, 6, 0);
            return (card, value, sub);
        }

        (Grid Bar, Action<double> Set) MiniBar(Color color)
        {
            var track = new Grid { Height = 5, Margin = new Thickness(0, 7, 0, 6) };
            track.Children.Add(new Border { CornerRadius = new CornerRadius(2.5), Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)) });
            var fill = new Border { CornerRadius = new CornerRadius(2.5), Background = new SolidColorBrush(color), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            track.Children.Add(fill);
            var current = 0.0;
            void Apply() => fill.Width = Math.Clamp(current, 0, 100) / 100 * Math.Max(0, track.ActualWidth);
            track.SizeChanged += (_, _) => Apply();
            return (track, pct => { current = pct; Apply(); });
        }

        var cpuGraph = new LineGraph { Height = 18, ShowGrid = false, Margin = new Thickness(0, 2, 0, 1) };
        var cpu = Stat("\uE950", "CPU", cpuGraph);
        var (ramBar, setRam) = MiniBar(Color.FromRgb(0xBF, 0x7B, 0xFF));
        var ram = Stat("\uE964", "Memória", ramBar);
        var netGraph = new LineGraph { Height = 18, ShowGrid = false, Maximum = null, Margin = new Thickness(0, 2, 0, 1) };
        var net = Stat("\uE774", "Rede", netGraph);

        var hasBattery = _hasBattery;
        var (lastBar, setLast) = MiniBar(hasBattery ? Color.FromRgb(0x4C, 0xC3, 0x8A) : Color.FromRgb(0x4F, 0x8C, 0xFF));
        var last = Stat(hasBattery ? "\uE83F" : "\uEDA2", hasBattery ? "Bateria" : "Disco C:", lastBar);
        last.Card.Margin = new Thickness(0);

        grid.Children.Add(cpu.Card);
        grid.Children.Add(ram.Card);
        grid.Children.Add(net.Card);
        grid.Children.Add(last.Card);
        grid.Width = 4 * 150 + 18;

        var cores = Environment.ProcessorCount;
        return (grid, () =>
        {
            cpuGraph.SetValues(m.Cpu.Values);
            cpu.Value.Text = $"{m.Cpu.Last:0}%";
            cpu.Sub.Text = $"{cores} núcleos";

            setRam(m.LastMemory.UsedPercent);
            ram.Value.Text = $"{m.LastMemory.UsedPercent:0}%";
            ram.Sub.Text = $"{ByteSize.Format(m.LastMemory.UsedBytes, PtBr)} de {ByteSize.Format(m.LastMemory.TotalBytes, PtBr)}";

            netGraph.SetValues(m.NetReceive.Values, m.NetSend.Values);
            net.Value.Text = "↓ " + Speed(m.NetReceive.Last);
            net.Sub.Text = "↑ " + Speed(m.NetSend.Last);

            if (hasBattery)
            {
                var p = Forms.SystemInformation.PowerStatus;
                var pct = p.BatteryLifePercent * 100;
                setLast(pct);
                last.Value.Text = $"{pct:0}%";
                last.Sub.Text = p.PowerLineStatus == Forms.PowerLineStatus.Online ? "Carregando"
                    : p.BatteryLifeRemaining > 0 ? $"~{p.BatteryLifeRemaining / 3600}h {p.BatteryLifeRemaining % 3600 / 60}min" : "Na bateria";
            }
            else
            {
                try
                {
                    var d = new System.IO.DriveInfo("C");
                    var used = d.TotalSize - d.TotalFreeSpace;
                    var pct = d.TotalSize > 0 ? used * 100.0 / d.TotalSize : 0;
                    setLast(pct);
                    last.Value.Text = $"{pct:0}%";
                    last.Sub.Text = $"{ByteSize.Format(d.TotalFreeSpace, PtBr)} livres";
                }
                catch (Exception)
                {
                    last.Value.Text = "—";
                }
            }
        });
    }

    // ---------- Peças ----------

    private Border Item(UIElement content, Action? click)
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
        if (click is not null)
            item.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                click();
            };
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
