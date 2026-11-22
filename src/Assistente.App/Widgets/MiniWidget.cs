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
    private const double Gap = 2;

    private readonly TranslateTransform _slide = new();
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
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(5),
            Margin = new Thickness(ShadowMargin),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.35, Color = Colors.Black },
            RenderTransform = _slide,
        };
        Pill.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        Pill.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        Content = Pill;

        SourceInitialized += (_, _) => MakeToolWindow();
        SizeChanged += (_, _) => Place();
    }

    public WidgetSide Side { get; }

    protected Border Pill { get; }

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

        if (main.Settings.Edge == DockEdge.Top)
        {
            top = area.Top + Math.Max(0, (main.Height - h) / 2);
            left = Side == WidgetSide.Before ? main.Left - Gap - w : main.Left + main.Width + Gap;
        }
        else
        {
            left = main.Settings.Edge == DockEdge.Left ? area.Left : area.Right - w;
            top = Side == WidgetSide.Before ? main.Top - Gap - h : main.Top + main.Height + Gap;
        }

        Left = Math.Clamp(left, area.Left, Math.Max(area.Left, area.Right - w));
        Top = Math.Clamp(top, area.Top, Math.Max(area.Top, area.Bottom - h));
    }

    // ---------- Peças reaproveitáveis ----------

    protected static Button IconButton(string glyph, string tip, Action onClick, bool primary = false, double size = 28)
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
            ToolTip = tip,
            Focusable = false,
            Style = (Style)Application.Current.Resources[primary ? "PrimaryButton" : "RoundButton"],
            Tag = new CornerRadius(size / 2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        button.Click += (_, _) => onClick();
        return button;
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
