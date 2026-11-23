using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Assistente.App.Services;
using Assistente.App.Tools;

namespace Assistente.App.Widgets;

/// <summary>
/// Mini player à direita do avatar: fundo com as cores da capa, barrinhas de equalizador,
/// música numa linha, anterior / tocar-pausar / próxima e uma linha de progresso.
/// </summary>
public sealed class MusicWidget : MiniWidget
{
    /// <summary>Depois de tanto tempo pausado, o widget some sozinho.</summary>
    private static readonly TimeSpan HideAfterPause = TimeSpan.FromMinutes(10);

    private static MediaService Media => MediaService.Instance;

    // Capa redonda que gira como um disco enquanto toca.
    private readonly RotateTransform _spin = new();
    private readonly Border _cover = new()
    {
        Width = 26,
        Height = 26,
        CornerRadius = new CornerRadius(13),
        VerticalAlignment = VerticalAlignment.Center,
        BorderThickness = new Thickness(1.5),
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)),
        RenderTransformOrigin = new Point(0.5, 0.5),
    };
    private bool _spinning;
    private readonly TextBlock _title = new()
    {
        FontSize = 12.5,
        FontWeight = FontWeights.SemiBold,
        Foreground = Brushes.White,
        MaxWidth = 170,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _artist = new()
    {
        FontSize = 11.5,
        MaxWidth = 120,
        Foreground = Brushes.White,
        Opacity = 0.65,
        Margin = new Thickness(6, 0, 0, 0),
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly StackPanel _equalizer = new()
    {
        Orientation = Orientation.Horizontal,
        Height = 12,
        Margin = new Thickness(8, 0, 6, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly PillButton _playPause;
    private readonly StackPanel _info;
    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private ImageSource? _lastCover;
    private string _lastTitle = "";
    private bool _eqRunning;
    private Brush _tint;

    public MusicWidget() : base(WidgetSide.After)
    {
        _tint = (Brush)Application.Current.Resources["AccentBrush"];
        _cover.RenderTransform = _spin;
        _playPause = IconButton("", "Tocar / pausar", Media.TogglePlayPause, primary: true, size: 26);
        _playPause.Margin = new Thickness(2, 0, 2, 0);

        foreach (var _ in new[] { 0, 1, 2 })
        {
            var bar = new Border
            {
                Width = 3,
                Height = 12,
                CornerRadius = new CornerRadius(1.5),
                Margin = new Thickness(0, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Bottom,
                RenderTransformOrigin = new Point(0.5, 1),
                RenderTransform = new ScaleTransform(1, 0.35),
                Background = _tint,
            };
            _equalizer.Children.Add(bar);
        }

        var text = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        text.Children.Add(_title);
        text.Children.Add(_artist);

        // Clicar na capa ou no nome abre a aba Música.
        _info = new StackPanel { Orientation = Orientation.Horizontal, Cursor = Cursors.Hand, Background = Brushes.Transparent };
        _info.Children.Add(_cover);
        _info.Children.Add(_equalizer);
        _info.Children.Add(text);
        Tip(_info, "Abrir a aba Música");
        _info.MouseLeftButtonUp += (_, _) => MainWindow.Current?.ShowTool("Tocando agora");

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(_info);
        row.Children.Add(IconButton("", "Anterior", Media.Previous));
        row.Children.Add(_playPause);
        row.Children.Add(IconButton("", "Próxima", Media.Next));
        var close = IconButton("", "Fechar o player (volta quando tocar outra música)", Dismiss, size: 20);
        close.Margin = new Thickness(4, 0, 0, 0);
        close.Opacity = 0.6;
        row.Children.Add(close);
        Body = row;

        SetCover(null);
        _progressTimer.Tick += (_, _) => UpdateProgress();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _progressTimer.Start();
            else _progressTimer.Stop();
            UpdateEqualizer();
        };
        Media.Changed += Refresh;
        Refresh();
    }

    /// <summary>Música que o usuário fechou: o player só volta quando mudar de música.</summary>
    private string? _dismissed;

    private string CurrentKey => Media.AppName + "|" + Media.Title;

    public override bool HasSomething =>
        Media.HasSession && _dismissed != CurrentKey &&
        (Media.IsPlaying || DateTime.Now - Media.LastPlayingAt < HideAfterPause);

    private void Dismiss()
    {
        _dismissed = CurrentKey;
        WidgetManager.Refresh();
    }

    private void Refresh()
    {
        if (!Media.HasSession) return;
        _title.Text = Media.Title;
        _artist.Text = Media.Artist.Length > 0 ? Media.Artist : Media.AppName;
        Tip(_info, $"{Media.Title}\n{Media.Artist}\nTocando em {Media.AppName}\n(clique para abrir a aba Música)");
        _playPause.Glyph = Media.IsPlaying ? "" : "";
        if (!ReferenceEquals(_lastCover, Media.Cover))
        {
            _lastCover = Media.Cover;
            SetCover(Media.Cover);
        }
        if (_lastTitle != Media.Title && IsVisible) Anim.FadeUp(_title, 0, 4);
        _lastTitle = Media.Title;
        UpdateEqualizer();
        UpdateProgress();
    }

    private void UpdateProgress()
    {
        var (position, duration) = Media.GetTimeline();
        SetProgress(duration > TimeSpan.Zero ? position.TotalSeconds / duration.TotalSeconds : null, _tint);
    }

    /// <summary>Barrinhas pulando enquanto toca (param quando pausa ou o widget some).</summary>
    private void UpdateEqualizer()
    {
        var run = IsVisible && Media.IsPlaying && Anim.Enabled;
        if (run != _spinning)
        {
            _spinning = run;
            if (run)
                _spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(_spin.Angle, _spin.Angle + 360, TimeSpan.FromSeconds(8)) { RepeatBehavior = RepeatBehavior.Forever });
            else
            {
                var angle = _spin.Angle;
                _spin.BeginAnimation(RotateTransform.AngleProperty, null);
                _spin.Angle = angle % 360;
            }
        }
        if (run == _eqRunning) return;
        _eqRunning = run;
        var i = 0;
        foreach (Border bar in _equalizer.Children)
        {
            var scale = (ScaleTransform)bar.RenderTransform;
            if (!run)
            {
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.To(0.35, 200));
                continue;
            }
            var ms = new[] { 420, 300, 520 }[i++];
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(ms))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                BeginTime = TimeSpan.FromMilliseconds(i * 90),
            });
        }
    }

    private void SetCover(ImageSource? image)
    {
        if (image is null)
        {
            _cover.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            _cover.Child = new TextBlock
            {
                Text = "",
                FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
                FontSize = 11,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Backdrop.Background = null;
            Backdrop.Child = null;
            SetTint((Brush)Application.Current.Resources["AccentBrush"]);
            SetAccent(null);
            return;
        }

        _cover.Child = null;
        _cover.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        if (IsVisible) Anim.Pop(_cover);

        // Fundo com as cores da capa: a capa reduzida a poucos pixels e esticada fica "desfocada".
        if (image is BitmapSource bitmap && bitmap.PixelWidth > 0)
        {
            try
            {
                var tiny = new TransformedBitmap(bitmap, new ScaleTransform(8.0 / bitmap.PixelWidth, 8.0 / bitmap.PixelHeight));
                tiny.Freeze();
                var blur = new ImageBrush(tiny) { Stretch = Stretch.UniformToFill, Opacity = 0.9 };
                RenderOptions.SetBitmapScalingMode(blur, BitmapScalingMode.Linear);
                blur.Freeze();
                Backdrop.Background = blur;
                // Véu escuro por cima para o texto continuar legível.
                Backdrop.Child = new Border
                {
                    Background = new LinearGradientBrush(Color.FromArgb(0xA0, 0x0C, 0x0C, 0x10), Color.FromArgb(0x60, 0x0C, 0x0C, 0x10), 0),
                };
                var vivid = Vivid(Average(tiny));
                SetTint(new SolidColorBrush(vivid));
                SetAccent(vivid);
            }
            catch (Exception)
            {
                Backdrop.Background = null;
            }
        }
    }

    private void SetTint(Brush brush)
    {
        _tint = brush;
        foreach (Border bar in _equalizer.Children) bar.Background = brush;
        UpdateProgress();
    }

    private static Color Average(BitmapSource source)
    {
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var w = bgra.PixelWidth;
        var h = bgra.PixelHeight;
        var pixels = new byte[w * h * 4];
        bgra.CopyPixels(pixels, w * 4, 0);
        long r = 0, g = 0, b = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            b += pixels[i];
            g += pixels[i + 1];
            r += pixels[i + 2];
        }
        var n = Math.Max(1, w * h);
        return Color.FromRgb((byte)(r / n), (byte)(g / n), (byte)(b / n));
    }

    /// <summary>Deixa a cor média da capa viva e clara o bastante para aparecer no fundo escuro.</summary>
    private static Color Vivid(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        if (max - min < 0.08) return Color.FromRgb(0xE8, 0xE9, 0xED); // capa cinza: usa branco
        var boost = 0.85 / Math.Max(max, 0.01);
        double Sat(double v) => Math.Clamp(min + (v - min) * 1.4, 0, 1);
        return Color.FromRgb((byte)(Math.Clamp(Sat(r) * boost, 0, 1) * 255), (byte)(Math.Clamp(Sat(g) * boost, 0, 1) * 255), (byte)(Math.Clamp(Sat(b) * boost, 0, 1) * 255));
    }
}
