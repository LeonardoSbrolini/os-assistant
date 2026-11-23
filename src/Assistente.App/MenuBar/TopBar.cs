using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Rectangle = System.Windows.Shapes.Rectangle;
using Path = System.IO.Path;
using Assistente.Core.Text;
using Assistente.App.Docking;
using Assistente.App.Services;
using Assistente.App.Tools;
using Forms = System.Windows.Forms;

namespace Assistente.App.MenuBar;

/// <summary>
/// Barra de menus estilo Mac, de ponta a ponta no topo, com a ilha no meio:
/// à esquerda logo, app em uso e menus; à direita status (clima, Wi‑Fi, bateria…) e data/hora.
/// Os itens de cada lado são escolhidos em Configurações › Barra de menus.
/// </summary>
internal sealed class TopBar : Grid
{
    public static readonly (string Key, string Name)[] LeftCatalog =
    {
        ("windows", "Logo do Windows (abre o Iniciar)"),
        ("app", "Nome do app em uso"),
        ("arquivos", "Menu Arquivos (suas pastas)"),
        ("apps", "Menu Apps (seus atalhos)"),
        ("janelas", "Menu Janelas (trocar de janela)"),
        ("sistema", "Menu Windows (ferramentas)"),
        ("assistente", "Menu Assistente"),
    };

    public static readonly (string Key, string Name)[] RightCatalog =
    {
        ("clima", "Clima"),
        ("musica", "Música tocando"),
        ("timer", "Timer e cronômetro"),
        ("cpu", "Uso da CPU"),
        ("memoria", "Uso da memória"),
        ("rede", "Velocidade da rede"),
        ("wifi", "Wi‑Fi / rede"),
        ("volume", "Volume"),
        ("bateria", "Bateria"),
        ("data", "Data e hora"),
    };

    public static readonly string[] DefaultLeft = { "windows", "app", "arquivos", "apps", "janelas", "sistema" };
    public static readonly string[] DefaultRight = { "clima", "musica", "timer", "cpu", "wifi", "volume", "bateria", "data" };

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly AppSettings _settings;
    private readonly Action<string> _openIsland;
    private readonly StackPanel _left = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(10, 0, 0, 0) };
    private readonly StackPanel _right = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 10, 0) };
    private readonly List<Action> _refreshers = new();
    private IntPtr _lastApp;
    private int _menusOpen;

    /// <summary>Algum menu da barra está aberto (a barra não deve sumir).</summary>
    public bool MenuOpen => _menusOpen > 0;

    public TopBar(AppSettings settings, Action<string> openIsland, double height)
    {
        _settings = settings;
        _openIsland = openIsland;
        Height = height;
        VerticalAlignment = VerticalAlignment.Top;
        Children.Add(_left);
        Children.Add(_right);
        WeatherService.Instance.Changed += Refresh;
        Rebuild();
    }

    public static List<string> LeftItems(AppSettings s) => s.TopBarLeft ?? DefaultLeft.ToList();
    public static List<string> RightItems(AppSettings s) => s.TopBarRight ?? DefaultRight.ToList();

    /// <summary>Remonta os itens (mudou a configuração).</summary>
    public void Rebuild()
    {
        Background = _settings.TopBarStyle switch
        {
            "preta" => new SolidColorBrush(Color.FromRgb(0, 0, 0)),
            "transparente" => new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
            _ => new SolidColorBrush(Color.FromArgb(0x8C, 0, 0, 0)),
        };
        // Sem fundo, uma sombra leve deixa o texto legível em papel de parede claro.
        Effect = _settings.TopBarStyle == "transparente"
            ? new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 0, Opacity = 0.8 }
            : null;

        _left.Children.Clear();
        _right.Children.Clear();
        _refreshers.Clear();
        foreach (var key in LeftItems(_settings))
            if (BuildLeft(key) is { } el) _left.Children.Add(el);
        foreach (var key in RightItems(_settings))
            if (BuildRight(key) is { } el) _right.Children.Add(el);
        Refresh();
    }

    public void Refresh()
    {
        foreach (var r in _refreshers)
        {
            try { r(); } catch (Exception) { }
        }
    }

    // ---------- Esquerda ----------

    private FrameworkElement? BuildLeft(string key)
    {
        switch (key)
        {
            case "windows":
            {
                var logo = new Grid { Width = 13, Height = 13, VerticalAlignment = VerticalAlignment.Center };
                for (var i = 0; i < 4; i++)
                {
                    var r = new Rectangle { Width = 6, Height = 6, Fill = Brushes.White, HorizontalAlignment = i % 2 == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right, VerticalAlignment = i < 2 ? VerticalAlignment.Top : VerticalAlignment.Bottom };
                    logo.Children.Add(r);
                }
                var item = Item(logo, "Iniciar");
                item.Padding = new Thickness(10, 0, 10, 0);
                item.MouseLeftButtonUp += (_, _) => Native.OpenStartMenu();
                return item;
            }
            case "app":
            {
                var name = Label("", bold: true);
                var item = Item(name, null);
                _refreshers.Add(() => name.Text = ForegroundName());
                item.MouseLeftButtonUp += (_, _) => ShowMenu(item, AppMenu());
                return item;
            }
            case "arquivos":
                return MenuItemLabel("Arquivos", FilesMenu);
            case "apps":
                return MenuItemLabel("Apps", AppsMenu);
            case "janelas":
                return MenuItemLabel("Janelas", WindowsMenu);
            case "sistema":
                return MenuItemLabel("Windows", SystemMenu);
            case "assistente":
                return MenuItemLabel("Assistente", AssistantMenu);
        }
        return null;
    }

    private Border MenuItemLabel(string text, Func<IEnumerable<(string, Action?)>> items)
    {
        var item = Item(Label(text), null);
        item.MouseLeftButtonUp += (_, _) => ShowMenu(item, items());
        return item;
    }

    private string ForegroundName()
    {
        var fg = Native.Foreground;
        var pid = fg == IntPtr.Zero ? 0 : Native.GetWindowProcessId(fg);
        if (pid == 0 || pid == (uint)Environment.ProcessId)
            fg = _lastApp;
        else
            _lastApp = fg;
        if (fg == IntPtr.Zero || Native.ForegroundAppRect() is null && fg == Native.Foreground) return "Área de trabalho";
        try
        {
            var path = Native.GetProcessPath(Native.GetWindowProcessId(fg));
            if (string.IsNullOrEmpty(path)) return "Área de trabalho";
            if (Path.GetFileName(path).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
                return Native.ForegroundAppRect() is null ? "Área de trabalho" : "Explorador de Arquivos";
            var desc = FileVersionInfo.GetVersionInfo(path).FileDescription;
            var name = string.IsNullOrWhiteSpace(desc) ? Path.GetFileNameWithoutExtension(path) : desc.Trim();
            return name.Length > 28 ? name[..27] + "…" : name;
        }
        catch (Exception)
        {
            return "Área de trabalho";
        }
    }

    private IEnumerable<(string, Action?)> AppMenu()
    {
        var app = _lastApp;
        if (app != IntPtr.Zero && IsWindow(app))
        {
            yield return ("Minimizar", () => Native.Minimize(app));
            yield return ("Fechar janela", () => Native.Close(app));
            yield return ("-", null);
        }
        yield return ("Mostrar a área de trabalho", () => Native.WinCombo(0x44));
        yield return ("Visão de tarefas", () => Native.WinCombo(0x09));
        yield return ("Gerenciador de tarefas", () => Launch("taskmgr.exe"));
    }

    private static IEnumerable<(string, Action?)> FilesMenu()
    {
        yield return ("Área de trabalho", () => Launch(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)));
        yield return ("Documentos", () => Launch(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)));
        yield return ("Downloads", () => Launch("explorer.exe", "shell:Downloads"));
        yield return ("Imagens", () => Launch(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)));
        yield return ("Músicas", () => Launch(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)));
        yield return ("Vídeos", () => Launch(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)));
        yield return ("-", null);
        yield return ("Este computador", () => Launch("explorer.exe", "shell:MyComputerFolder"));
        yield return ("Lixeira", () => Launch("explorer.exe", "shell:RecycleBinFolder"));
    }

    private IEnumerable<(string, Action?)> AppsMenu()
    {
        var items = _settings.CornerLeftItems ?? new List<string>();
        foreach (var path in items)
        {
            var p = path;
            string name;
            try { name = Native.DisplayName(p); } catch (Exception) { name = Path.GetFileNameWithoutExtension(p); }
            yield return (name, () => Launch(p));
        }
        if (items.Count > 0) yield return ("-", null);
        yield return ("Todos os apps…", () => Launch("explorer.exe", "shell:AppsFolder"));
    }

    private static IEnumerable<(string, Action?)> WindowsMenu()
    {
        var windows = Native.GetOpenWindows();
        foreach (var w in windows.Take(20))
        {
            var h = w.Handle;
            var title = w.Title.Length > 48 ? w.Title[..47] + "…" : w.Title;
            yield return ((w.Minimized ? "◦ " : "") + title, () => Native.Activate(h));
        }
        if (windows.Count > 0) yield return ("-", null);
        yield return ("Mostrar a área de trabalho", () => Native.WinCombo(0x44));
    }

    private static IEnumerable<(string, Action?)> SystemMenu()
    {
        yield return ("Configurações", () => Launch("ms-settings:"));
        yield return ("Painel de controle", () => Launch("control.exe"));
        yield return ("Gerenciador de tarefas", () => Launch("taskmgr.exe"));
        yield return ("-", null);
        yield return ("Rede e Wi‑Fi", () => Launch("ms-settings:network"));
        yield return ("Bluetooth", () => Launch("ms-settings:bluetooth"));
        yield return ("Som", () => Launch("ms-settings:sound"));
        yield return ("Tela", () => Launch("ms-settings:display"));
        yield return ("Impressoras", () => Launch("ms-settings:printers"));
        yield return ("-", null);
        yield return ("Apps instalados", () => Launch("ms-settings:appsfeatures"));
        yield return ("Windows Update", () => Launch("ms-settings:windowsupdate"));
        yield return ("Terminal", () => Launch("powershell.exe"));
    }

    private IEnumerable<(string, Action?)> AssistantMenu()
    {
        yield return ("Abrir o assistente", () => MainWindow.Current?.ExpandPinned());
        yield return ("Abrir a ilha", () => _openIsland("inicio"));
        yield return ("-", null);
        yield return ("Personalizar esta barra…", () => SettingsWindow.Open("Barra de menus"));
        yield return ("Configurações…", () => SettingsWindow.Open());
    }

    // ---------- Direita ----------

    private FrameworkElement? BuildRight(string key)
    {
        var m = MetricsService.Instance;
        switch (key)
        {
            case "clima":
            {
                var symbol = Label("", size: 14);
                symbol.FontFamily = new FontFamily("Segoe UI Symbol");
                symbol.Margin = new Thickness(0, 0, 5, 0);
                var temp = Label("");
                var item = Item(Row(symbol, temp), "Clima");
                _refreshers.Add(() =>
                {
                    var w = WeatherService.Instance;
                    w.RefreshIfStale();
                    item.Visibility = w.Temperature is null ? Visibility.Collapsed : Visibility.Visible;
                    symbol.Text = w.Symbol;
                    temp.Text = w.Temperature is { } t ? $"{t:0}°" : "";
                    item.ToolTip = w.Temperature is null ? null : $"{w.Description}{(w.City.Length > 0 ? " em " + w.City : "")}";
                });
                item.MouseLeftButtonUp += (_, _) => Launch("https://www.msn.com/pt-br/clima");
                return item;
            }
            case "musica":
            {
                var title = Label("");
                var item = Item(Row(Glyph(""), title), null);
                _refreshers.Add(() =>
                {
                    var media = MediaService.Instance;
                    item.Visibility = media.HasSession ? Visibility.Visible : Visibility.Collapsed;
                    var t = media.Title;
                    title.Text = t.Length > 24 ? t[..23] + "…" : t;
                    item.ToolTip = media.Artist.Length > 0 ? $"{media.Title} · {media.Artist}\nClique para tocar/pausar" : "Clique para tocar/pausar";
                });
                item.MouseLeftButtonUp += (_, _) => MediaService.Instance.TogglePlayPause();
                return item;
            }
            case "timer":
            {
                var glyph = Glyph("");
                var text = Label("");
                text.FontFamily = (FontFamily)Application.Current.Resources["MonoFont"];
                var item = Item(Row(glyph, text), "Abrir na ilha");
                _refreshers.Add(() =>
                {
                    var timer = TimerService.Instance;
                    var watch = StopwatchService.Instance;
                    if (timer.State != TimerState.Idle)
                    {
                        item.Visibility = Visibility.Visible;
                        text.Text = timer.State == TimerState.Finished ? "Acabou!" : TimerService.Format(timer.Remaining);
                    }
                    else if (watch.IsActive)
                    {
                        item.Visibility = Visibility.Visible;
                        text.Text = StopwatchService.Short(watch.Elapsed);
                    }
                    else item.Visibility = Visibility.Collapsed;
                });
                item.MouseLeftButtonUp += (_, _) => _openIsland("inicio");
                return item;
            }
            case "cpu":
            {
                var spark = new LineGraph { Width = 30, Height = 12, ShowGrid = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
                var text = Label("");
                var item = Item(Row(spark, text), "CPU — clique para ver detalhes");
                _refreshers.Add(() =>
                {
                    spark.SetValues(m.Cpu.Values);
                    text.Text = $"{m.Cpu.Last:0}%";
                });
                item.MouseLeftButtonUp += (_, _) => _openIsland("sistema");
                return item;
            }
            case "memoria":
            {
                var text = Label("");
                var item = Item(Row(Glyph(""), text), "Memória — clique para ver detalhes");
                _refreshers.Add(() => text.Text = $"{m.LastMemory.UsedPercent:0}%");
                item.MouseLeftButtonUp += (_, _) => _openIsland("sistema");
                return item;
            }
            case "rede":
            {
                var text = Label("");
                var item = Item(text, "Rede — clique para ver detalhes");
                _refreshers.Add(() => text.Text = "↓ " + ByteSize.Format(m.NetReceive.Last, PtBr) + "/s");
                item.MouseLeftButtonUp += (_, _) => _openIsland("sistema");
                return item;
            }
            case "wifi":
            {
                var glyph = Glyph("");
                glyph.Margin = new Thickness(0);
                var item = Item(glyph, null);
                _refreshers.Add(() =>
                {
                    var (g, tip) = NetworkStatus();
                    glyph.Text = g;
                    item.ToolTip = tip;
                });
                item.MouseLeftButtonUp += (_, _) => Native.WinCombo(0x41);
                return item;
            }
            case "volume":
            {
                var glyph = Glyph("");
                glyph.Margin = new Thickness(0);
                var item = Item(glyph, "Volume — role a rodinha para mudar");
                item.MouseWheel += (_, e) =>
                {
                    if (e.Delta > 0) MediaService.VolumeUp(); else MediaService.VolumeDown();
                    e.Handled = true;
                };
                item.MouseLeftButtonUp += (_, _) => Native.WinCombo(0x41);
                return item;
            }
            case "bateria":
            {
                var glyph = Glyph("");
                var text = Label("");
                var item = Item(Row(glyph, text), null);
                _refreshers.Add(() =>
                {
                    var p = Forms.SystemInformation.PowerStatus;
                    var has = p.BatteryChargeStatus != Forms.BatteryChargeStatus.NoSystemBattery && p.BatteryLifePercent <= 1;
                    item.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
                    if (!has) return;
                    var charging = p.PowerLineStatus == Forms.PowerLineStatus.Online;
                    glyph.Text = charging ? "" : "";
                    text.Text = $"{p.BatteryLifePercent * 100:0}%";
                    item.ToolTip = charging ? "Carregando" : "Na bateria";
                });
                item.MouseLeftButtonUp += (_, _) => Launch("ms-settings:batterysaver");
                return item;
            }
            case "data":
            {
                var text = Label("", bold: true);
                text.FontWeight = FontWeights.Medium;
                var item = Item(text, "Calendário e notificações");
                _refreshers.Add(() =>
                {
                    var now = DateTime.Now;
                    var dow = now.ToString("ddd", PtBr).TrimEnd('.');
                    var mon = now.ToString("MMM", PtBr).TrimEnd('.');
                    text.Text = $"{Cap(dow)} {now.Day} {mon}  {now:HH:mm}";
                });
                item.MouseLeftButtonUp += (_, _) => ShowCalendar(item);
                return item;
            }
        }
        return null;
    }

    private static (string Glyph, string Tip) NetworkStatus()
    {
        try
        {
            var up = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .ToList();
            if (up.Any(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)) return ("", "Wi‑Fi conectado");
            if (up.Any(n => n.NetworkInterfaceType == NetworkInterfaceType.Ethernet)) return ("", "Cabo de rede conectado");
            return ("", "Sem internet");
        }
        catch (Exception)
        {
            return ("", "Rede");
        }
    }

    // ---------- Calendário ----------

    private Popup? _calendar;

    /// <summary>Calendário do mês que desce da data (estilo Mac), com setas para trocar de mês.</summary>
    private void ShowCalendar(FrameworkElement anchor)
    {
        if (_calendar is { IsOpen: true })
        {
            _calendar.IsOpen = false;
            return;
        }
        var month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var body = new StackPanel { Width = 262 };
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x10)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(12, 4, 12, 16),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.5 },
            Child = body,
        };
        var scale = new ScaleTransform(0.92, 0.92);
        card.RenderTransformOrigin = new Point(0.5, 0);
        card.RenderTransform = scale;

        void Render()
        {
            body.Children.Clear();
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var arrows = new StackPanel { Orientation = Orientation.Horizontal };
            arrows.Children.Add(new Widgets.PillButton("\uE76B", () => { month = month.AddMonths(-1); Render(); }, false, 26) { ToolTip = "Mês anterior" });
            arrows.Children.Add(new Widgets.PillButton("\uE8D1", () => { month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1); Render(); }, false, 26) { ToolTip = "Hoje" });
            arrows.Children.Add(new Widgets.PillButton("\uE76C", () => { month = month.AddMonths(1); Render(); }, false, 26) { ToolTip = "Próximo mês" });
            DockPanel.SetDock(arrows, System.Windows.Controls.Dock.Right);
            header.Children.Add(arrows);
            var title = Label(Cap(month.ToString("MMMM", PtBr)) + " " + month.Year, bold: true, size: 15);
            header.Children.Add(title);
            body.Children.Add(header);

            var grid = new UniformGrid { Columns = 7 };
            foreach (var d in new[] { "D", "S", "T", "Q", "Q", "S", "S" })
            {
                var t = Label(d, bold: true, size: 10.5);
                t.Opacity = 0.45;
                t.HorizontalAlignment = HorizontalAlignment.Center;
                t.Margin = new Thickness(0, 0, 0, 4);
                grid.Children.Add(t);
            }
            var start = month.AddDays(-(int)month.DayOfWeek);
            var weeks = ((int)month.DayOfWeek + DateTime.DaysInMonth(month.Year, month.Month) + 6) / 7;
            for (var i = 0; i < weeks * 7; i++)
            {
                var day = start.AddDays(i);
                var isToday = day == DateTime.Today;
                var t = Label(day.Day.ToString(), bold: isToday, size: 12.5);
                t.HorizontalAlignment = HorizontalAlignment.Center;
                t.Opacity = day.Month == month.Month ? (day.DayOfWeek is DayOfWeek.Sunday or DayOfWeek.Saturday ? 0.6 : 1) : 0.25;
                var cell = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Child = t, Margin = new Thickness(1) };
                if (isToday)
                {
                    cell.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                    t.Opacity = 1;
                }
                grid.Children.Add(cell);
            }
            body.Children.Add(grid);

            var footer = new TextBlock
            {
                Text = Cap(DateTime.Now.ToString("dddd, d 'de' MMMM", PtBr)),
                FontSize = 12,
                Foreground = Brushes.White,
                Opacity = 0.6,
                Margin = new Thickness(2, 10, 0, 0),
            };
            var open = new TextBlock { Text = "Notificações", FontSize = 12, Cursor = Cursors.Hand, Margin = new Thickness(0, 10, 2, 0) };
            open.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            open.MouseLeftButtonUp += (_, _) => { if (_calendar is not null) _calendar.IsOpen = false; Native.WinCombo(0x4E); };
            var foot = new DockPanel();
            DockPanel.SetDock(open, System.Windows.Controls.Dock.Right);
            foot.Children.Add(open);
            foot.Children.Add(footer);
            body.Children.Add(foot);
        }

        Render();
        _calendar = new Popup
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.Bottom,
            HorizontalOffset = -150,
            AllowsTransparency = true,
            StaysOpen = false,
            PopupAnimation = PopupAnimation.None,
            Child = card,
        };
        _menusOpen++;
        _calendar.Closed += (_, _) => _menusOpen = Math.Max(0, _menusOpen - 1);
        _calendar.IsOpen = true;
        card.BeginAnimation(OpacityProperty, Anim.FromTo(0, 1, 160));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.To(1, 220, Anim.Back));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.To(1, 220, Anim.Back));
    }

    // ---------- Peças ----------

    private void ShowMenu(FrameworkElement anchor, IEnumerable<(string Header, Action? Run)> items)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom, VerticalOffset = 4 };
        foreach (var (header, run) in items)
        {
            if (header == "-")
            {
                menu.Items.Add(new Separator());
                continue;
            }
            var item = new MenuItem { Header = header };
            if (run is not null)
                item.Click += (_, _) =>
                {
                    try { run(); }
                    catch (Exception ex) { TrayIcon.Notify("Não deu certo", ex.Message); }
                };
            menu.Items.Add(item);
        }
        _menusOpen++;
        menu.Closed += (_, _) => _menusOpen = Math.Max(0, _menusOpen - 1);
        menu.IsOpen = true;
    }

    private static void Launch(string target, string? args = null)
    {
        try
        {
            var psi = new ProcessStartInfo(target) { UseShellExecute = true };
            if (args is not null) psi.Arguments = args;
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            TrayIcon.Notify("Não deu para abrir", ex.Message);
        }
    }

    private static Border Item(UIElement content, string? tip)
    {
        var hover = new SolidColorBrush(Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF));
        var item = new Border
        {
            Child = content,
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(1, 4, 1, 4),
            CornerRadius = new CornerRadius(7),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = tip,
        };
        item.MouseEnter += (_, _) => item.Background = hover;
        item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;
        return item;
    }

    private static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var c in children) row.Children.Add(c);
        return row;
    }

    private static TextBlock Label(string text, bool bold = false, double size = 12.5) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = Brushes.White,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static TextBlock Glyph(string glyph) => new()
    {
        Text = glyph,
        FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
        FontSize = 13,
        Foreground = Brushes.White,
        Margin = new Thickness(0, 0, 5, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpper(s[0], PtBr) + s[1..];

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
}
