using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using System.Windows.Threading;
using Assistente.App.Services;
using Assistente.App.Tools;

namespace Assistente.App.Radial;

/// <summary>
/// Menu em círculo que aparece em volta do arquivo que você está arrastando: solte o arquivo
/// numa fatia para zipar, extrair, converter imagem etc.
/// </summary>
internal sealed class RadialMenu : Window
{
    private const double Outer = 132;
    private const double Inner = 46;
    private const double Size = Outer * 2 + 16;

    /// <summary>Uma ação em andamento: para onde salvar e o que foi criado.</summary>
    private sealed class Job
    {
        public required DropTarget Target { get; init; }
        public List<string> Created { get; } = new();
        public string Out(string source) => Target.FolderFor(source);
    }

    private sealed record Slice(string Glyph, string Label, Func<string[], Job, string?> Run);

    private static RadialMenu? _current;
    public static bool IsOpen => _current is { IsVisible: true };

    private readonly List<(Path Shape, Slice Slice)> _slices = new();
    private readonly Ellipse _center;
    private readonly ScaleTransform _scale = new(1, 1);
    private Path? _hot;
    private bool _dropped;
    private DropTarget? _target;

    private static readonly Slice[] Actions =
    {
        new("", "Zipar", Zip),
        new("", "Extrair", Extract),
        new("", "Para PNG", (f, j) => ConvertImages(f, j, "png")),
        new("", "Para JPG", (f, j) => ConvertImages(f, j, "jpg")),
        new("", "Reduzir 50%", ResizeImages),
        new("\uE91B", "Remover fundo", RemoveBackground),
        new("", "Copiar caminho", CopyPaths),
        new("", "Bandeja", AddToTray),
    };

    public static void ShowAt(int screenX, int screenY)
    {
        _current?.Close();
        _current = new RadialMenu { _target = DropTarget.Capture(screenX, screenY) };
        var dpi = VisualTreeHelper.GetDpi(Application.Current.MainWindow ?? _current);
        _current.Left = screenX / dpi.DpiScaleX - Size / 2;
        _current.Top = screenY / dpi.DpiScaleY - Size / 2;
        _current.Show();
        var hwnd = new WindowInteropHelper(_current).Handle;
        Docking.Native.BringToTop(hwnd);
        Anim.Unfold(_current._canvas, _current._scale, true, 0.6, 0.6);
    }

    /// <summary>Soltou o botão esquerdo: se não caiu numa fatia, fecha.</summary>
    public static void LeftReleased()
    {
        var menu = _current;
        if (menu is null) return;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!menu._dropped) menu.FadeClose();
        };
        timer.Start();
    }

    private readonly Canvas _canvas = new() { Width = Size, Height = Size };

    private RadialMenu()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Width = Height = Size;
        Title = "Assistente Círculo";
        FontFamily = new FontFamily("Segoe UI");
        Foreground = Brushes.White;
        AllowDrop = true;

        _canvas.RenderTransformOrigin = new Point(0.5, 0.5);
        _canvas.RenderTransform = _scale;
        var c = Size / 2;

        // Anel preto de fundo (estilo da ilha).
        var ring = new Ellipse { Width = Outer * 2 + 8, Height = Outer * 2 + 8, Fill = new SolidColorBrush(Color.FromArgb(0xF0, 0, 0, 0)) };
        Canvas.SetLeft(ring, c - Outer - 4);
        Canvas.SetTop(ring, c - Outer - 4);
        _canvas.Children.Add(ring);

        var n = Actions.Length;
        var step = 360.0 / n;
        for (var i = 0; i < n; i++)
        {
            var a0 = -90 - step / 2 + i * step + 1.2;
            var a1 = a0 + step - 2.4;
            var shape = new Path
            {
                Data = SliceGeometry(c, a0, a1),
                Fill = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
                AllowDrop = true,
            };
            _canvas.Children.Add(shape);
            _slices.Add((shape, Actions[i]));

            // Ícone + texto no meio da fatia.
            var mid = (a0 + a1) / 2 * Math.PI / 180;
            var r = (Outer + Inner) / 2 + 4;
            var label = new StackPanel { IsHitTestVisible = false, Width = 74 };
            label.Children.Add(new TextBlock { Text = Actions[i].Glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center });
            label.Children.Add(new TextBlock { Text = Actions[i].Label, FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 3, 0, 0) });
            Canvas.SetLeft(label, c + r * Math.Cos(mid) - 37);
            Canvas.SetTop(label, c + r * Math.Sin(mid) - 18);
            _canvas.Children.Add(label);
        }

        // Centro: cancelar.
        _center = new Ellipse { Width = Inner * 2 - 8, Height = Inner * 2 - 8, Fill = new SolidColorBrush(Color.FromArgb(0xFF, 0x1C, 0x1C, 0x1E)), AllowDrop = true };
        Canvas.SetLeft(_center, c - Inner + 4);
        Canvas.SetTop(_center, c - Inner + 4);
        _canvas.Children.Add(_center);
        var x = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 16, Opacity = 0.7, IsHitTestVisible = false };
        Canvas.SetLeft(x, c - 8);
        Canvas.SetTop(x, c - 9);
        _canvas.Children.Add(x);

        Content = _canvas;

        DragOver += OnDragOver;
        DragLeave += (_, _) => Highlight(null);
        Drop += OnDrop;
        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) FadeClose(); };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x80 | 0x08000000);
        };
    }

    private static Geometry SliceGeometry(double c, double a0, double a1)
    {
        Point P(double r, double deg) => new(c + r * Math.Cos(deg * Math.PI / 180), c + r * Math.Sin(deg * Math.PI / 180));
        var fig = new PathFigure { StartPoint = P(Inner, a0), IsClosed = true };
        fig.Segments.Add(new LineSegment(P(Outer, a0), true));
        fig.Segments.Add(new ArcSegment(P(Outer, a1), new System.Windows.Size(Outer, Outer), 0, false, SweepDirection.Clockwise, true));
        fig.Segments.Add(new LineSegment(P(Inner, a1), true));
        fig.Segments.Add(new ArcSegment(P(Inner, a0), new System.Windows.Size(Inner, Inner), 0, false, SweepDirection.Counterclockwise, true));
        var g = new PathGeometry(new[] { fig });
        g.Freeze();
        return g;
    }

    private Slice? SliceAt(Point p)
    {
        foreach (var (shape, slice) in _slices)
            if (shape.Data.FillContains(p)) return slice;
        return null;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        var p = e.GetPosition(_canvas);
        var hit = _slices.FirstOrDefault(s => s.Shape.Data.FillContains(p)).Shape;
        Highlight(hit);
        e.Effects = hit is not null && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Highlight(Path? hot)
    {
        if (ReferenceEquals(hot, _hot)) return;
        if (_hot is not null) _hot.Fill = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
        _hot = hot;
        if (hot is not null) hot.SetResourceReference(Shape.FillProperty, "AccentBrush");
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        _dropped = true;
        e.Effects = DragDropEffects.None;
        var slice = SliceAt(e.GetPosition(_canvas));
        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        FadeClose();
        if (slice is null || files is null || files.Length == 0) return;
        e.Effects = DragDropEffects.Copy;
        var name = slice.Label;
        var job = new Job { Target = _target ?? DropTarget.Capture(-1, -1) };
        var ui = Dispatcher;
        Task.Run(() =>
        {
            string? result;
            try
            {
                result = slice.Run(files, job);
            }
            catch (Exception ex)
            {
                result = "Não deu certo: " + ex.Message;
            }
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!string.IsNullOrEmpty(result)) TrayIcon.Notify(name, result);
                job.Target.Place(job.Created, ui);
            }));
        });
    }

    private void FadeClose()
    {
        if (!IsVisible) return;
        Anim.Unfold(_canvas, _scale, false, 0.7, 0.7, () =>
        {
            Close();
            if (ReferenceEquals(_current, this)) _current = null;
        });
    }

    // ---------- Ações ----------

    private static string Unique(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        var dir = System.IO.Path.GetDirectoryName(path) ?? "";
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        var ext = System.IO.Path.GetExtension(path);
        for (var i = 2; ; i++)
        {
            var candidate = System.IO.Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
    }

    private static string? Zip(string[] files, Job job)
    {
        var first = files[0].TrimEnd('\\');
        var dir = job.Out(first);
        var baseName = files.Length == 1 ? System.IO.Path.GetFileNameWithoutExtension(first) : "Arquivos";
        var zip = Unique(System.IO.Path.Combine(dir, baseName + ".zip"));
        if (files.Length == 1 && Directory.Exists(first))
        {
            ZipFile.CreateFromDirectory(first, zip, CompressionLevel.Optimal, includeBaseDirectory: true);
        }
        else
        {
            using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
            foreach (var f in files)
            {
                if (File.Exists(f)) archive.CreateEntryFromFile(f, System.IO.Path.GetFileName(f), CompressionLevel.Optimal);
                else if (Directory.Exists(f))
                {
                    var root = System.IO.Path.GetDirectoryName(f.TrimEnd('\\')) ?? f;
                    foreach (var file in Directory.EnumerateFiles(f, "*", SearchOption.AllDirectories))
                        archive.CreateEntryFromFile(file, System.IO.Path.GetRelativePath(root, file), CompressionLevel.Optimal);
                }
            }
        }
        job.Created.Add(zip);
        return "Criado " + System.IO.Path.GetFileName(zip);
    }

    private static string? Extract(string[] files, Job job)
    {
        var zips = files.Where(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(f)).ToList();
        if (zips.Count == 0) return "Extrair funciona com arquivos .zip.";
        string? last = null;
        foreach (var z in zips)
        {
            var target = Unique(System.IO.Path.Combine(job.Out(z), System.IO.Path.GetFileNameWithoutExtension(z)));
            ZipFile.ExtractToDirectory(z, target);
            last = target;
            job.Created.Add(target);
        }
        return zips.Count == 1 ? "Extraído em " + System.IO.Path.GetFileName(last) : $"{zips.Count} arquivos extraídos";
    }

    private static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic", ".ico" };

    private static IEnumerable<string> Images(string[] files) =>
        files.Where(f => File.Exists(f) && ImageExts.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant()));

    private static BitmapSource Load(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }

    private static void Save(BitmapSource image, string path, string format)
    {
        BitmapEncoder encoder = format == "jpg" ? new JpegBitmapEncoder { QualityLevel = 92 } : new PngBitmapEncoder();
        // JPG não tem transparência: põe fundo branco.
        if (format == "jpg" && image.Format != PixelFormats.Bgr24)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
                dc.DrawImage(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
            }
            var rtb = new RenderTargetBitmap(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            image = rtb;
        }
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = File.Create(path);
        encoder.Save(output);
    }

    private static string? ConvertImages(string[] files, Job job, string format)
    {
        var images = Images(files).ToList();
        if (images.Count == 0) return "Solte uma imagem (PNG, JPG, BMP, GIF, TIFF, WebP…).";
        string? last = null;
        var done = 0;
        foreach (var img in images)
        {
            var target = Unique(System.IO.Path.Combine(job.Out(img), System.IO.Path.GetFileNameWithoutExtension(img) + "." + format));
            RunSta(() => Save(Load(img), target, format));
            last = target;
            job.Created.Add(target);
            done++;
        }
        return done == 1 ? "Salvo " + System.IO.Path.GetFileName(last) : $"{done} imagens convertidas";
    }

    private static string? ResizeImages(string[] files, Job job)
    {
        var images = Images(files).ToList();
        if (images.Count == 0) return "Solte uma imagem para reduzir.";
        string? last = null;
        foreach (var img in images)
        {
            var ext = System.IO.Path.GetExtension(img).ToLowerInvariant();
            var format = ext is ".jpg" or ".jpeg" ? "jpg" : "png";
            var target = Unique(System.IO.Path.Combine(job.Out(img),
                System.IO.Path.GetFileNameWithoutExtension(img) + " (50%)." + format));
            RunSta(() =>
            {
                var source = Load(img);
                var small = new TransformedBitmap(source, new ScaleTransform(0.5, 0.5));
                small.Freeze();
                Save(small, target, format);
            });
            last = target;
            job.Created.Add(target);
        }
        return images.Count == 1 ? "Salvo " + System.IO.Path.GetFileName(last) : $"{images.Count} imagens reduzidas";
    }

    private static string? CopyPaths(string[] files, Job job)
    {
        var text = string.Join(Environment.NewLine, files);
        Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(text));
        return files.Length == 1 ? "Caminho copiado" : $"{files.Length} caminhos copiados";
    }

    private static string? AddToTray(string[] files, Job job)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (MainWindow.Current?.Settings is not { } settings) return;
            var items = settings.IslandTray ??= new();
            foreach (var f in files)
                if (!items.Contains(f, StringComparer.OrdinalIgnoreCase)) items.Add(f);
            settings.Save();
        });
        return files.Length == 1 ? "Guardado na bandeja da ilha" : $"{files.Length} itens na bandeja da ilha";
    }

    /// <summary>
    /// Remove o fundo: descobre a cor do fundo pelas bordas da imagem, "pinta" a partir das bordas
    /// tudo que for parecido com ela e deixa transparente, suavizando o contorno.
    /// Funciona melhor com fundo liso (branco, verde, uma cor só).
    /// </summary>
    private static string? RemoveBackground(string[] files, Job job)
    {
        var images = Images(files).ToList();
        if (images.Count == 0) return "Solte uma imagem para remover o fundo.";
        string? last = null;
        var busy = false;
        foreach (var img in images)
        {
            var target = Unique(System.IO.Path.Combine(job.Out(img), System.IO.Path.GetFileNameWithoutExtension(img) + " (sem fundo).png"));
            RunSta(() =>
            {
                var source = new FormatConvertedBitmap(Load(img), PixelFormats.Bgra32, null, 0);
                var w = source.PixelWidth;
                var h = source.PixelHeight;
                var px = new byte[w * h * 4];
                source.CopyPixels(px, w * 4, 0);
                busy |= BackgroundRemover.Apply(px, w, h);
                var result = BitmapSource.Create(w, h, source.DpiX, source.DpiY, PixelFormats.Bgra32, null, px, w * 4);
                result.Freeze();
                Save(result, target, "png");
            });
            last = target;
            job.Created.Add(target);
        }
        var msg = images.Count == 1 ? "Salvo " + System.IO.Path.GetFileName(last) : $"{images.Count} imagens sem fundo";
        return busy ? msg + ". O fundo tinha muitas cores, então confira o resultado." : msg;
    }

    /// <summary>Imagens WPF precisam de uma thread STA.</summary>
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new System.Threading.Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw error;
    }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
