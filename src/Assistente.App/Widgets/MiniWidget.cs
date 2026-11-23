using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Assistente.App.Services;
using Assistente.App.Tools;

namespace Assistente.App.Widgets;

/// <summary>Em que lado do avatar o widget fica (no topo: esquerda/direita; nas laterais: acima/abaixo).</summary>
public enum WidgetSide { Before, After }

/// <summary>
/// Janelinha flutuante em forma de pílula que fica ao lado do avatar com o painel fechado.
/// Não rouba o foco (clicar nela não tira o foco do navegador/jogo).
/// </summary>
public abstract class MiniWidget : Window
{
    /// <summary>Espaço transparente em volta da pílula (para a sombra).</summary>
    protected const double ShadowMargin = 6;
    private const double Gap = 4;

    /// <summary>Altura da "aba" (igual à barra de abas de um navegador) — a mesma do avatar.</summary>
    public const double TabHeight = 34;

    private readonly TranslateTransform _slide = new();
    private readonly ScaleTransform _hover = new();
    private readonly Border _body;
    private readonly Border _progressTrack;
    private readonly Border _progress;
    private bool _shown;

    protected MiniWidget(WidgetSide side)
    {
        Side = side;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        UseLayoutRounding = true;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 12;
        SetResourceReference(ForegroundProperty, "TextBrush");

        Pill = new Border
        {
            CornerRadius = new CornerRadius(11),
            Height = TabHeight,
            Margin = new Thickness(ShadowMargin),
            BorderThickness = new Thickness(1),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new TransformGroup { Children = { _hover, _slide } },
            // Borda com brilho em cima (efeito vidro).
            BorderBrush = new LinearGradientBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), 90),
        };
        Pill.SetResourceReference(Border.BackgroundProperty, "PanelBrush");

        // Camadas: fundo (capa/cor), brilho, conteúdo e linha de progresso.
        var layers = new Grid();
        layers.SizeChanged += (_, e) =>
            layers.Clip = new RectangleGeometry(new Rect(e.NewSize), 10, 10);
        Backdrop = new Border();
        layers.Children.Add(Backdrop);
        layers.Children.Add(new Border
        {
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF), 0),
                    new(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.55),
                }, 90),
            IsHitTestVisible = false,
        });
        _body = new Border { Padding = new Thickness(6, 0, 6, 0) };
        layers.Children.Add(_body);
        _progressTrack = new Border
        {
            Height = 2,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        _progress = new Border { Height = 2, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(0, 1, 1, 0) };
        _progress.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        _progressTrack.Child = _progress;
        layers.Children.Add(_progressTrack);
        Pill.Child = layers;
        Content = Pill;

        // Cresce um pouquinho com o mouse em cima.
        Pill.MouseEnter += (_, _) => Hover(1.04);
        Pill.MouseLeave += (_, _) => Hover(1);

        SourceInitialized += (_, _) => MakeToolWindow();
        SizeChanged += (_, _) => Place();
    }

    public WidgetSide Side { get; }

    protected Border Pill { get; }

    /// <summary>Camada de fundo (capa desfocada, cor do estado...).</summary>
    protected Border Backdrop { get; }

    /// <summary>Conteúdo do widget.</summary>
    protected UIElement Body
    {
        set => _body.Child = value;
    }

    /// <summary>Linha fina de progresso embaixo (null esconde).</summary>
    protected void SetProgress(double? fraction, Brush? brush = null)
    {
        if (fraction is not { } f)
        {
            _progressTrack.Visibility = Visibility.Collapsed;
            return;
        }
        _progressTrack.Visibility = Visibility.Visible;
        if (brush is not null) _progress.Background = brush;
        var width = Math.Max(0, Pill.ActualWidth - 2) * Math.Clamp(f, 0, 1);
        _progress.BeginAnimation(WidthProperty, Anim.To(width, 400));
    }

    private void Hover(double scale)
    {
        _hover.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.To(scale, 160));
        _hover.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.To(scale, 160));
    }

    /// <summary>True quando o widget tem algo para mostrar.</summary>
    public abstract bool HasSomething { get; }

    /// <summary>Mostra/esconde com animação.</summary>
    public void SetShown(bool show)
    {
        if (show == _shown) return;
        _shown = show;
        var edgeTop = MainWindow.Current?.Settings.Edge == DockEdge.Top;
        var offset = Side == WidgetSide.Before ? 10.0 : -10.0;

        if (show)
        {
            IsHitTestVisible = true;
            Opacity = 0;
            Show();
            Place();
            BeginAnimation(OpacityProperty, Anim.FromTo(0, 1, 220));
            var prop = edgeTop ? TranslateTransform.XProperty : TranslateTransform.YProperty;
            _slide.BeginAnimation(edgeTop ? TranslateTransform.YProperty : TranslateTransform.XProperty, null);
            _slide.BeginAnimation(prop, Anim.FromTo(offset, 0, 280, Anim.Back));
        }
        else
        {
            // Fecha as dicas abertas: se ficarem abertas com a janela escondida, o Windows
            // as joga no canto da tela.
            IsHitTestVisible = false;
            CloseTips(this);
            var fade = Anim.To(0, 150);
            fade.Completed += (_, _) =>
            {
                if (!_shown) Hide();
            };
            BeginAnimation(OpacityProperty, fade);
        }
    }

    /// <summary>Coloca o widget colado ao avatar, do lado certo.</summary>
    public void Place()
    {
        var main = MainWindow.Current;
        if (main is null || ActualWidth <= 0) return;
        var area = SystemParameters.WorkArea;
        var w = ActualWidth;
        var h = ActualHeight;
        double left, top;

        var handle = main.HandleVisualRect;
        if (main.Settings.Edge == DockEdge.Top)
        {
            top = handle.Top + handle.Height / 2 - h / 2;
            left = Side == WidgetSide.Before ? handle.Left - Gap - w : handle.Right + Gap;
        }
        else
        {
            left = main.Settings.Edge == DockEdge.Left ? handle.Left - ShadowMargin : handle.Right - w + ShadowMargin;
            top = Side == WidgetSide.Before ? handle.Top - Gap - h : handle.Bottom + Gap;
        }

        Left = Math.Clamp(left, area.Left, Math.Max(area.Left, area.Right - w));
        Top = Math.Clamp(top, area.Top, Math.Max(area.Top, area.Bottom - h));
    }

    // ---------- Peças reaproveitáveis ----------

    protected static Button IconButton(string glyph, string tip, Action onClick, bool primary = false, double size = 24)
    {
        var button = new Button
        {
            Content = glyph,
            FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
            FontSize = size / 2.5,
            Width = size,
            Height = size,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 0, 0, 0),
            ToolTip = new ToolTip { Content = tip },
            Focusable = false,
            Style = (Style)Application.Current.Resources[primary ? "PrimaryButton" : "IconButton"],
            Tag = new CornerRadius(size / 2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (!primary) button.SetResourceReference(ForegroundProperty, "TextBrush");
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>Troca o texto da dica (sem criar dicas soltas).</summary>
    protected static void Tip(FrameworkElement element, string text)
    {
        if (element.ToolTip is ToolTip tip) tip.Content = text;
        else element.ToolTip = new ToolTip { Content = text };
    }

    private static void CloseTips(DependencyObject root)
    {
        if (root is FrameworkElement { ToolTip: ToolTip tip }) tip.IsOpen = false;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            CloseTips(VisualTreeHelper.GetChild(root, i));
    }

    protected static void Pulse(UIElement element, bool on)
    {
        if (!on)
        {
            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = 1;
            return;
        }
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(500))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        });
    }

    // ---------- Win32 ----------

    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExAppWindow = 0x00040000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private void MakeToolWindow()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(hwnd, GwlExStyle);
        SetWindowLong(hwnd, GwlExStyle, (style | WsExToolWindow | WsExNoActivate) & ~WsExAppWindow);
    }
}
