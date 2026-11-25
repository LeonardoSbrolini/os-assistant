using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Assistente.App.Services;
using Assistente.App.Tools;

namespace Assistente.App.Docking;

/// <summary>
/// Dock no estilo do macOS: fica embaixo, centralizado, com os programas fixados e abertos.
/// Os ícones crescem perto do mouse, uma bolinha marca os abertos, e ele some quando uma
/// janela encosta nele (volta ao levar o mouse até a borda de baixo).
/// </summary>
public sealed class DockWindow : Window
{
    public static DockWindow? Current { get; private set; }

    private const double ShelfPad = 6;
    private const double BottomMargin = 0;
    private const double EarSize = 14;
    private readonly ScaleTransform _unfold = new(1, 1);
    private int _revealToken;
    /// <summary>Uma única forma preta (prateleira + as duas orelhas), sem emendas.</summary>
    private readonly System.Windows.Shapes.Path _shape = new()
    {
        Fill = Ears.Black,
        IsHitTestVisible = false,
        VerticalAlignment = VerticalAlignment.Bottom,
        HorizontalAlignment = HorizontalAlignment.Center,
        SnapsToDevicePixels = true,
    };
    private const uint AppBarCallback = 0x0400 + 0x1D0; // WM_USER + n

    private readonly AppSettings _settings;
    private readonly Border _shelf = new();
    private readonly Canvas _items = new();
    private readonly Grid _content = new();
    private readonly TranslateTransform _slide = new();
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromMilliseconds(80) };

    private readonly List<DockItem> _entries = new();
    private readonly Dictionary<string, ImageSource?> _storeIcons = new();
    private string _signature = "";
    private IntPtr _lastForeground;
    private IntPtr _hwnd;
    private bool _appBar;
    private bool _hidden;
    private bool _menuOpen;
    private bool _refreshing;
    private bool _animating;
    private double? _mouseX;
    private DateTime _wantHideSince = DateTime.MaxValue;
    private TimeSpan _lastFrame;
    private int _barPx;
    private Rect? _barRect;
    private DateTime _lastBarChange;
    private DateTime _revealedAt = DateTime.MinValue;

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

        // Grudado na borda de baixo como a ilha do topo: cantos de cima arredondados e
        // "orelhas" curvas que se encontram com a borda da tela.
        _shelf.CornerRadius = new CornerRadius(22, 22, 0, 0);
        _shelf.BorderThickness = new Thickness(0);
        _shelf.VerticalAlignment = VerticalAlignment.Bottom;
        _shelf.HorizontalAlignment = HorizontalAlignment.Center;
        _shelf.Margin = new Thickness(0, 0, 0, BottomMargin);
        _shelf.Background = Brushes.Transparent;

        // Quase transparente: a faixa inteira recebe o mouse (para o zoom).
        _items.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        _items.VerticalAlignment = VerticalAlignment.Bottom;
        _items.Margin = new Thickness(0, 0, 0, BottomMargin + ShelfPad);
        _items.MouseMove += (_, e) =>
        {
            // Enquanto o dock está subindo, o mouse "passa" pelos ícones sem querer: sem zoom nesse momento.
            // O zoom só começa quando o mouse sobe para cima dos ícones.
            var pos = e.GetPosition(_items);
            if (_hidden || DateTime.Now - _revealedAt < TimeSpan.FromMilliseconds(380) ||
                (_mouseX is null && pos.Y > _items.ActualHeight - DockItem.DotSpace - 4))
                return;
            _mouseX = pos.X;
            UpdateTargets();
        };
        _items.MouseLeave += (_, _) =>
        {
            _mouseX = null;
            UpdateTargets();
        };

        _items.SizeChanged += (_, _) =>
        {
            Layout();
            UpdateTargets();
        };
        _content.RenderTransformOrigin = new Point(0.5, 1);
        _content.RenderTransform = new TransformGroup { Children = { _unfold, _slide } };
        _content.Children.Add(_shape);
        _content.Children.Add(_shelf);
        _content.Children.Add(_items);
        Content = _content;

        _refresh.Tick += (_, _) => RefreshApps();
        _watch.Tick += (_, _) => Watch();

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
            StopAnimating();
            if (_appBar) Native.UnregisterBar(_hwnd);
            _appBar = false;
        };
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.WorkArea) && !_appBar && IsLoaded) Dispatcher.BeginInvoke(new Action(() => Place(false)));
        };
    }

    private double IconSize => Math.Clamp(_settings.DockIconSize, 32, 80);
    private double Magnify => Anim.Enabled ? Math.Clamp(_settings.DockMagnify, 1, 2.2) : 1;
    private string HideMode => _settings.DockHideMode is "sempre" or "nunca" ? _settings.DockHideMode : "janela";
    private bool TaskbarHidden => _settings.DockHideTaskbar;

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
        }
        Current._signature = "";
        Current.RefreshApps();
    }

    /// <summary>Ao sair do Assistente: tira a reserva de espaço e devolve a barra do Windows.</summary>
    public static void Shutdown(AppSettings settings)
    {
        Current?.Close();
        Current = null;
        RestoreTaskbar(settings);
    }

    /// <summary>Devolve a barra do Windows como estava (também usado se o app der erro).</summary>
    public static void RestoreTaskbar(AppSettings settings)
    {
        try
        {
            Services.TaskbarBlocker.Stop();
            Native.SetTaskbarVisible(true);
            if (settings.DockTaskbarWasAutoHide is { } original)
            {
                Native.TaskbarAutoHide = original;
                settings.DockTaskbarWasAutoHide = null;
                settings.Save();
            }
        }
        catch (Exception)
        {
            // Sem barra de tarefas (ex.: outro shell).
        }
    }

    private static void ApplyTaskbar(AppSettings settings)
    {
        try
        {
            // Barra do Windows desativada: some de vez, em todos os monitores, mesmo com o
            // mouse embaixo da tela (vale com ou sem o dock ligado).
            if (settings.DockHideTaskbar)
            {
                if (settings.DockTaskbarWasAutoHide is null)
                {
                    settings.DockTaskbarWasAutoHide = Native.TaskbarAutoHide;
                    settings.Save();
                }
                // "Ocultar automaticamente" libera o espaço da barra; esconder a janela dela
                // impede que ela apareça ao encostar o mouse embaixo.
                if (!Native.TaskbarAutoHide) Native.TaskbarAutoHide = true;
                Native.SetTaskbarVisible(false);
                Services.TaskbarBlocker.Start();
            }
            else
            {
                // Garante que a barra do Windows esteja visível (a v0.9.0 escondia).
                RestoreTaskbar(settings);
            }
        }
        catch (Exception)
        {
            // Sem barra de tarefas: ignora.
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

    /// <summary>Lista as janelas abertas numa thread separada (não trava a interface).</summary>
    private async void RefreshApps()
    {
        if (_refreshing) return;
        _refreshing = true;
        List<OpenWindow> windows;
        try { windows = await Task.Run(Native.GetOpenWindows); }
        catch (Exception) { windows = new(); }
        finally { _refreshing = false; }
        if (Current != this) return;

        var entries = new List<DockItem> { DockItem.StartButton() };
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in Pinned)
        {
            var key = KeyFor(Native.ResolveTarget(path));
            if (!used.Add(key)) continue;
            entries.Add(new DockItem(key, path, Native.DisplayName(path)) { Windows = windows.Where(w => KeyFor(w) == key).ToList() });
        }

        // Programas abertos que não estão fixados aparecem depois de um separador (como no Mac).
        if (_settings.DockShowRunning)
        {
            var running = windows.Where(w => !used.Contains(KeyFor(w))).GroupBy(KeyFor).ToList();
            if (running.Count > 0) entries.Add(DockItem.Separator());
            foreach (var group in running)
            {
                var first = group.First();
                var isStoreApp = IsStoreHost(first.ExePath);
                entries.Add(new DockItem(group.Key, isStoreApp ? null : first.ExePath, isStoreApp ? first.Title : Native.DisplayName(first.ExePath))
                {
                    Windows = group.ToList(),
                    IconOverride = isStoreApp ? StoreIcon(group.Key, first.Handle) : null,
                });
            }
        }

        // Wi‑Fi, volume, relógio e apps em segundo plano ficam no menu do canto de baixo à direita.

        var signature = string.Join("|", entries.Select(e => e.Key)) + "#" + IconSize + Magnify + HideMode + TaskbarHidden;
        if (signature == _signature)
        {
            for (var i = 0; i < entries.Count && i < _entries.Count; i++) _entries[i].Windows = entries[i].Windows;
            UpdateDots();
            return;
        }
        _signature = signature;
        Rebuild(entries);
    }

    private static bool? _hasBattery;

    private static bool HasBattery()
    {
        if (_hasBattery is null)
        {
            try { _hasBattery = System.Windows.Forms.SystemInformation.PowerStatus.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.NoSystemBattery; }
            catch (Exception) { _hasBattery = false; }
        }
        return _hasBattery.Value;
    }

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
        var first = _entries.Count == 0;
        _entries.Clear();
        _items.Children.Clear();
        foreach (var entry in entries)
        {
            if (old.TryGetValue(entry.Key, out var previous)) entry.Scale = previous.Scale;
            entry.Build(IconSize);
            if (!entry.IsSeparator)
            {
                entry.View.MouseLeftButtonUp += (_, _) => OnClick(entry);
                entry.View.MouseUp += (_, e) =>
                {
                    if (e.ChangedButton == MouseButton.Middle) Launch(entry);
                };
                if (entry.Action is null) entry.View.ContextMenu = BuildMenu(entry);
            }
            _entries.Add(entry);
            _items.Children.Add(entry.View);
            if (!first && !old.ContainsKey(entry.Key) && !entry.IsSeparator) Anim.Pop(entry.View);
        }
        _items.Height = IconSize * Magnify + DockItem.DotSpace + 10;
        _shelf.Height = IconSize + DockItem.DotSpace + 2 * ShelfPad;
        Place(false);
        Layout();
        UpdateDots();
    }

    private double BaseWidth() => _entries.Sum(e => e.BaseWidth(IconSize));

    // ---------- Posição ----------

    private void Place(bool force)
    {
        var s = IconSize;
        var width = BaseWidth() + 2 * ShelfPad + s * (Magnify - 1) * 3 + 40;
        var height = s * Magnify + DockItem.DotSpace + ShelfPad + BottomMargin + 34;
        Width = width;
        Height = height;

        var dpi = VisualTreeHelper.GetDpi(this);
        double bottom;
        if (HideMode == "nunca" && _hwnd != IntPtr.Zero)
        {
            // Sempre visível: reserva o espaço (janelas maximizadas param acima do dock).
            var barPx = (int)Math.Ceiling((s + DockItem.DotSpace + 2 * ShelfPad + BottomMargin + 2) * dpi.DpiScaleY);
            if (!_appBar || force || barPx != _barPx || _barRect is null)
            {
                _barRect = Native.RegisterBottomBar(_hwnd, barPx, AppBarCallback, _appBar);
                _appBar = _barRect is not null;
                _barPx = barPx;
                _lastBarChange = DateTime.Now;
            }
            bottom = _barRect is { } r ? r.Bottom / dpi.DpiScaleY : SystemParameters.WorkArea.Bottom;
        }
        else
        {
            if (_appBar) Native.UnregisterBar(_hwnd);
            _appBar = false;
            _barRect = null;
            // Encosta na borda de baixo da tela (por cima da barra do Windows quando aparece).
            bottom = SystemParameters.PrimaryScreenHeight;
        }

        Left = (SystemParameters.PrimaryScreenWidth - width) / 2;
        Top = bottom - height;
        if (HideMode == "nunca") Reveal(true, animate: false);
        else if (!_placedOnce) Reveal(false, animate: false); // começa escondido: só aparece com o mouse embaixo
        _placedOnce = true;
    }

    private bool _placedOnce;

    private double HiddenOffset => IconSize + DockItem.DotSpace + 2 * ShelfPad + BottomMargin + 6;

    private void Reveal(bool show, bool animate = true)
    {
        if (_hidden == !show && animate) return;
        _hidden = !show;
        if (show)
        {
            _revealedAt = DateTime.Now;
            Native.BringToTop(_hwnd);
        }
        else
        {
            _mouseX = null;
            UpdateTargets();
            CloseTips(this);
        }
        var to = show ? 0 : HiddenOffset;
        var token = ++_revealToken;
        _slide.BeginAnimation(TranslateTransform.YProperty, null);
        if (animate)
        {
            // Mesma animação do assistente: desdobra da borda de baixo, sem quique.
            if (show) _slide.Y = 0;
            Anim.Unfold(_content, _unfold, show, 0.6, 0.2, () =>
            {
                if (token != _revealToken) return;
                if (!show) _slide.Y = HiddenOffset;
            });
        }
        else
        {
            _slide.Y = to;
            _unfold.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _unfold.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _content.BeginAnimation(OpacityProperty, null);
            _unfold.ScaleX = _unfold.ScaleY = 1;
            _content.Opacity = 1;
        }
    }

    private static void CloseTips(DependencyObject root)
    {
        if (root is FrameworkElement { ToolTip: ToolTip tip }) tip.IsOpen = false;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            CloseTips(VisualTreeHelper.GetChild(root, i));
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Outra barra mudou: recalcula a faixa reservada (ignorando o aviso da nossa própria mudança).
        if (msg == AppBarCallback && wParam.ToInt32() == 1 /* ABN_POSCHANGED */ && _appBar &&
            DateTime.Now - _lastBarChange > TimeSpan.FromSeconds(1))
            Dispatcher.BeginInvoke(new Action(() => Place(true)));
        return IntPtr.Zero;
    }

    // ---------- Zoom (efeito "lupa") ----------

    /// <summary>Calcula o zoom desejado de cada ícone pela distância até o mouse.</summary>
    private void UpdateTargets()
    {
        var s = IconSize;
        var mag = Magnify;
        var x = (_items.ActualWidth - BaseWidth()) / 2;
        var range = (s + DockItem.Gap) * 2.4;
        foreach (var entry in _entries)
        {
            var w = entry.BaseWidth(s);
            var target = 1.0;
            if (_mouseX is { } mx && !entry.IsSeparator && mag > 1)
            {
                var d = Math.Abs(mx - (x + w / 2));
                if (d < range) target = 1 + (mag - 1) * (Math.Cos(d / range * Math.PI) + 1) / 2;
            }
            entry.TargetScale = target;
            x += w;
        }
        StartAnimating();
    }

    private void StartAnimating()
    {
        if (_animating) return;
        _animating = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnFrame;
    }

    private void StopAnimating()
    {
        if (!_animating) return;
        _animating = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    /// <summary>Uma vez por quadro (sincronizado com a tela) enquanto o zoom estiver mudando.</summary>
    private void OnFrame(object? sender, EventArgs e)
    {
        var now = e is RenderingEventArgs r ? r.RenderingTime : TimeSpan.Zero;
        var dt = _lastFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Clamp((now - _lastFrame).TotalSeconds, 0, 0.05);
        if (now == _lastFrame && _lastFrame != TimeSpan.Zero) return; // mesmo quadro
        _lastFrame = now;

        var k = 1 - Math.Exp(-dt * 20);
        var moving = false;
        foreach (var entry in _entries)
        {
            var diff = entry.TargetScale - entry.Scale;
            if (Math.Abs(diff) < 0.001)
            {
                if (entry.Scale != entry.TargetScale)
                {
                    entry.Scale = entry.TargetScale;
                    entry.ApplyScale();
                }
                continue;
            }
            entry.Scale += diff * k;
            entry.ApplyScale();
            moving = true;
        }
        Layout();
        if (!moving) StopAnimating();
    }

    /// <summary>Posiciona os ícones lado a lado (com o zoom atual), centralizados.</summary>
    private void Layout()
    {
        var s = IconSize;
        var total = _entries.Sum(e => e.SlotWidth(s));
        var x = (_items.ActualWidth > 0 ? _items.ActualWidth : Width) / 2 - total / 2;
        foreach (var entry in _entries)
        {
            var slot = entry.SlotWidth(s);
            var w = entry.BaseWidth(s);
            Canvas.SetLeft(entry.View, x + slot / 2 - w / 2);
            Canvas.SetBottom(entry.View, 0);
            x += slot;
        }
        _shelf.Width = Math.Max(0, total + 2 * ShelfPad);
        UpdateShape();
    }

    /// <summary>
    /// Desenha prateleira e orelhas como um só caminho: canto côncavo à esquerda, cantos de cima
    /// arredondados e canto côncavo à direita, encostando na borda de baixo da tela.
    /// </summary>
    private void UpdateShape()
    {
        var w = _shelf.Width;
        var h = _shelf.Height;
        if (double.IsNaN(w) || double.IsNaN(h) || w <= 0 || h <= 0) return;
        const double e = EarSize;
        var r = Math.Min(22, Math.Min(w / 2, h - e));
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        string F(double v) => v.ToString("0.##", ic);
        var data =
            $"M0,{F(h)} A{F(e)},{F(e)} 0 0 0 {F(e)},{F(h - e)} " +
            $"L{F(e)},{F(r)} A{F(r)},{F(r)} 0 0 1 {F(e + r)},0 " +
            $"L{F(e + w - r)},0 A{F(r)},{F(r)} 0 0 1 {F(e + w)},{F(r)} " +
            $"L{F(e + w)},{F(h - e)} A{F(e)},{F(e)} 0 0 0 {F(2 * e + w)},{F(h)} Z";
        var g = Geometry.Parse(data);
        g.Freeze();
        _shape.Data = g;
        _shape.Width = w + 2 * e;
        _shape.Height = h;
        _shape.Margin = new Thickness(0, 0, 0, BottomMargin);
    }

    // ---------- Mostrar / esconder ----------

    private void Watch()
    {
        var fg = Native.Foreground;
        if (fg != IntPtr.Zero && fg != _lastForeground && (Native.GetWindowProcessId(fg) != (uint)Environment.ProcessId || Native.IsOwnAppWindow(fg)))
        {
            _lastForeground = fg;
            UpdateDots();
        }

        foreach (var entry in _entries) entry.Tick();

        // (Esconder a barra do Windows de vez é com o TaskbarBlocker.)

        if (HideMode == "nunca" || !IsLoaded) return;

        // Alt+Tab / Win+Tab: é só a troca de janela; o dock não aparece nem some por causa disso.
        if (Native.IsSwitcherForeground())
        {
            _wantHideSince = DateTime.MaxValue;
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var (cx, cy) = Native.CursorPosition();
        var shelfLeft = (Left + (Width - _shelf.ActualWidth) / 2) * dpi.DpiScaleX;
        var shelfRight = shelfLeft + _shelf.ActualWidth * dpi.DpiScaleX;
        var dockTop = (Top + Height - BottomMargin - _shelf.ActualHeight) * dpi.DpiScaleY;
        var dockBottom = (Top + Height) * dpi.DpiScaleY;
        var fullscreen = Native.IsForegroundFullscreen();

        // A janela da frente encosta no dock? (na área de trabalho não há janela: o dock fica à vista)
        bool Covered()
        {
            var rect = Native.ForegroundAppRect();
            return rect is { } r && r.Right > shelfLeft && r.Left < shelfRight && r.Bottom > dockTop && r.Top < dockBottom;
        }

        if (_hidden)
        {
            // Mouse na borda de baixo, na altura do dock, ou nenhuma janela por cima: aparece.
            var atEdge = cy >= dockBottom - 4 && cy <= dockBottom + 4 && cx >= shelfLeft - 40 && cx <= shelfRight + 40;
            if (!fullscreen && (atEdge || (HideMode == "janela" && !Covered())))
            {
                _wantHideSince = DateTime.MaxValue;
                Reveal(true);
            }
            return;
        }

        var mouseOnDock = cx >= shelfLeft - 10 && cx <= shelfRight + 10 && cy >= dockTop - IconSize * (Magnify - 1) - 10 && cy <= dockBottom + 2;
        bool wantHide;
        if (mouseOnDock || _menuOpen || TrayPanel.IsOpen) wantHide = false;
        else if (fullscreen || HideMode == "sempre") wantHide = true;
        else wantHide = Covered();

        if (!wantHide)
        {
            _wantHideSince = DateTime.MaxValue;
            return;
        }
        if (_wantHideSince == DateTime.MaxValue) _wantHideSince = DateTime.Now;
        if (DateTime.Now - _wantHideSince > TimeSpan.FromMilliseconds(mouseOnDock ? 0 : 350)) Reveal(false);
    }

    private void UpdateDots()
    {
        foreach (var entry in _entries) entry.SetRunning(entry.Windows.Count > 0, entry.Windows.Any(w => w.Handle == _lastForeground));
    }

    // ---------- Cliques ----------

    private void OnClick(DockItem entry)
    {
        if (entry.IsStart)
        {
            Native.OpenStartMenu();
            return;
        }
        if (entry.Key == "::tray")
        {
            TrayPanel.Toggle(entry.View);
            return;
        }
        if (entry.Action is { } action)
        {
            action();
            return;
        }
        var windows = entry.Windows.Where(w => Native.GetWindowProcessId(w.Handle) != 0).ToList();
        if (windows.Count == 0)
        {
            Launch(entry);
            return;
        }

        var index = windows.FindIndex(w => w.Handle == _lastForeground);
        if (windows.Count == 1 && index == 0 && !Native.IsMinimized(windows[0].Handle))
        {
            Native.Minimize(windows[0].Handle);
            _lastForeground = IntPtr.Zero;
        }
        else
        {
            var target = index >= 0 ? windows[(index + 1) % windows.Count].Handle : windows[0].Handle;
            Native.Activate(target);
            _lastForeground = target;
        }
        UpdateDots();
    }

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
        menu.Closed += (_, _) => _menuOpen = false;

        MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            return item;
        }

        if (entry.IsStart)
        {
            menu.Items.Add(Item("Configurações do dock…", () => SettingsWindow.Open("Dock")));
            if (_settings.DockHideTaskbar)
            {
                menu.Items.Add(Item("Mostrar a barra do Windows", () =>
                {
                    _settings.DockHideTaskbar = false;
                    _settings.Save();
                    Apply(_settings);
                }));
            }
            menu.Items.Add(Item("Desligar o dock", () =>
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
