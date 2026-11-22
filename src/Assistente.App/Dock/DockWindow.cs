using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Assistente.App.Services;
using Assistente.App.Tools;

namespace Assistente.App.Docking;

/// <summary>
/// Dock no estilo do macOS: fica embaixo, centralizado, com os programas fixados e abertos.
/// Os ícones crescem perto do mouse e uma bolinha marca os programas abertos.
/// </summary>
public sealed class DockWindow : Window
{
    public static DockWindow? Current { get; private set; }

    private const double Gap = 8;
    private const double SeparatorWidth = 13;
    private const double ShelfPad = 6;
    private const double DotSpace = 8;
    private const double BottomMargin = 6;
    private const uint AppBarCallback = 0x0400 + 0x1D0; // WM_USER + n

    private readonly AppSettings _settings;
    private readonly Grid _root = new();
    private readonly Border _shelf = new();
    private readonly StackPanel _items = new() { Orientation = Orientation.Horizontal };
    private readonly Border _hotStrip = new() { Height = 3, VerticalAlignment = VerticalAlignment.Bottom, Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
    private readonly TranslateTransform _slide = new();
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DispatcherTimer _anim = new() { Interval = TimeSpan.FromMilliseconds(15) };
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };

    private readonly List<DockItem> _entries = new();
    private string _signature = "";
    private IntPtr _lastForeground;
    private IntPtr _hwnd;
    private bool _appBar;
    private bool _hidden;
    private bool _menuOpen;
    private bool _fullscreenHidden;
    private double? _mouseX;

    private DockWindow(AppSettings settings)
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
        Title = "Assistente Dock";
        FontFamily = new FontFamily("Segoe UI");

        _shelf.CornerRadius = new CornerRadius(18);
        _shelf.BorderThickness = new Thickness(1);
        _shelf.VerticalAlignment = VerticalAlignment.Bottom;
        _shelf.HorizontalAlignment = HorizontalAlignment.Center;
        _shelf.Margin = new Thickness(0, 0, 0, BottomMargin);
        _shelf.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        _shelf.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        _shelf.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.35 };

        _items.VerticalAlignment = VerticalAlignment.Bottom;
        _items.HorizontalAlignment = HorizontalAlignment.Center;
        _items.Margin = new Thickness(0, 0, 0, BottomMargin + ShelfPad);
        // Quase transparente: faz a faixa inteira receber o mouse (para o zoom).
        _items.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        _items.MouseMove += (_, e) => _mouseX = e.GetPosition(this).X;
        _items.MouseLeave += (_, _) => _mouseX = null;
        _items.SizeChanged += (_, _) => _shelf.Width = Math.Max(0, BaseWidth() + 2 * ShelfPad);

        var content = new Grid { RenderTransform = _slide };
        content.Children.Add(_shelf);
        content.Children.Add(_items);
        _root.Children.Add(content);
        _root.Children.Add(_hotStrip);
        Content = _root;

        MouseEnter += (_, _) =>
        {
            _hideTimer.Stop();
            if (_hidden) Reveal(true);
        };
        MouseLeave += (_, _) =>
        {
            if (_settings.DockAutoHide) _hideTimer.Start();
        };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (!IsMouseOver && !_menuOpen && _settings.DockAutoHide) Reveal(false);
        };

        _refresh.Tick += (_, _) => RefreshApps();
        _watch.Tick += (_, _) => Watch();
        _anim.Tick += (_, _) => Animate();

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            MakeToolWindow(_hwnd);
            HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        };
        Closed += (_, _) =>
        {
            _refresh.Stop();
            _watch.Stop();
            _anim.Stop();
            if (_appBar) Native.UnregisterBar(_hwnd);
            _appBar = false;
        };
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.WorkArea) && !_appBar) Dispatcher.BeginInvoke(new Action(Place));
        };
    }

    private double IconSize => Math.Clamp(_settings.DockIconSize, 32, 80);
    private double Magnify => Math.Clamp(_settings.DockMagnify, 1, 2.2);

    // ---------- Ligar / desligar ----------

    /// <summary>Aplica as configurações do dock (abre, fecha, esconde a barra do Windows...).</summary>
    public static void Apply(AppSettings settings)
    {
        ApplyTaskbar(settings);
        if (!settings.DockEnabled)
        {
            Current?.Close();
            Current = null;
            return;
        }
        if (Current is null)
        {
            Current = new DockWindow(settings);
            Current.Show();
            Current._refresh.Start();
            Current._watch.Start();
            Current._anim.Start();
        }
        Current._signature = "";
        Current.RefreshApps();
    }

    /// <summary>Ao sair do Assistente: tira a reserva de espaço e devolve a barra do Windows.</summary>
    public static void Shutdown(AppSettings settings)
    {
        Current?.Close();
        Current = null;
        if (settings.DockTaskbarWasAutoHide is { } original)
        {
            try { Native.TaskbarAutoHide = original; } catch (Exception) { /* sem barra? */ }
            settings.DockTaskbarWasAutoHide = null;
            settings.Save();
        }
    }

    private static void ApplyTaskbar(AppSettings settings)
    {
        try
        {
            var wantHidden = settings.DockEnabled && settings.DockHideTaskbar;
            if (wantHidden)
            {
                if (settings.DockTaskbarWasAutoHide is null)
                {
                    settings.DockTaskbarWasAutoHide = Native.TaskbarAutoHide;
                    settings.Save();
                }
                if (!Native.TaskbarAutoHide) Native.TaskbarAutoHide = true;
            }
            else if (settings.DockTaskbarWasAutoHide is { } original)
            {
                Native.TaskbarAutoHide = original;
                settings.DockTaskbarWasAutoHide = null;
                settings.Save();
            }
        }
        catch (Exception)
        {
            // Sem barra de tarefas (ex.: outro shell): ignora.
        }
    }

    public static List<string> DefaultPinned()
    {
        var list = new List<string>();
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        list.Add(System.IO.Path.Combine(windows, "explorer.exe"));
        foreach (var candidate in new[]
        {
            @"%ProgramFiles%\Google\Chrome\Application\chrome.exe",
            @"%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe",
            @"%LocalAppData%\Google\Chrome\Application\chrome.exe",
            @"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe",
            @"%ProgramFiles%\Microsoft\Edge\Application\msedge.exe",
        })
        {
            var path = Environment.ExpandEnvironmentVariables(candidate);
            if (File.Exists(path) && !list.Any(p => System.IO.Path.GetFileName(p).Equals(System.IO.Path.GetFileName(path), StringComparison.OrdinalIgnoreCase)))
                list.Add(path);
        }
        foreach (var name in new[] { "notepad.exe", "calc.exe" })
        {
            var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), name);
            if (File.Exists(path)) list.Add(path);
        }
        return list;
    }

    private List<string> Pinned => _settings.DockPinned ??= DefaultPinned();

    // ---------- Programas no dock ----------

    private void RefreshApps()
    {
        List<OpenWindow> windows;
        try { windows = Native.GetOpenWindows(); }
        catch (Exception) { windows = new(); }

        var entries = new List<DockItem> { DockItem.StartButton() };
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in Pinned)
        {
            var key = KeyFor(Native.ResolveTarget(path));
            if (!used.Add(key)) continue;
            entries.Add(new DockItem(key, path, Native.DisplayName(path)) { Windows = windows.Where(w => KeyFor(w) == key).ToList() });
        }

        if (_settings.DockShowRunning)
        {
            var running = windows.Where(w => !used.Contains(KeyFor(w))).GroupBy(KeyFor).ToList();
            if (running.Count > 0) entries.Add(DockItem.Separator());
            foreach (var group in running)
            {
                var first = group.First();
                var isStoreApp = IsStoreHost(first.ExePath);
                var item = new DockItem(group.Key, isStoreApp ? null : first.ExePath, isStoreApp ? first.Title : Native.DisplayName(first.ExePath))
                {
                    Windows = group.ToList(),
                    IconOverride = isStoreApp ? StoreIcon(group.Key, first.Handle) : null,
                };
                entries.Add(item);
            }
        }

        var signature = string.Join("|", entries.Select(e => e.Key + ":" + (e.Windows.Count > 0 ? "1" : "0"))) + IconSize;
        if (signature == _signature)
        {
            // Mesmo conjunto: só atualiza a lista de janelas de cada item.
            for (var i = 0; i < entries.Count && i < _entries.Count; i++) _entries[i].Windows = entries[i].Windows;
            return;
        }
        _signature = signature;
        Rebuild(entries);
    }

    private readonly Dictionary<string, ImageSource?> _storeIcons = new();

    private ImageSource? StoreIcon(string key, IntPtr hwnd)
    {
        if (!_storeIcons.TryGetValue(key, out var icon)) _storeIcons[key] = icon = Native.GetWindowIcon(hwnd);
        return icon;
    }

    private static string KeyFor(OpenWindow w) => IsStoreHost(w.ExePath) ? "store:" + w.Title : KeyFor(w.ExePath);
    private static string KeyFor(string path) => path.Trim().ToLowerInvariant();
    private static bool IsStoreHost(string exe) => System.IO.Path.GetFileName(exe).Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);

    private void Rebuild(List<DockItem> entries)
    {
        var old = _entries.ToDictionary(e => e.Key, e => e);
        _entries.Clear();
        _items.Children.Clear();
        foreach (var entry in entries)
        {
            // Mantém o tamanho atual (zoom) de quem já estava no dock.
            if (old.TryGetValue(entry.Key, out var previous)) entry.Scale = previous.Scale;
            entry.Build(IconSize);
            if (!entry.IsSeparator)
            {
                entry.View.MouseLeftButtonUp += (_, _) => OnClick(entry);
                entry.View.MouseUp += (_, e) =>
                {
                    if (e.ChangedButton == MouseButton.Middle) Launch(entry);
                };
                entry.View.ContextMenu = BuildMenu(entry);
            }
            _entries.Add(entry);
            _items.Children.Add(entry.View);
            if (!old.ContainsKey(entry.Key) && old.Count > 0) Anim.Pop(entry.View);
        }
        _shelf.Height = IconSize + DotSpace + 2 * ShelfPad;
        _hotStrip.Visibility = _settings.DockAutoHide ? Visibility.Visible : Visibility.Collapsed;
        Place();
    }

    private double BaseWidth() => _entries.Sum(e => e.IsSeparator ? SeparatorWidth : IconSize + Gap);

    // ---------- Posição ----------

    private int _barPx;
    private Rect? _barRect;
    private DateTime _lastBarChange;

    private void Place() => Place(force: false);

    private void Place(bool force)
    {
        var s = IconSize;
        var width = BaseWidth() + 2 * ShelfPad + s * (Magnify - 1) * 3 + 40;
        var height = s * Magnify + DotSpace + ShelfPad + BottomMargin + 30;
        Width = width;
        Height = height;

        var dpi = VisualTreeHelper.GetDpi(this);
        var reserve = !_settings.DockAutoHide && _hwnd != IntPtr.Zero;
        double bottom;
        if (reserve)
        {
            var barPx = (int)Math.Ceiling((s + DotSpace + 2 * ShelfPad + BottomMargin + 2) * dpi.DpiScaleY);
            if (!_appBar || force || barPx != _barPx || _barRect is null)
            {
                _barRect = Native.RegisterBottomBar(_hwnd, barPx, AppBarCallback, _appBar);
                _appBar = _barRect is not null;
                _barPx = barPx;
                _lastBarChange = DateTime.Now;
            }
            var rect = _barRect;
            bottom = rect is { } r ? r.Bottom / dpi.DpiScaleY : SystemParameters.WorkArea.Bottom;
        }
        else
        {
            if (_appBar) Native.UnregisterBar(_hwnd);
            _appBar = false;
            _barRect = null;
            bottom = SystemParameters.WorkArea.Bottom;
        }

        var screenW = SystemParameters.PrimaryScreenWidth;
        Left = (screenW - width) / 2;
        Top = bottom - height;

        if (_settings.DockAutoHide && !IsMouseOver) Reveal(false, animate: false);
        else if (!_settings.DockAutoHide) Reveal(true, animate: false);
    }

    private void Reveal(bool show, bool animate = true)
    {
        _hidden = !show;
        var to = show ? 0 : IconSize + DotSpace + 2 * ShelfPad + BottomMargin + 4;
        _slide.BeginAnimation(TranslateTransform.YProperty, animate ? Anim.To(to, show ? 220 : 260) : null);
        if (!animate) _slide.Y = to;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // A barra do Windows (ou outra) mudou: recalcula a faixa reservada. Ignora o aviso
        // causado pela nossa própria mudança para não entrar em loop.
        if (msg == AppBarCallback && wParam.ToInt32() == 1 /* ABN_POSCHANGED */ && _appBar &&
            DateTime.Now - _lastBarChange > TimeSpan.FromSeconds(1))
            Dispatcher.BeginInvoke(new Action(() => Place(force: true)));
        return IntPtr.Zero;
    }

    // ---------- Zoom (efeito "lupa") ----------

    private void Animate()
    {
        if (_entries.Count == 0) return;
        var s = IconSize;
        var mag = Anim.Enabled ? Magnify : 1;
        var x = (Width - BaseWidth()) / 2;
        var range = s * 2.6;
        var changed = false;

        foreach (var entry in _entries)
        {
            var w = entry.IsSeparator ? SeparatorWidth : s + Gap;
            var target = 1.0;
            if (_mouseX is { } mx && !entry.IsSeparator && mag > 1)
            {
                var d = Math.Abs(mx - (x + w / 2));
                if (d < range) target = 1 + (mag - 1) * (Math.Cos(d / range * Math.PI) + 1) / 2;
            }
            x += w;
            if (Math.Abs(entry.Scale - target) > 0.002)
            {
                entry.Scale += (target - entry.Scale) * 0.3;
                entry.ApplyScale(s);
                changed = true;
            }
        }
        if (changed) _shelf.Width = BaseWidth() + 2 * ShelfPad + _entries.Sum(e => e.IsSeparator ? 0 : (e.Scale - 1) * s);
    }

    // ---------- Cliques ----------

    private void Watch()
    {
        var fg = Native.Foreground;
        if (fg != IntPtr.Zero && Native.GetWindowProcessId(fg) != (uint)Environment.ProcessId) _lastForeground = fg;

        // Some quando um jogo/vídeo está em tela cheia.
        var full = fg != IntPtr.Zero && fg == _lastForeground && Native.IsForegroundFullscreen();
        if (full != _fullscreenHidden)
        {
            _fullscreenHidden = full;
            BeginAnimation(OpacityProperty, Anim.To(full ? 0 : 1, 180));
            IsHitTestVisible = !full;
        }

        foreach (var entry in _entries) entry.SetRunning(entry.Windows.Count > 0, entry.Windows.Any(w => w.Handle == _lastForeground));
    }

    private void OnClick(DockItem entry)
    {
        if (entry.IsStart)
        {
            Native.OpenStartMenu();
            return;
        }
        var windows = entry.Windows.Where(w => IsAlive(w.Handle)).ToList();
        if (windows.Count == 0)
        {
            Launch(entry);
            return;
        }

        var index = windows.FindIndex(w => w.Handle == _lastForeground);
        IntPtr target;
        if (windows.Count == 1 && index == 0 && !Native.IsMinimized(windows[0].Handle))
        {
            Native.Minimize(windows[0].Handle);
            _lastForeground = IntPtr.Zero;
            return;
        }
        target = index >= 0 ? windows[(index + 1) % windows.Count].Handle : windows[0].Handle;
        Native.Activate(target);
        _lastForeground = target;
    }

    private static bool IsAlive(IntPtr hwnd) => Native.GetWindowProcessId(hwnd) != 0;

    private void Launch(DockItem entry)
    {
        var path = entry.LaunchPath;
        if (path is null) return;
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            entry.Bounce();
        }
        catch (Exception ex)
        {
            TrayIcon.Notify("Não foi possível abrir", $"{entry.Name}: {ex.Message}");
        }
    }

    private ContextMenu BuildMenu(DockItem entry)
    {
        var menu = new ContextMenu();
        menu.Opened += (_, _) => _menuOpen = true;
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            if (_settings.DockAutoHide && !IsMouseOver) _hideTimer.Start();
        };

        MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            return item;
        }

        if (entry.IsStart)
        {
            menu.Items.Add(Item("Configurações do dock…", () => SettingsWindow.Open("Dock")));
            menu.Items.Add(Item("Esconder o dock", () =>
            {
                _settings.DockEnabled = false;
                _settings.Save();
                Apply(_settings);
            }));
            return menu;
        }

        menu.Items.Add(new MenuItem { Header = entry.Name, IsEnabled = false });
        menu.Items.Add(new Separator());
        if (entry.LaunchPath is not null) menu.Items.Add(Item("Abrir nova janela", () => Launch(entry)));

        var pinned = Pinned.FindIndex(p => KeyFor(Native.ResolveTarget(p)) == entry.Key);
        if (pinned >= 0)
        {
            menu.Items.Add(Item("Remover do dock", () =>
            {
                Pinned.RemoveAt(pinned);
                SaveAndRefresh();
            }));
        }
        else if (entry.LaunchPath is not null)
        {
            menu.Items.Add(Item("Manter no dock", () =>
            {
                Pinned.Add(entry.LaunchPath);
                SaveAndRefresh();
            }));
        }

        menu.Opened += (_, _) =>
        {
            // Opções de janela só quando há janelas abertas.
            foreach (var old in menu.Items.OfType<MenuItem>().Where(i => i.Tag is "win").ToList()) menu.Items.Remove(old);
            var count = entry.Windows.Count;
            if (count == 0) return;
            var close = Item(count > 1 ? $"Fechar {count} janelas" : "Fechar janela", () =>
            {
                foreach (var w in entry.Windows) Native.Close(w.Handle);
            });
            close.Tag = "win";
            menu.Items.Add(close);
        };
        return menu;
    }

    public void SaveAndRefresh()
    {
        _settings.Save();
        _signature = "";
        RefreshApps();
    }

    // ---------- Win32 ----------

    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExAppWindow = 0x00040000;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private static void MakeToolWindow(IntPtr hwnd)
    {
        var style = GetWindowLong(hwnd, GwlExStyle);
        SetWindowLong(hwnd, GwlExStyle, (style | WsExToolWindow) & ~WsExAppWindow);
    }
}
