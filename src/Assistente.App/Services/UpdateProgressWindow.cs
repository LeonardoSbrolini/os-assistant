using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Assistente.App.Tools;

namespace Assistente.App.Services;

/// <summary>
/// Janelinha que aparece quando você busca atualizações: procurando → baixando (barra, %,
/// tamanho, velocidade e tempo restante) → reiniciando. Também mostra "já está atualizado" e erros.
/// </summary>
internal sealed class UpdateProgressWindow : Window
{
    private readonly TextBlock _icon;
    private readonly TextBlock _title;
    private readonly TextBlock _subtitle;
    private readonly Grid _barHost;
    private readonly Border _fill;
    private readonly Border _shimmer;
    private readonly TextBlock _percent;
    private readonly TextBlock _sizeText;
    private readonly TextBlock _speedText;
    private readonly TextBlock _remainingText;
    private readonly Grid _details;
    private readonly Button _action;
    private readonly ScaleTransform _fillScale = new(0, 1);
    private Action? _onAction;
    private DispatcherTimer? _autoClose;

    // Velocidade média suavizada (bytes/s).
    private double _speed;
    private long _lastBytes;
    private DateTime _lastTime = DateTime.MinValue;
    private DateTime _lastUiUpdate = DateTime.MinValue;

    public bool Cancelled { get; private set; }

    /// <summary>Chamado quando a pessoa cancela o download.</summary>
    public event Action? CancelRequested;

    private static readonly Brush Muted = Freeze(new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA2)));

    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }

    public UpdateProgressWindow()
    {
        Title = "Atualização do Assistente";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        Foreground = Brushes.White;
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.ico"));

        _icon = new TextBlock
        {
            Text = "",
            FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
            FontSize = 22,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var iconTile = new Border
        {
            Width = 46,
            Height = 46,
            CornerRadius = new CornerRadius(14),
            Child = _icon,
            Margin = new Thickness(0, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        iconTile.SetResourceReference(Border.BackgroundProperty, "AccentBrush");

        _title = new TextBlock { FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        _subtitle = new TextBlock { FontSize = 12.5, Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
        var heading = new StackPanel();
        heading.Children.Add(_title);
        heading.Children.Add(_subtitle);
        var top = new DockPanel();
        DockPanel.SetDock(iconTile, System.Windows.Controls.Dock.Left);
        top.Children.Add(iconTile);
        top.Children.Add(heading);

        // Barra de progresso própria: trilho, preenchimento com degradê e um brilho que corre.
        _fill = new Border
        {
            CornerRadius = new CornerRadius(5),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            RenderTransformOrigin = new Point(0, 0.5),
            RenderTransform = _fillScale,
        };
        _fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        _shimmer = new Border
        {
            Width = 90,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(0, 255, 255, 255), 0),
                new GradientStop(Color.FromArgb(0x70, 255, 255, 255), 0.5),
                new GradientStop(Color.FromArgb(0, 255, 255, 255), 1),
            }, 0),
            RenderTransform = new TranslateTransform(-90, 0),
            IsHitTestVisible = false,
        };
        _barHost = new Grid { Height = 10, Margin = new Thickness(0, 18, 0, 0), ClipToBounds = true };
        _barHost.Children.Add(new Border { CornerRadius = new CornerRadius(5), Background = Freeze(new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF))) });
        _barHost.Children.Add(_fill);
        _barHost.Children.Add(_shimmer);
        _barHost.SizeChanged += (_, _) =>
        {
            _barHost.Clip = new RectangleGeometry(new Rect(0, 0, _barHost.ActualWidth, _barHost.ActualHeight), 5, 5);
            StartShimmer();
        };

        _percent = new TextBlock { FontSize = 26, FontWeight = FontWeights.SemiBold };
        _sizeText = new TextBlock { Foreground = Muted, Margin = new Thickness(0, 2, 0, 0) };
        _speedText = new TextBlock { TextAlignment = TextAlignment.Right, FontWeight = FontWeights.Medium };
        _remainingText = new TextBlock { TextAlignment = TextAlignment.Right, Foreground = Muted, Margin = new Thickness(0, 2, 0, 0) };
        var leftInfo = new StackPanel();
        leftInfo.Children.Add(_percent);
        leftInfo.Children.Add(_sizeText);
        var rightInfo = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        rightInfo.Children.Add(_speedText);
        rightInfo.Children.Add(_remainingText);
        _details = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        _details.ColumnDefinitions.Add(new ColumnDefinition());
        _details.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(rightInfo, 1);
        _details.Children.Add(leftInfo);
        _details.Children.Add(rightInfo);

        _action = Ui.Btn("Cancelar", () => _onAction?.Invoke());
        _action.HorizontalAlignment = HorizontalAlignment.Right;
        _action.Margin = new Thickness(0, 18, 0, 0);

        var body = new StackPanel();
        body.Children.Add(top);
        body.Children.Add(_barHost);
        body.Children.Add(_details);
        body.Children.Add(_action);

        Content = Ui.LiftShadow(new Border
        {
            CornerRadius = new CornerRadius(24),
            Background = Freeze(new SolidColorBrush(Color.FromRgb(0x0F, 0x0F, 0x12))),
            BorderBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF))),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(24, 22, 24, 20),
            Margin = new Thickness(16),
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 6, Opacity = 0.55, Direction = 270 },
            Child = body,
        });

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not Button) DragMove();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) _onAction?.Invoke();
        };
        Loaded += (_, _) =>
        {
            PlaceOnMouseScreen();
            var scale = new ScaleTransform(0.94, 0.94);
            var root = (FrameworkElement)Content;
            root.RenderTransformOrigin = new Point(0.5, 0.5);
            root.RenderTransform = scale;
            root.BeginAnimation(OpacityProperty, Anim.FromTo(0, 1, 200));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.FromTo(0.94, 1, 260, Anim.Back));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.FromTo(0.94, 1, 260, Anim.Back));
        };
        SizeChanged += (_, e) =>
        {
            // Mantém centralizada quando a altura muda entre as etapas.
            if (e.HeightChanged && IsLoaded) Top -= (e.NewSize.Height - e.PreviousSize.Height) / 2;
        };

        Searching();
    }

    /// <summary>Centraliza na tela onde está o mouse.</summary>
    private void PlaceOnMouseScreen()
    {
        var area = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
        var dpi = MainWindow.Current is { } main ? VisualTreeHelper.GetDpi(main) : VisualTreeHelper.GetDpi(this);
        Left = area.Left / dpi.DpiScaleX + (area.Width / dpi.DpiScaleX - ActualWidth) / 2;
        Top = area.Top / dpi.DpiScaleY + (area.Height / dpi.DpiScaleY - ActualHeight) / 2;
    }

    private void StartShimmer()
    {
        if (_shimmer.RenderTransform is not TranslateTransform t || _barHost.ActualWidth <= 0) return;
        var anim = new System.Windows.Media.Animation.DoubleAnimation(-90, _barHost.ActualWidth + 90, new Duration(TimeSpan.FromSeconds(1.4)))
        {
            RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
            EasingFunction = Anim.EaseInOut,
        };
        t.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    private void SetBar(double fraction, bool animate = true)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        if (animate) _fillScale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.To(fraction, 180));
        else
        {
            _fillScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _fillScale.ScaleX = fraction;
        }
    }

    private void SetAction(string text, Action action, bool primary = false)
    {
        _action.Content = text;
        _action.Style = primary ? (Style)Application.Current.Resources["PrimaryButton"] : null;
        _onAction = action;
    }

    // ---------- Etapas ----------

    public void Searching()
    {
        _icon.Text = ""; // lupa
        _title.Text = "Procurando atualizações…";
        _subtitle.Text = $"Você está na versão {UpdateService.CurrentVersionText}.";
        _barHost.Visibility = Visibility.Visible;
        _shimmer.Visibility = Visibility.Visible;
        _details.Visibility = Visibility.Collapsed;
        SetBar(0, animate: false);
        SetAction("Fechar", Close);
    }

    public void Downloading(string version, long total)
    {
        _icon.Text = ""; // download
        _title.Text = $"Baixando a versão {version}";
        _subtitle.Text = "O Assistente reinicia sozinho quando terminar.";
        _details.Visibility = Visibility.Visible;
        _shimmer.Visibility = Visibility.Visible;
        _percent.Text = "0%";
        _sizeText.Text = total > 0 ? $"0 MB de {Mb(total)}" : "";
        _speedText.Text = "";
        _remainingText.Text = "Calculando o tempo…";
        _lastTime = DateTime.Now;
        _lastBytes = 0;
        _speed = 0;
        SetAction("Cancelar", () =>
        {
            Cancelled = true;
            CancelRequested?.Invoke();
        });
    }

    /// <summary>Atualiza a barra (pode ser chamado a cada pedaço baixado: a tela só muda ~8 vezes por segundo).</summary>
    public void Progress(long done, long total)
    {
        var now = DateTime.Now;
        var dt = (now - _lastTime).TotalSeconds;
        if (dt >= 0.25)
        {
            var instant = (done - _lastBytes) / dt;
            _speed = _speed <= 0 ? instant : _speed * 0.7 + instant * 0.3;
            _lastBytes = done;
            _lastTime = now;
        }
        if ((now - _lastUiUpdate).TotalMilliseconds < 120 && done < total) return;
        _lastUiUpdate = now;

        if (total > 0)
        {
            var fraction = (double)done / total;
            SetBar(fraction);
            _percent.Text = $"{Math.Floor(fraction * 100):0}%";
            _sizeText.Text = $"{Mb(done)} de {Mb(total)}";
            if (_speed > 0)
            {
                var seconds = (total - done) / _speed;
                _remainingText.Text = $"Faltam {Duration(seconds)}";
            }
        }
        else
        {
            _percent.Text = Mb(done);
            _sizeText.Text = "baixados";
        }
        if (_speed > 0) _speedText.Text = $"{Mb((long)_speed)}/s";
    }

    public void Installing(string version)
    {
        _icon.Text = ""; // atualizar
        _title.Text = $"Versão {version} baixada!";
        _subtitle.Text = "Instalando e reiniciando o Assistente…";
        SetBar(1);
        _percent.Text = "100%";
        _remainingText.Text = "Pronto";
        _speedText.Text = "";
        _shimmer.Visibility = Visibility.Collapsed;
        _action.Visibility = Visibility.Collapsed;
    }

    public void UpToDate(string message)
    {
        _icon.Text = ""; // certo
        _title.Text = "Tudo em dia!";
        _subtitle.Text = message;
        _barHost.Visibility = Visibility.Collapsed;
        _details.Visibility = Visibility.Collapsed;
        _action.Visibility = Visibility.Visible;
        SetAction("OK", Close, primary: true);
        CloseAfter(TimeSpan.FromSeconds(5));
    }

    public void Failed(string message)
    {
        _icon.Text = ""; // aviso
        _title.Text = "Não deu para atualizar";
        _subtitle.Text = message;
        _barHost.Visibility = Visibility.Collapsed;
        _details.Visibility = Visibility.Collapsed;
        _action.Visibility = Visibility.Visible;
        SetAction("Fechar", Close);
    }

    private void CloseAfter(TimeSpan delay)
    {
        _autoClose?.Stop();
        _autoClose = new DispatcherTimer { Interval = delay };
        _autoClose.Tick += (_, _) =>
        {
            _autoClose.Stop();
            Close();
        };
        _autoClose.Start();
    }

    private static string Mb(long bytes) =>
        bytes >= 1024 * 1024
            ? (bytes / 1024d / 1024d).ToString("0.0", Ui.PtBr) + " MB"
            : (bytes / 1024d).ToString("0", Ui.PtBr) + " KB";

    private static string Duration(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return "…";
        if (seconds < 1) return "menos de 1 s";
        if (seconds < 60) return $"{Math.Ceiling(seconds):0} s";
        var t = TimeSpan.FromSeconds(Math.Ceiling(seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes} min" : $"{t.Minutes} min {t.Seconds:00} s";
    }
}
