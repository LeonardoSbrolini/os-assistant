using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Assistente.App.Services;
using Assistente.App.Tools;
using Microsoft.Win32;

namespace Assistente.App.Desktop;

/// <summary>
/// "Tela personalizada": um widget grande no centro da área de trabalho com atalhos (apps,
/// pastas e sites) em ícones grandes. Fica atrás de todas as janelas, como parte do fundo,
/// em uma tela escolhida (ou em todas). Fundo e atalhos podem ser sólidos ou liquid glass.
/// </summary>
public static class DesktopWidget
{
    private static readonly List<DesktopWidgetWindow> Open = new();
    private static AppSettings? _settings;
    private static bool _hooked;

    /// <summary>Telas ligadas, numeradas da esquerda para a direita (como na página de papel de parede).</summary>
    public static List<(string Device, string Label, System.Windows.Forms.Screen Screen)> Screens()
    {
        return System.Windows.Forms.Screen.AllScreens
            .OrderBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y)
            .Select((s, i) => (s.DeviceName, $"Tela {i + 1}{(s.Primary ? " · principal" : "")}", s))
            .ToList();
    }

    /// <summary>Liga/desliga e reconstrói conforme as configurações (chamado ao abrir e ao mudar algo).</summary>
    public static void Apply(AppSettings settings)
    {
        _settings = settings;
        if (!_hooked)
        {
            _hooked = true;
            // Telas ligadas/desligadas ou papel de parede trocado: refaz (o vidro usa o fundo da tela).
            SystemEvents.DisplaySettingsChanged += (_, _) => Later();
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category is UserPreferenceCategory.Desktop) Later();
            };
        }

        // Só esconde quando pedido (mostrar de volta é feito ao desligar a opção).
        if (settings.DesktopHideIcons) DesktopIcons.SetVisible(false);

        foreach (var w in Open) w.Close();
        Open.Clear();
        if (!settings.DesktopWidgetEnabled) return;

        var all = Screens();
        var targets = settings.DesktopWidgetScreen switch
        {
            "todas" => all.Select(s => s.Screen).ToList(),
            "principal" => all.Where(s => s.Screen.Primary).Select(s => s.Screen).ToList(),
            var device => all.Where(s => s.Device == device).Select(s => s.Screen).ToList(),
        };
        // A tela escolhida foi desligada: mostra na principal para não sumir.
        if (targets.Count == 0) targets = all.Where(s => s.Screen.Primary).Select(s => s.Screen).ToList();

        foreach (var screen in targets)
        {
            var window = new DesktopWidgetWindow(settings, screen);
            Open.Add(window);
            window.Show();
        }
    }

    /// <summary>Reconstrói o conteúdo (atalhos/estilo mudaram) sem recriar as janelas.</summary>
    public static void Refresh()
    {
        foreach (var w in Open) w.Rebuild();
    }

    private static DispatcherTimer? _later;

    private static void Later()
    {
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_later is null)
            {
                _later = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                _later.Tick += (_, _) =>
                {
                    _later!.Stop();
                    if (_settings is not null) Apply(_settings);
                };
            }
            _later.Stop();
            _later.Start();
        }));
    }

    // ---------- Atalhos ----------

    internal static void Add(AppSettings s, DesktopShortcut item)
    {
        s.DesktopShortcuts ??= new List<DesktopShortcut>();
        if (s.DesktopShortcuts.Any(x => string.Equals(x.Target, item.Target, StringComparison.OrdinalIgnoreCase))) return;
        s.DesktopShortcuts.Add(item);
        s.Save();
        Refresh();
    }

    /// <summary>Cria o atalho certo para um caminho (pasta ou programa/arquivo).</summary>
    internal static DesktopShortcut FromPath(string path)
    {
        if (Directory.Exists(path))
        {
            var name = new DirectoryInfo(path).Name;
            if (string.IsNullOrEmpty(name)) name = path; // raiz de um disco (C:\)
            return new DesktopShortcut { Name = name, Target = path, Kind = "pasta" };
        }
        if (path.EndsWith(".url", StringComparison.OrdinalIgnoreCase) && ReadUrlFile(path) is { } url)
            return new DesktopShortcut { Name = Path.GetFileNameWithoutExtension(path), Target = url, Kind = "site" };
        return new DesktopShortcut { Name = Docking.Native.DisplayName(path), Target = path, Kind = "app" };
    }

    private static string? ReadUrlFile(string path)
    {
        try
        {
            return File.ReadLines(path).FirstOrDefault(l => l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))?[4..].Trim();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Endereço completo (coloca https:// se faltar).</summary>
    internal static string NormalizeUrl(string text)
    {
        text = text.Trim();
        if (!text.Contains("://", StringComparison.Ordinal)) text = "https://" + text;
        return text;
    }

    internal static string SiteName(string url)
    {
        try
        {
            var host = new Uri(url).Host;
            if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) host = host[4..];
            var first = host.Split('.')[0];
            return first.Length == 0 ? host : char.ToUpper(first[0]) + first[1..];
        }
        catch (Exception)
        {
            return url;
        }
    }

    internal static void Launch(DesktopShortcut item)
    {
        try
        {
            // O widget não pega o foco: deixa o app aberto vir para a frente.
            AllowSetForegroundWindow(-1);
            Process.Start(new ProcessStartInfo(item.Target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            TrayIcon.Notify("Não deu para abrir", $"{item.Name}: {ex.Message}");
        }
    }

    internal static void AddAppDialog(AppSettings settings)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Escolha um aplicativo ou arquivo",
            Filter = "Aplicativos e atalhos|*.exe;*.lnk;*.url;*.appref-ms;*.bat;*.cmd|Todos os arquivos|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) + @"\Programs",
            DereferenceLinks = false,
        };
        if (dialog.ShowDialog() == true) Add(settings, FromPath(dialog.FileName));
    }

    internal static void AddFolderDialog(AppSettings settings)
    {
        var dialog = new OpenFolderDialog { Title = "Escolha uma pasta" };
        if (dialog.ShowDialog() == true) Add(settings, FromPath(dialog.FolderName));
    }

    internal static void AddSiteDialog(AppSettings settings)
    {
        if (InputDialog.Ask("Adicionar site", ("Endereço (ex.: youtube.com)", ""), ("Nome (opcional)", "")) is not { } r) return;
        if (r[0].Trim().Length == 0) return;
        var url = NormalizeUrl(r[0]);
        var name = r[1].Trim().Length > 0 ? r[1].Trim() : SiteName(url);
        Add(settings, new DesktopShortcut { Name = name, Target = url, Kind = "site" });
    }

    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);

    // ---------- Ícones de sites ----------

    private static readonly string FaviconFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Assistente", "Icones");

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly Dictionary<string, Task<ImageSource?>> Favicons = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ícone grande do site (baixado uma vez e guardado no computador).</summary>
    internal static Task<ImageSource?> FaviconAsync(string url)
    {
        string host;
        try { host = new Uri(url).Host; }
        catch (Exception) { return Task.FromResult<ImageSource?>(null); }
        if (host.Length == 0) return Task.FromResult<ImageSource?>(null);
        if (Favicons.TryGetValue(host, out var task)) return task;
        return Favicons[host] = LoadFavicon(host);
    }

    private static async Task<ImageSource?> LoadFavicon(string host)
    {
        var file = Path.Combine(FaviconFolder, string.Concat(host.Split(Path.GetInvalidFileNameChars())) + ".png");
        try
        {
            if (!File.Exists(file))
            {
                var bytes = await Http.GetByteArrayAsync($"https://www.google.com/s2/favicons?domain={Uri.EscapeDataString(host)}&sz=256");
                Directory.CreateDirectory(FaviconFolder);
                await File.WriteAllBytesAsync(file, bytes);
            }
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(file);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            // O Google devolve um globo de 16 px quando não acha nada: melhor a letra.
            return bmp.PixelWidth < 32 ? null : bmp;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>A janela do widget em uma tela.</summary>
internal sealed class DesktopWidgetWindow : Window
{
    private readonly AppSettings _settings;
    private readonly System.Windows.Forms.Screen _screen;
    private readonly Grid _root = new();
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _guard = new() { Interval = TimeSpan.FromSeconds(2) };
    private TextBlock? _time;
    private TextBlock? _date;
    private Grid? _glassLayer;
    private bool _closing;

    private bool GlassPanel => _settings.DesktopWidgetBackground != "solido";
    private bool GlassTiles => _settings.DesktopWidgetTiles != "solido";

    public DesktopWidgetWindow(AppSettings settings, System.Windows.Forms.Screen screen)
    {
        _settings = settings;
        _screen = screen;

        Title = "Assistente · Tela personalizada";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        FontFamily = new FontFamily("Segoe UI");
        Foreground = Brushes.White;
        AllowDrop = true;

        // Começa já dentro da tela certa; a posição exata (em pixels) vem depois de medir.
        var dpi = MainWindow.Current is { } main ? VisualTreeHelper.GetDpi(main) : new DpiScale(1, 1);
        Left = (screen.WorkingArea.Left + screen.WorkingArea.Width / 2.0) / dpi.DpiScaleX - 200;
        Top = (screen.WorkingArea.Top + screen.WorkingArea.Height / 2.0) / dpi.DpiScaleY - 150;

        Content = _root;
        Rebuild();

        SourceInitialized += (_, _) => OnHandle();
        SizeChanged += (_, _) => { Center(); UpdateGlass(); };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(new Action(() => { Center(); UpdateGlass(); }));
        Drop += OnDrop;
        DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Text)
                ? DragDropEffects.Link : DragDropEffects.None;
            e.Handled = true;
        };

        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        // "Mostrar área de trabalho" (Win+D) ou o Explorer reiniciando podem esconder o widget: volta sozinho.
        _guard.Tick += (_, _) => Guard();
        _guard.Start();
        Closing += (_, _) =>
        {
            _closing = true;
            _clockTimer.Stop();
            _guard.Stop();
        };
    }

    // ---------- Janela presa ao fundo ----------

    private IntPtr Handle => new WindowInteropHelper(this).Handle;

    private void OnHandle()
    {
        var hwnd = Handle;
        var ex = GetWindowLong(hwnd, GwlExStyle);
        // Não rouba o foco (as checagens da ilha/dock continuam vendo a área de trabalho) e não aparece no Alt+Tab.
        SetWindowLong(hwnd, GwlExStyle, ex | WsExToolWindow | WsExNoActivate);
        AttachToDesktop();
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        SendToBack();
    }

    /// <summary>Dono = área de trabalho: assim o Win+D não esconde o widget.</summary>
    private void AttachToDesktop()
    {
        var progman = FindWindow("Progman", null);
        if (progman != IntPtr.Zero) SetWindowLongPtr(Handle, GwlpHwndParent, progman);
    }

    private void SendToBack() =>
        SetWindowPos(Handle, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmWindowPosChanging)
        {
            // Sempre atrás das outras janelas.
            var pos = Marshal.PtrToStructure<WindowPos>(lParam);
            pos.HwndInsertAfter = HwndBottom;
            pos.Flags &= ~SwpNoZOrder;
            Marshal.StructureToPtr(pos, lParam, false);
        }
        else if (msg == WmMouseActivate)
        {
            handled = true;
            return (IntPtr)MaNoActivate;
        }
        return IntPtr.Zero;
    }

    private void Guard()
    {
        if (_closing) return;
        var hwnd = Handle;
        if (hwnd == IntPtr.Zero) return;
        if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) ShowWindow(hwnd, SwShowNoActivate);
        if (GetWindowLongPtr(hwnd, GwlpHwndParent) == IntPtr.Zero || GetWindowLongPtr(hwnd, GwlpHwndParent) != FindWindow("Progman", null))
            AttachToDesktop();
    }

    /// <summary>Centraliza na área útil da tela, em pixels (funciona com escalas diferentes em cada tela).</summary>
    private void Center()
    {
        var hwnd = Handle;
        if (hwnd == IntPtr.Zero || ActualWidth <= 0) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var w = (int)Math.Round(ActualWidth * dpi.DpiScaleX);
        var h = (int)Math.Round(ActualHeight * dpi.DpiScaleY);
        var area = _screen.WorkingArea;
        var x = area.Left + (area.Width - w) / 2;
        var y = area.Top + Math.Max(0, (area.Height - h) / 2);
        SetWindowPos(hwnd, HwndBottom, x, y, 0, 0, SwpNoSize | SwpNoActivate);
    }

    // ---------- Conteúdo ----------

    public void Rebuild()
    {
        _root.Children.Clear();
        var items = _settings.DesktopShortcuts ?? new List<DesktopShortcut>();
        var tile = Math.Clamp(_settings.DesktopWidgetTileSize, 64, 160);
        var columns = Math.Clamp(_settings.DesktopWidgetColumns, 3, 10);
        var cellW = tile + Math.Max(28, tile * 0.38);

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

        if (_settings.DesktopWidgetClock)
        {
            _time = new TextBlock
            {
                FontSize = Math.Max(76, tile * 1.05),
                FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
                FontWeight = FontWeights.SemiBold,
                Foreground = new LinearGradientBrush(Colors.White, Color.FromRgb(0xE4, 0xEA, 0xF5), 90),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, -4, 0, 0),
                Effect = TextShadow(strong: true),
            };
            _date = new TextBlock
            {
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, -8, 0, 28),
                Effect = TextShadow(strong: true),
            };
            // Clique na hora ou na data abre o calendário.
            CalendarPopup.Attach(_time);
            CalendarPopup.Attach(_date);
            stack.Children.Add(_time);
            stack.Children.Add(_date);
            UpdateClock();
        }
        else
        {
            _time = _date = null;
        }

        var grid = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Width = cellW * Math.Min(columns, items.Count + 1),
        };
        for (var i = 0; i < items.Count; i++) grid.Children.Add(Shortcut(items[i], i, tile, cellW));
        grid.Children.Add(AddTile(tile, cellW, items.Count == 0));
        stack.Children.Add(grid);

        if (items.Count == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "Arraste apps, pastas ou links para cá, ou clique no +",
                FontSize = 14,
                Opacity = 0.8,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0),
                Effect = TextShadow(),
            });
        }

        var panel = new Border
        {
            CornerRadius = new CornerRadius(38),
            Padding = new Thickness(40, _settings.DesktopWidgetClock ? 22 : 36, 40, 30),
            Child = stack,
            MinWidth = 420,
        };

        var frame = new Grid { Margin = new Thickness(30) }; // espaço para a sombra
        if (GlassPanel)
        {
            _glassLayer = new Grid { IsHitTestVisible = false };
            frame.Children.Add(_glassLayer);
            frame.Children.Add(GlassSkin(38, strong: true));
            panel.Background = Brushes.Transparent;
        }
        else
        {
            _glassLayer = null;
            panel.Background = Frozen(new SolidColorBrush(Color.FromArgb(0xF0, 0x14, 0x14, 0x18)));
            panel.BorderBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)));
            panel.BorderThickness = new Thickness(1);
        }
        frame.Children.Add(panel);
        frame.Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 8, Opacity = GlassPanel ? 0.35 : 0.5, Direction = 270 };
        _root.Children.Add(frame);

        if (IsLoaded) Dispatcher.BeginInvoke(new Action(UpdateGlass), DispatcherPriority.Loaded);
    }

    private void UpdateClock()
    {
        if (_time is null || _date is null) return;
        var now = DateTime.Now;
        _time.Text = now.ToString("HH:mm", Ui.PtBr);
        var date = now.ToString("dddd, d 'de' MMMM", Ui.PtBr);
        _date.Text = char.ToUpper(date[0]) + date[1..];
    }

    /// <summary>Sombra do texto: forte e curta, para ler bem em qualquer papel de parede.</summary>
    private static Effect TextShadow(bool strong = false) => new DropShadowEffect
    {
        BlurRadius = strong ? 14 : 7,
        ShadowDepth = strong ? 2 : 1.5,
        Opacity = strong ? 0.6 : 0.9,
        Direction = 270,
        Color = Colors.Black,
    };

    private static Brush Frozen(Brush b)
    {
        b.Freeze();
        return b;
    }

    /// <summary>
    /// Camadas do "liquid glass": brilho por cima, reflexo no alto, borda clara que pega luz
    /// em cima e embaixo. Vai por cima do fundo (desfocado ou translúcido).
    /// </summary>
    private static Grid GlassSkin(double radius, bool strong)
    {
        var skin = new Grid { IsHitTestVisible = false };
        skin.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(radius),
            Background = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromArgb((byte)(strong ? 0x16 : 0x48), 0xFF, 0xFF, 0xFF), 0),
                new GradientStop(Color.FromArgb((byte)(strong ? 0x04 : 0x1E), 0xFF, 0xFF, 0xFF), 0.55),
                new GradientStop(Color.FromArgb((byte)(strong ? 0x0A : 0x2A), 0xFF, 0xFF, 0xFF), 1),
            }, new Point(0, 0), new Point(1, 1)),
        });
        // Reflexo suave na metade de cima.
        skin.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(radius),
            Background = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), 0),
                new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.45),
            }, 90),
        });
        // Borda que "pega luz".
        skin.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(radius),
            BorderThickness = new Thickness(strong ? 1.4 : 1.2),
            BorderBrush = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF), 0),
                new GradientStop(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF), 0.4),
                new GradientStop(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF), 0.7),
                new GradientStop(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF), 1),
            }, new Point(0.2, 0), new Point(0.8, 1)),
        });
        return skin;
    }

    /// <summary>Fundo do vidro: o próprio papel de parede atrás do widget, bem desfocado.</summary>
    private void UpdateGlass()
    {
        if (_glassLayer is null || _glassLayer.ActualWidth <= 0 || Handle == IntPtr.Zero) return;
        _glassLayer.Children.Clear();
        const double radius = 38;
        _glassLayer.Clip = new RectangleGeometry(new Rect(0, 0, _glassLayer.ActualWidth, _glassLayer.ActualHeight), radius, radius);

        // Sem imagem (cor sólida, slideshow...): vidro escuro translúcido.
        var tint = new Border { Background = Frozen(new LinearGradientBrush(Color.FromArgb(0x60, 0x10, 0x12, 0x1C), Color.FromArgb(0x88, 0x08, 0x08, 0x10), 90)) };

        if (WallpaperBitmap() is { } img)
        {
            try
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                const double pad = 60; // o desfoque precisa de borda para não escurecer os cantos
                var origin = _glassLayer.PointToScreen(new Point(-pad, -pad));
                var size = new Size((_glassLayer.ActualWidth + 2 * pad) * dpi.DpiScaleX, (_glassLayer.ActualHeight + 2 * pad) * dpi.DpiScaleY);
                var b = _screen.Bounds;

                // "Preencher": a imagem cobre a tela inteira, centralizada.
                double iw = img.PixelWidth, ih = img.PixelHeight;
                var scale = Math.Max(b.Width / iw, b.Height / ih);
                var offX = (b.Width - iw * scale) / 2;
                var offY = (b.Height - ih * scale) / 2;
                var vx = (origin.X - b.Left - offX) / scale / iw;
                var vy = (origin.Y - b.Top - offY) / scale / ih;
                var vw = size.Width / scale / iw;
                var vh = size.Height / scale / ih;

                var blurred = new Border
                {
                    Margin = new Thickness(-pad),
                    Background = new ImageBrush(img)
                    {
                        Viewbox = new Rect(vx, vy, vw, vh),
                        ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
                        Stretch = Stretch.Fill,
                    },
                    Effect = new BlurEffect { Radius = 48, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance },
                };
                _glassLayer.Children.Add(blurred);
                // Vidro "fumê": escurece um pouco o fundo para o branco dos textos saltar.
                tint.Background = Frozen(new LinearGradientBrush(Color.FromArgb(0x48, 0x08, 0x0A, 0x14), Color.FromArgb(0x70, 0x05, 0x06, 0x0C), 90));
            }
            catch (Exception)
            {
                // Fica só o vidro translúcido.
            }
        }
        _glassLayer.Children.Add(tint);
        // Toque de cor (luz da cor de destaque num canto e azul no outro): dá vida ao vidro.
        var accent = Application.Current.TryFindResource("AccentBrush") is SolidColorBrush ab ? ab.Color : Color.FromRgb(0x5B, 0x8C, 0xFF);
        _glassLayer.Children.Add(new Border
        {
            Background = new RadialGradientBrush(Color.FromArgb(0x40, accent.R, accent.G, accent.B), Color.FromArgb(0, accent.R, accent.G, accent.B))
            {
                Center = new Point(0.15, 0.05),
                GradientOrigin = new Point(0.15, 0.05),
                RadiusX = 0.7,
                RadiusY = 0.8,
            },
        });
        _glassLayer.Children.Add(new Border
        {
            Background = new RadialGradientBrush(Color.FromArgb(0x30, 0xB0, 0x5C, 0xFF), Color.FromArgb(0, 0xB0, 0x5C, 0xFF))
            {
                Center = new Point(0.9, 1),
                GradientOrigin = new Point(0.9, 1),
                RadiusX = 0.7,
                RadiusY = 0.8,
            },
        });
    }

    private BitmapSource? _wallpaper;
    private string? _wallpaperPath;

    private BitmapSource? WallpaperBitmap()
    {
        var path = WallpaperPath();
        if (path is null) return null;
        if (_wallpaper is not null && _wallpaperPath == path) return _wallpaper;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            bmp.DecodePixelWidth = 1280; // vai ser desfocada: não precisa de resolução
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.EndInit();
            bmp.Freeze();
            _wallpaperPath = path;
            return _wallpaper = bmp;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private string? WallpaperPath()
    {
        var b = _screen.Bounds;
        var mine = WallpaperService.CurrentMonitors().FirstOrDefault(m => m.X == b.Left && m.Width == b.Width);
        if (mine?.Current is { Length: > 0 } current && File.Exists(current)) return current;
        // Cópia que o Windows guarda do papel de parede atual.
        var transcoded = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "Themes", "TranscodedWallpaper");
        return File.Exists(transcoded) ? transcoded : null;
    }

    // ---------- Atalhos ----------

    private FrameworkElement Shortcut(DesktopShortcut item, int index, double size, double cellW)
    {
        var face = TileFace(size);
        var icon = new Grid { Width = size * 0.62, Height = size * 0.62 };
        SetFaceContent(face, icon, size);
        FillIcon(face, icon, item, size);

        var label = TileLabel(item.Name, size, cellW);
        var cell = Cell(face, label, cellW, size);
        cell.ToolTip = item.Kind == "site" ? item.Target : item.Name;
        cell.MouseLeftButtonUp += (_, _) => DesktopWidget.Launch(item);

        var menu = new ContextMenu();
        menu.Items.Add(MenuItem("Abrir", () => DesktopWidget.Launch(item)));
        menu.Items.Add(MenuItem("Renomear…", () =>
        {
            if (InputDialog.Ask("Renomear atalho", ("Nome", item.Name)) is { } r && r[0].Trim().Length > 0)
            {
                item.Name = r[0].Trim();
                Save();
            }
        }));
        if (item.Kind == "site")
        {
            menu.Items.Add(MenuItem("Trocar endereço…", () =>
            {
                if (InputDialog.Ask("Endereço do site", ("Endereço", item.Target)) is { } r && r[0].Trim().Length > 0)
                {
                    item.Target = DesktopWidget.NormalizeUrl(r[0]);
                    Save();
                }
            }));
        }
        menu.Items.Add(new Separator());
        var list = _settings.DesktopShortcuts!;
        var left = MenuItem("Mover para a esquerda", () => Move(index, -1));
        left.IsEnabled = index > 0;
        var right = MenuItem("Mover para a direita", () => Move(index, +1));
        right.IsEnabled = index < list.Count - 1;
        menu.Items.Add(left);
        menu.Items.Add(right);
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("Remover da tela", () =>
        {
            list.Remove(item);
            Save();
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("Configurar a tela personalizada…", () => SettingsWindow.Open("Tela personalizada")));
        cell.ContextMenu = menu;
        return cell;
    }

    private void Move(int index, int delta)
    {
        var list = _settings.DesktopShortcuts!;
        var to = index + delta;
        if (to < 0 || to >= list.Count) return;
        (list[index], list[to]) = (list[to], list[index]);
        Save();
    }

    private void Save()
    {
        _settings.Save();
        DesktopWidget.Refresh();
    }

    private static MenuItem MenuItem(string header, Action click)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => click();
        return item;
    }

    /// <summary>Nome embaixo do ícone: branco, seminegrito e com sombra firme (lê em qualquer fundo).</summary>
    private static TextBlock TileLabel(string text, double size, double cellW) => new()
    {
        Text = text,
        FontSize = Math.Max(13, size * 0.145),
        FontWeight = FontWeights.SemiBold,
        Foreground = Brushes.White,
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextAlignment = TextAlignment.Center,
        MaxWidth = cellW - 6,
        Margin = new Thickness(0, 9, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Center,
        Effect = TextShadow(),
    };

    /// <summary>
    /// Quadrado do atalho. Começa neutro (vidro claro ou grafite) e ganha a cor do ícone em
    /// <see cref="Tint"/>: degradê vivo, brilho no alto e borda que pega luz, estilo ícone de app.
    /// </summary>
    private Border TileFace(double size)
    {
        var radius = size * 0.27;
        var face = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(radius),
            BorderThickness = new Thickness(1.2),
            BorderBrush = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF), 0),
                new GradientStop(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF), 0.5),
                new GradientStop(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF), 1),
            }, 90),
        };
        Tint(face, null);
        return face;
    }

    /// <summary>Pinta o quadrado com a cor do ícone (null = neutro).</summary>
    private void Tint(Border face, Color? color)
    {
        var a = (byte)(GlassTiles ? 0xC0 : 0xFF);
        if (color is not { } c)
        {
            face.Background = GlassTiles
                ? new LinearGradientBrush(Color.FromArgb(0x5A, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF), 90)
                : new LinearGradientBrush(Color.FromRgb(0x3A, 0x3A, 0x44), Color.FromRgb(0x1E, 0x1E, 0x25), 90);
            face.Tag = null;
            return;
        }
        var (h, sat, l) = ToHsl(c);
        var top = FromHsl(h, Math.Min(1, sat + 0.05), Math.Min(0.72, l + 0.14));
        var bottom = FromHsl((h + 12) % 360, Math.Min(1, sat + 0.1), Math.Max(0.2, l - 0.14));
        face.Background = new LinearGradientBrush(
            Color.FromArgb(a, top.R, top.G, top.B), Color.FromArgb(a, bottom.R, bottom.G, bottom.B), new Point(0.2, 0), new Point(0.8, 1));
        face.Tag = c;
    }

    /// <summary>Conteúdo do quadrado: brilho de vidro no alto (atrás) e o ícone com sombra (na frente).</summary>
    private static void SetFaceContent(Border face, FrameworkElement content, double size)
    {
        var radius = size * 0.27;
        var layers = new Grid();
        layers.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(radius, radius, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Height = size * 0.5,
            IsHitTestVisible = false,
            Background = new LinearGradientBrush(Color.FromArgb(0x4C, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 90),
        });
        content.HorizontalAlignment = HorizontalAlignment.Center;
        content.VerticalAlignment = VerticalAlignment.Center;
        content.Effect ??= new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.35, Direction = 270 };
        layers.Children.Add(content);
        face.Child = layers;
    }

    private FrameworkElement Cell(Border face, TextBlock label, double cellW, double size)
    {
        var holder = new Grid { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center };
        holder.Children.Add(face);
        // Sombra que vira um brilho da cor do ícone ao passar o mouse.
        var glow = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 5, Opacity = 0.4, Direction = 270, Color = Colors.Black };
        holder.Effect = glow;

        var scale = new ScaleTransform(1, 1);
        holder.RenderTransformOrigin = new Point(0.5, 0.5);
        holder.RenderTransform = scale;

        var stack = new StackPanel { Width = cellW, Margin = new Thickness(0, 0, 0, 22), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        stack.Children.Add(holder);
        stack.Children.Add(label);

        void To(double v, double ms)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.To(v, ms, Anim.Back));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.To(v, ms, Anim.Back));
        }
        void Glow(bool on)
        {
            glow.Color = on && face.Tag is Color c ? c : Colors.Black;
            glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, Anim.To(on ? 34 : 18, 220));
            glow.BeginAnimation(DropShadowEffect.OpacityProperty, Anim.To(on ? 0.85 : 0.4, 220));
            glow.BeginAnimation(DropShadowEffect.ShadowDepthProperty, Anim.To(on ? 2 : 5, 220));
        }
        stack.MouseEnter += (_, _) => { To(1.1, 180); Glow(true); };
        stack.MouseLeave += (_, _) => { To(1, 200); Glow(false); };
        stack.PreviewMouseLeftButtonDown += (_, _) => To(0.94, 90);
        stack.PreviewMouseLeftButtonUp += (_, _) => To(1.1, 160);
        return stack;
    }

    private void FillIcon(Border face, Grid host, DesktopShortcut item, double size)
    {
        switch (item.Kind)
        {
            case "pasta":
                host.Children.Add(Glyph("\uE8B7", size * 0.48, Colors.White));
                Tint(face, Color.FromRgb(0x2E, 0x8B, 0xFF));
                break;
            case "site":
                var name = DesktopWidget.SiteName(item.Target);
                var letter = Monogram(name, size);
                host.Children.Add(letter);
                Tint(face, FromHsl(NameHue(name), 0.7, 0.5));
                _ = LoadSiteIcon(face, host, letter, item.Target, size);
                break;
            default:
                var image = Docking.Native.GetIcon(item.Target, 256);
                if (image is null) host.Children.Add(Glyph("\uE7AC", size * 0.45, Colors.White));
                else host.Children.Add(new Image { Source = image, Stretch = Stretch.Uniform });
                RenderOptions.SetBitmapScalingMode(host, BitmapScalingMode.HighQuality);
                Tint(face, image is null ? null : IconColor(image));
                break;
        }
    }

    private async Task LoadSiteIcon(Border face, Grid host, UIElement placeholder, string url, double size)
    {
        if (await DesktopWidget.FaviconAsync(url) is not { } img) return;
        host.Children.Remove(placeholder);
        // Favicon num "selo" branco arredondado: fica nítido sobre o quadrado colorido.
        var image = new Image { Source = img, Stretch = Stretch.Uniform, Margin = new Thickness(size * 0.07) };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        host.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(size * 0.18),
            Background = Brushes.White,
            Child = image,
        });
        if (IconColor(img) is { } c) Tint(face, c);
    }

    /// <summary>
    /// Cor "da marca" do ícone: média das cores mais vivas (ignora branco, preto e cinza),
    /// já ajustada para ficar viva e com o branco legível por cima. Null = ícone sem cor.
    /// </summary>
    private static Color? IconColor(ImageSource source)
    {
        try
        {
            if (source is not BitmapSource bmp || bmp.PixelWidth <= 0) return null;
            BitmapSource small = bmp;
            if (bmp.PixelWidth > 48)
            {
                var f = 48.0 / bmp.PixelWidth;
                small = new TransformedBitmap(bmp, new ScaleTransform(f, f));
            }
            var conv = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
            var w = conv.PixelWidth;
            var h = conv.PixelHeight;
            var px = new byte[w * h * 4];
            conv.CopyPixels(px, w * 4, 0);
            double x = 0, y = 0, weight = 0, light = 0;
            for (var i = 0; i < px.Length; i += 4)
            {
                if (px[i + 3] < 140) continue;
                var (hh, ss, ll) = ToHsl(Color.FromRgb(px[i + 2], px[i + 1], px[i]));
                if (ss < 0.25 || ll < 0.12 || ll > 0.9) continue;
                var wgt = ss * (1 - Math.Abs(ll - 0.5));
                x += Math.Cos(hh * Math.PI / 180) * wgt;
                y += Math.Sin(hh * Math.PI / 180) * wgt;
                light += ll * wgt;
                weight += wgt;
            }
            if (weight < w * h * 0.03) return null;
            var hue = (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
            var l = Math.Clamp(light / weight, 0.42, 0.55);
            return FromHsl(hue, 0.72, l);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max - min < 1e-6) return (0, 0, l);
        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h;
        if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
        else if (max == g) h = (b - r) / d + 2;
        else h = (r - g) / d + 4;
        return (h * 60, s, l);
    }

    private static int NameHue(string name) => name.Aggregate(0, (h, ch) => (h * 31 + ch) & 0x7FFFFFFF) % 360;

    private static TextBlock Glyph(string glyph, double fontSize, Color color) => new()
    {
        Text = glyph,
        FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
        FontSize = fontSize,
        Foreground = new SolidColorBrush(color),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Letra do site (enquanto o ícone não chega, ou se não houver); o quadrado já tem a cor.</summary>
    private static TextBlock Monogram(string name, double size) => new()
    {
        Text = name.Length > 0 ? name[..1].ToUpperInvariant() : "?",
        FontSize = size * 0.42,
        FontWeight = FontWeights.Bold,
        Foreground = Brushes.White,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Color FromHsl(double h, double s, double l)
    {
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = l - c / 2;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    private FrameworkElement AddTile(double size, double cellW, bool empty)
    {
        var face = TileFace(size);
        var plus = Glyph("\uE710", size * 0.32, Colors.White);
        plus.Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = 0.5, Direction = 270 };
        SetFaceContent(face, plus, size);
        face.Opacity = empty ? 1 : 0.8;
        var label = TileLabel("Adicionar", size, cellW);
        label.Opacity = 0.9;
        var cell = Cell(face, label, cellW, size);

        var menu = new ContextMenu();
        menu.Items.Add(MenuItem("Aplicativo ou arquivo…", AddApp));
        menu.Items.Add(MenuItem("Pasta…", AddFolder));
        menu.Items.Add(MenuItem("Site…", AddSite));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("Configurar a tela personalizada…", () => SettingsWindow.Open("Tela personalizada")));
        cell.ContextMenu = menu;
        cell.MouseLeftButtonUp += (_, _) =>
        {
            menu.PlacementTarget = cell;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        };
        return cell;
    }

    private void AddApp() => DesktopWidget.AddAppDialog(_settings);
    private void AddFolder() => DesktopWidget.AddFolderDialog(_settings);
    private void AddSite() => DesktopWidget.AddSiteDialog(_settings);

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (var f in files) DesktopWidget.Add(_settings, DesktopWidget.FromPath(f));
        }
        else if (e.Data.GetData(DataFormats.Text) is string text && text.Trim().Length > 0 &&
                 Uri.TryCreate(DesktopWidget.NormalizeUrl(text.Trim().Split('\n')[0]), UriKind.Absolute, out var uri) &&
                 uri.Scheme is "http" or "https")
        {
            DesktopWidget.Add(_settings, new DesktopShortcut { Name = DesktopWidget.SiteName(uri.ToString()), Target = uri.ToString(), Kind = "site" });
        }
        e.Handled = true;
    }

    // ---------- Windows ----------

    private const int GwlExStyle = -20;
    private const int GwlpHwndParent = -8;
    private const int WsExToolWindow = 0x80;
    private const int WsExNoActivate = 0x08000000;
    private const int WmWindowPosChanging = 0x0046;
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const int SwShowNoActivate = 4;
    private static readonly IntPtr HwndBottom = new(1);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public IntPtr Hwnd;
        public IntPtr HwndInsertAfter;
        public int X, Y, Cx, Cy;
        public uint Flags;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string? title);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
}

/// <summary>Caixinha simples para digitar textos (nome, endereço), no visual escuro do app.</summary>
internal static class InputDialog
{
    public static string[]? Ask(string title, params (string Label, string Value)[] fields)
    {
        var boxes = fields.Select(f => Ui.Input(f.Value)).ToArray();
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) });
        for (var i = 0; i < fields.Length; i++) stack.Children.Add(Ui.Field(fields[i].Label, boxes[i]));

        string[]? result = null;
        var window = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            Topmost = true,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
        };
        window.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        var ok = Ui.Btn("OK", () => { result = boxes.Select(b => b.Text).ToArray(); window.Close(); }, primary: true);
        var cancel = Ui.Btn("Cancelar", window.Close);
        ok.Margin = new Thickness(8, 0, 0, 0);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        stack.Children.Add(buttons);

        window.Content = new Border
        {
            CornerRadius = new CornerRadius(20),
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x17)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(22),
            Margin = new Thickness(14),
            Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 4, Opacity = 0.55 },
            Child = stack,
        };
        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) window.Close();
            else if (e.Key == Key.Enter) { result = boxes.Select(b => b.Text).ToArray(); window.Close(); }
        };
        window.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is not TextBox && e.ButtonState == MouseButtonState.Pressed) window.DragMove();
        };
        window.Loaded += (_, _) =>
        {
            window.Activate();
            if (boxes.Length > 0)
            {
                boxes[0].Focus();
                boxes[0].SelectAll();
            }
        };
        window.ShowDialog();
        return result;
    }
}
