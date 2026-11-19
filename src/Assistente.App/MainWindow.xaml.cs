using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Assistente.App.Services;
using Assistente.App.Tools;

namespace Assistente.App;

/// <summary>
/// Janela flutuante: fica recolhida como uma alça fina na borda da tela e
/// expande quando o mouse passa por cima.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Espessura e comprimento da alça recolhida.</summary>
    private const double HandleThickness = 10;
    private const double HandleLength = 120;
    private const double PanelWidth = 640;
    private const double PanelHeight = 680;

    /// <summary>O painel nunca passa de 40% da largura nem da altura da tela.</summary>
    private const double MaxScreenFraction = 0.4;

    private static readonly Duration AnimationDuration = new(TimeSpan.FromMilliseconds(180));
    private static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };

    /// <summary>Enquanto maior que zero, o painel não recolhe (ex.: diálogo de confirmação aberto).</summary>
    public static int SuppressCollapse { get; set; }

    /// <summary>Janela principal (para serviços que precisam abrir o painel, ex.: timer).</summary>
    public static MainWindow? Current { get; private set; }

    private readonly AppSettings _settings;
    private readonly DispatcherTimer _collapseTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly Dictionary<ITool, FrameworkElement> _views = new();
    private bool _expanded;
    private bool _pinned;

    /// <summary>True quando o painel está aberto (o atualizador espera ele fechar para reiniciar).</summary>
    public bool IsExpanded => _expanded;

    public MainWindow(AppSettings settings)
    {
        _settings = settings;
        Current = this;
        InitializeComponent();

        _collapseTimer.Tick += (_, _) => TryCollapse();
        SourceInitialized += (_, _) =>
        {
            HideFromAltTab();
            ClipboardHistory.Instance.Attach(this);
        };
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;

        BuildToolList();
        ApplyEdge(); // já posiciona a alça antes de a janela aparecer
    }

    // ---------- Expandir / recolher ----------

    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        _collapseTimer.Stop();
        if (!_expanded || _collapsing) Expand();
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_expanded && !_pinned) _collapseTimer.Start();
    }

    private void TryCollapse()
    {
        _collapseTimer.Stop();
        // Não recolhe se o mouse voltou, se algo capturou o mouse (ex.: ComboBox aberto)
        // ou se há um diálogo aberto.
        if (_pinned || IsMouseOver || Mouse.Captured is not null || SuppressCollapse > 0) return;
        Collapse();
    }

    private void Expand()
    {
        _expanded = true;
        _collapsing = false;
        CollapsedHandle.Visibility = Visibility.Collapsed;
        ExpandedPanel.Visibility = Visibility.Visible;
        Reposition();
        AnimatePanel(show: true, onDone: null);
    }

    private void Collapse()
    {
        if (!_expanded || _collapsing) return;
        _collapsing = true;
        AnimatePanel(show: false, onDone: () =>
        {
            // O mouse pode ter voltado durante a animação.
            if (!_collapsing) return;
            _collapsing = false;
            _expanded = false;
            ExpandedPanel.Visibility = Visibility.Collapsed;
            CollapsedHandle.Visibility = Visibility.Visible;
            Reposition();
            AnimateHandleIn();
        });
    }

    private bool _collapsing;

    // ---------- Animações ----------

    /// <summary>Painel "desliza" a partir da borda e aparece/some com fade.</summary>
    private void AnimatePanel(bool show, Action? onDone)
    {
        var (fromX, fromY) = _settings.Edge switch
        {
            DockEdge.Top => (0.0, -16.0),
            DockEdge.Left => (-16.0, 0.0),
            _ => (16.0, 0.0),
        };

        var translate = new TranslateTransform(show ? fromX : 0, show ? fromY : 0);
        var scale = new ScaleTransform(show ? 0.97 : 1, show ? 0.97 : 1);
        ExpandedPanel.RenderTransformOrigin = _settings.Edge switch
        {
            DockEdge.Top => new Point(0.5, 0),
            DockEdge.Left => new Point(0, 0.5),
            _ => new Point(1, 0.5),
        };
        ExpandedPanel.RenderTransform = new TransformGroup { Children = { scale, translate } };

        var duration = show ? AnimationDuration : new Duration(TimeSpan.FromMilliseconds(130));
        DoubleAnimation Anim(double from, double to) => new(from, to, duration) { EasingFunction = Ease };

        var fade = Anim(show ? 0 : 1, show ? 1 : 0);
        if (onDone is not null) fade.Completed += (_, _) => onDone();

        ExpandedPanel.BeginAnimation(OpacityProperty, fade);
        translate.BeginAnimation(TranslateTransform.XProperty, Anim(show ? fromX : 0, show ? 0 : fromX));
        translate.BeginAnimation(TranslateTransform.YProperty, Anim(show ? fromY : 0, show ? 0 : fromY));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(show ? 0.97 : 1, show ? 1 : 0.97));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(show ? 0.97 : 1, show ? 1 : 0.97));
    }

    private void AnimateHandleIn()
    {
        CollapsedHandle.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, AnimationDuration) { EasingFunction = Ease });
    }

    /// <summary>Troca de ferramenta: a tela nova sobe um pouco e aparece com fade.</summary>
    private static void AnimateToolIn(FrameworkElement view)
    {
        var translate = new TranslateTransform(0, 10);
        view.RenderTransform = translate;
        view.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, AnimationDuration) { EasingFunction = Ease });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, AnimationDuration) { EasingFunction = Ease });
    }

    // ---------- Tempo na alça ----------

    /// <summary>
    /// Mostra um texto (ex.: tempo do timer) na alça recolhida, no lugar da setinha.
    /// null volta ao ícone normal.
    /// </summary>
    public void SetHandleStatus(string? glyph, string? text)
    {
        var hasText = !string.IsNullOrEmpty(text);
        HandleText.Text = text ?? "";
        HandleText.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
        HandleIcon.FontSize = hasText ? 12 : 10;
        HandleIcon.Text = hasText && glyph is not null ? glyph : DefaultHandleGlyph;
        if (hasText != _handleHasText)
        {
            _handleHasText = hasText;
            Reposition();
        }
    }

    private bool _handleHasText;

    private string DefaultHandleGlyph => _settings.Edge == DockEdge.Top ? "\uE70D" : "\uE700";

    /// <summary>Abre o painel e mantém aberto (usado pelo ícone da bandeja).</summary>
    public void ExpandPinned()
    {
        SetPinned(true);
        Expand();
        Activate();
    }

    private void PinButton_Click(object sender, RoutedEventArgs e) => SetPinned(!_pinned);

    private void CollapseButton_Click(object sender, RoutedEventArgs e)
    {
        SetPinned(false);
        Collapse();
    }

    private void SetPinned(bool pinned)
    {
        _pinned = pinned;
        PinButton.Content = pinned ? "" : "";
        PinButton.ToolTip = pinned ? "Desafixar" : "Fixar aberto";
        PinButton.Foreground = pinned
            ? (Brush)FindResource("AccentBrush")
            : (Brush)FindResource("MutedBrush");
    }

    // ---------- Posicionamento ----------

    /// <summary>Ajusta cantos arredondados conforme a borda da tela.</summary>
    public void ApplyEdge()
    {
        switch (_settings.Edge)
        {
            case DockEdge.Top:
                CollapsedHandle.CornerRadius = new CornerRadius(0, 0, 8, 8);
                ExpandedPanel.CornerRadius = new CornerRadius(0, 0, 12, 12);
                ExpandedPanel.BorderThickness = new Thickness(1, 0, 1, 1);
                break;
            case DockEdge.Left:
                CollapsedHandle.CornerRadius = new CornerRadius(0, 8, 8, 0);
                ExpandedPanel.CornerRadius = new CornerRadius(0, 12, 12, 0);
                ExpandedPanel.BorderThickness = new Thickness(0, 1, 1, 1);
                break;
            default:
                CollapsedHandle.CornerRadius = new CornerRadius(8, 0, 0, 8);
                ExpandedPanel.CornerRadius = new CornerRadius(12, 0, 0, 12);
                ExpandedPanel.BorderThickness = new Thickness(1, 1, 0, 1);
                break;
        }
        if (!_handleHasText) HandleIcon.Text = DefaultHandleGlyph;
        Reposition();
    }

    private void Reposition()
    {
        var area = SystemParameters.WorkArea;
        var top = _settings.Edge == DockEdge.Top;
        var position = Math.Clamp(_settings.Position, 0, 1);

        double width, height;
        if (_expanded)
        {
            width = Math.Min(PanelWidth, area.Width * MaxScreenFraction);
            height = Math.Min(PanelHeight, area.Height * MaxScreenFraction);
        }
        else
        {
            if (_handleHasText)
            {
                // Alça maior para caber o tempo (ex.: "12:34").
                width = top ? HandleLength : 92;
                height = 26;
            }
            else
            {
                width = top ? HandleLength : HandleThickness;
                height = top ? HandleThickness : HandleLength;
            }
        }

        Width = width;
        Height = height;

        if (top)
        {
            // Encostado no topo; "Position" é a posição horizontal (0,5 = centro).
            var centerX = area.Left + area.Width * position;
            Left = Math.Clamp(centerX - width / 2, area.Left, area.Right - width);
            Top = area.Top;
        }
        else
        {
            var centerY = area.Top + area.Height * position;
            Top = Math.Clamp(centerY - height / 2, area.Top, area.Bottom - height);
            Left = _settings.Edge == DockEdge.Left ? area.Left : area.Right - width;
        }
    }

    /// <summary>Abre o painel fixado mostrando a ferramenta indicada.</summary>
    public void ShowTool(string toolName)
    {
        var item = ToolList.Items.OfType<ListBoxItem>()
            .FirstOrDefault(i => i.Tag is ITool t && t.Name == toolName);
        if (item is not null) ToolList.SelectedItem = item;
        ExpandPinned();
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.WorkArea))
            Dispatcher.BeginInvoke(new Action(Reposition));
    }

    // ---------- Ferramentas ----------

    private void BuildToolList()
    {
        foreach (var group in ToolRegistry.Tools.GroupBy(t => t.Category))
        {
            ToolList.Items.Add(new ListBoxItem
            {
                IsEnabled = false,
                Focusable = false,
                Padding = new Thickness(10, 10, 10, 2),
                Content = new TextBlock
                {
                    Text = group.Key.ToUpperInvariant(),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("MutedBrush"),
                },
            });

            foreach (var tool in group)
            {
                var item = new ListBoxItem { Tag = tool, Content = ToolLabel(tool) };
                ToolList.Items.Add(item);
            }
        }

        // Seleciona a última ferramenta usada (ou a primeira).
        var last = ToolList.Items.OfType<ListBoxItem>()
            .FirstOrDefault(i => i.Tag is ITool t && t.Name == _settings.LastTool);
        ToolList.SelectedItem = last ?? ToolList.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag is ITool);
    }

    private StackPanel ToolLabel(ITool tool)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = tool.Glyph,
            FontFamily = (FontFamily)FindResource("IconFont"),
            FontSize = 14,
            Width = 22,
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(new TextBlock { Text = tool.Name, VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }

    private void ToolList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ToolList.SelectedItem is not ListBoxItem { Tag: ITool tool }) return;

        if (!_views.TryGetValue(tool, out var view))
        {
            view = tool.CreateView();
            _views[tool] = view;
        }
        ToolHost.Content = view;
        AnimateToolIn(view);

        if (_settings.LastTool != tool.Name)
        {
            _settings.LastTool = tool.Name;
            _settings.Save();
        }
    }

    // ---------- Win32: esconder do Alt+Tab ----------

    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExAppWindow = 0x00040000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private void HideFromAltTab()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(hwnd, GwlExStyle);
        SetWindowLong(hwnd, GwlExStyle, (style | WsExToolWindow) & ~WsExAppWindow);
    }
}
