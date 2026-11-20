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
    private const double MinPanelWidth = 520;
    private const double MinPanelHeight = 360;

    /// <summary>Enquanto maior que zero, o painel não recolhe (ex.: diálogo de confirmação aberto).</summary>
    public static int SuppressCollapse { get; set; }

    /// <summary>Janela principal (para serviços que precisam abrir o painel, ex.: timer).</summary>
    public static MainWindow? Current { get; private set; }

    private readonly AppSettings _settings;
    private readonly DispatcherTimer _collapseTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly Dictionary<ToolGroup, ToolGroupView> _groupViews = new();
    private bool _expanded;
    private bool _pinned;

    /// <summary>True quando o painel está aberto (o atualizador espera ele fechar para reiniciar).</summary>
    public bool IsExpanded => _expanded;

    public AppSettings Settings => _settings;

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

        // De tempos em tempos o avatar dá uma "balançadinha" para parecer vivo.
        _avatarIdle.Tick += (_, _) => WiggleAvatar();
        _avatarIdle.Start();

        BuildToolList();
        ApplySettings(); // aplica tema e já posiciona a alça antes de a janela aparecer
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

        var duration = show ? 260 : 150;
        DoubleAnimation Anim(double from, double to) => Tools.Anim.FromTo(from, to, duration);

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
        CollapsedHandle.BeginAnimation(OpacityProperty, Tools.Anim.FromTo(0, 1, 250));
    }


    // ---------- Configurações ----------

    /// <summary>Aplica tema, tamanho, posição e comportamento a partir das configurações.</summary>
    public void ApplySettings()
    {
        Tools.Anim.Enabled = _settings.Animations;
        ThemeService.Apply(_settings);
        _collapseTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.CloseDelayMs, 150, 3000));
        ApplyEdge();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => SettingsWindow.Open();

    private void SelectTool(string toolName) => SelectGroup(ToolRegistry.GroupOf(toolName), toolName);

    // ---------- Tempo na alça ----------

    /// <summary>
    /// Mostra um texto (ex.: tempo do timer) na alça recolhida, no lugar da setinha.
    /// null volta ao ícone normal.
    /// </summary>
    public void SetHandleStatus(string? glyph, string? text)
    {
        _handleGlyph = glyph;
        HandleText.Text = text ?? "";
        var hasText = !string.IsNullOrEmpty(text);
        if (hasText != _handleHasText)
        {
            _handleHasText = hasText;
            UpdateHandle();
        }
        else if (hasText && !_hasAvatar)
        {
            HandleIcon.Text = glyph ?? DefaultHandleGlyph;
        }
    }

    private readonly DispatcherTimer _avatarIdle = new() { Interval = TimeSpan.FromSeconds(9) };

    private void WiggleAvatar()
    {
        if (!_hasAvatar || _expanded || !Tools.Anim.Enabled) return;
        var wiggle = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(900) };
        foreach (var (ms, angle) in new[] { (0, 0.0), (150, -12.0), (300, 10.0), (450, -7.0), (600, 4.0), (900, 0.0) })
            wiggle.KeyFrames.Add(new EasingDoubleKeyFrame(angle, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
        AvatarRotate.BeginAnimation(RotateTransform.AngleProperty, wiggle);
    }

    private bool _handleHasText;
    private string? _handleGlyph;
    private bool _hasAvatar;

    /// <summary>Atualiza a aparência da alça: barra com setinha, avatar e/ou tempo do timer.</summary>
    private void UpdateHandle()
    {
        var avatar = Services.Avatars.Find(_settings.Avatar);
        _hasAvatar = avatar is not null;

        HandleAvatar.Source = avatar?.Image;
        HandleAvatar.Visibility = _hasAvatar ? Visibility.Visible : Visibility.Collapsed;
        HandleAvatar.Width = HandleAvatar.Height = _handleHasText ? 26 : 44;

        HandleText.Visibility = _handleHasText ? Visibility.Visible : Visibility.Collapsed;
        HandleIcon.Visibility = _hasAvatar ? Visibility.Collapsed : Visibility.Visible;
        HandleIcon.FontSize = _handleHasText ? 12 : 10;
        HandleIcon.Text = _handleHasText && _handleGlyph is not null ? _handleGlyph : DefaultHandleGlyph;

        if (_hasAvatar)
        {
            // Avatar: alça vira um círculo (ou pílula com o tempo) com borda na cor de destaque.
            CollapsedHandle.Padding = new Thickness(_handleHasText ? 4 : 3);
            CollapsedHandle.CornerRadius = new CornerRadius(_handleHasText ? 17 : 25);
        }
        else
        {
            CollapsedHandle.Padding = new Thickness(0);
            CollapsedHandle.CornerRadius = _settings.Edge switch
            {
                DockEdge.Top => new CornerRadius(0, 0, 8, 8),
                DockEdge.Left => new CornerRadius(0, 8, 8, 0),
                _ => new CornerRadius(8, 0, 0, 8),
            };
        }
        Reposition();
    }

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
                Sidebar.CornerRadius = new CornerRadius(0, 0, 0, 12);
                CollapsedHandle.CornerRadius = new CornerRadius(0, 0, 8, 8);
                ExpandedPanel.CornerRadius = new CornerRadius(0, 0, 12, 12);
                ExpandedPanel.BorderThickness = new Thickness(1, 0, 1, 1);
                break;
            case DockEdge.Left:
                Sidebar.CornerRadius = new CornerRadius(0);
                CollapsedHandle.CornerRadius = new CornerRadius(0, 8, 8, 0);
                ExpandedPanel.CornerRadius = new CornerRadius(0, 12, 12, 0);
                ExpandedPanel.BorderThickness = new Thickness(0, 1, 1, 1);
                break;
            default:
                Sidebar.CornerRadius = new CornerRadius(12, 0, 0, 12);
                CollapsedHandle.CornerRadius = new CornerRadius(8, 0, 0, 8);
                ExpandedPanel.CornerRadius = new CornerRadius(12, 0, 0, 12);
                ExpandedPanel.BorderThickness = new Thickness(1, 1, 0, 1);
                break;
        }
        UpdateHandle();
    }

    private void Reposition()
    {
        var area = SystemParameters.WorkArea;
        var top = _settings.Edge == DockEdge.Top;
        var position = Math.Clamp(_settings.Position, 0, 1);

        double width, height;
        if (_expanded)
        {
            // Tamanho definido nas configurações (padrão 50% da tela).
            var size = Math.Clamp(_settings.PanelSize, 0.3, 0.5);
            width = Math.Min(area.Width, Math.Max(MinPanelWidth, area.Width * size));
            height = Math.Min(area.Height, Math.Max(MinPanelHeight, area.Height * size));
        }
        else
        {
            if (_hasAvatar)
            {
                // Círculo do avatar (ou pílula avatar + tempo).
                width = _handleHasText ? 112 : 50;
                height = _handleHasText ? 34 : 50;
            }
            else if (_handleHasText)
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
        SelectTool(toolName);
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
        foreach (var group in ToolRegistry.Groups)
        {
            var item = new ListBoxItem { Tag = group, Content = GroupLabel(group), ToolTip = string.Join(", ", group.Tools.Select(t => t.Name)) };
            ToolList.Items.Add(item);
        }

        // Abre o grupo da última ferramenta usada (ou o primeiro).
        var lastGroup = _settings.LastTool is { } last ? ToolRegistry.GroupOf(last) : null;
        SelectGroup(lastGroup ?? ToolRegistry.Groups.FirstOrDefault(), _settings.LastTool);
    }

    private StackPanel GroupLabel(ToolGroup group)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = group.Glyph,
            FontFamily = (FontFamily)FindResource("IconFont"),
            FontSize = 16,
            Width = 28,
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(new TextBlock { Text = group.Name, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }

    private void SelectGroup(ToolGroup? group, string? toolName = null)
    {
        if (group is null) return;
        var item = ToolList.Items.OfType<ListBoxItem>().FirstOrDefault(i => ReferenceEquals(i.Tag, group));
        if (item is null) return;

        _pendingTool = toolName;
        if (ToolList.SelectedItem == item) ShowGroup(group);
        else ToolList.SelectedItem = item; // dispara ToolList_SelectionChanged
        ToolList.ScrollIntoView(item);
    }

    private string? _pendingTool;

    private void ToolList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ToolList.SelectedItem is ListBoxItem { Tag: ToolGroup group }) ShowGroup(group);
    }

    private void ShowGroup(ToolGroup group)
    {
        if (!_groupViews.TryGetValue(group, out var view))
        {
            view = new ToolGroupView(group);
            view.ToolChanged += tool =>
            {
                if (_settings.LastTool == tool.Name) return;
                _settings.LastTool = tool.Name;
                _settings.Save();
            };
            _groupViews[group] = view;
        }

        ToolHost.Content = view;
        if (_pendingTool is { } name && group.Tools.Any(t => t.Name == name)) view.Show(name);
        else view.EnsureShown();
        _pendingTool = null;
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
