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

    /// <summary>Tamanho do círculo do avatar na alça e da imagem dentro dele.</summary>
    private const double AvatarCircle = Widgets.MiniWidget.TabHeight;
    private const double AvatarSize = 28;

    /// <summary>Espaço entre o avatar e a borda da tela.</summary>
    private const double EdgeGap = 6;

    /// <summary>A alça mudou de lugar/tamanho ou o painel abriu/fechou (os mini widgets se reposicionam).</summary>
    public event Action? HandleLayoutChanged;

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
        BuildChromeButtons();

        _collapseTimer.Tick += (_, _) => TryCollapse();
        SourceInitialized += (_, _) =>
        {
            HideFromAltTab();
            ClipboardHistory.Instance.Attach(this);
        };
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;

        // De tempos em tempos o avatar dá uma "balançadinha" para parecer vivo.

        CollapsedHandle.RenderTransform = _handleSlide;
        _peek.Tick += (_, _) => Peek();
        _peek.Start();

        BuildToolList();
        ApplySettings(); // aplica tema e já posiciona a alça antes de a janela aparecer
    }

    // ---------- Expandir / recolher ----------

    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        _collapseTimer.Stop();
        // Logo depois de fechar, ignora o mouse por um instante (evita o abre-e-fecha sem parar).
        if (DateTime.Now - _collapsedAt < TimeSpan.FromMilliseconds(350)) return;
        if (_handleHidden && !_expanded) return;
        if (!_expanded || _collapsing) Expand();
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_expanded && !_pinned) _collapseTimer.Start();
    }

    /// <summary>
    /// O mouse está dentro da área do painel? Usa a posição real do cursor (e não o
    /// IsMouseOver do WPF, que "pisca" durante as animações e causava o abre-e-fecha).
    /// </summary>
    private bool CursorInsideWindow()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var (cx, cy) = Docking.Native.CursorPosition();
        var rect = new Rect(Left * dpi.DpiScaleX, Top * dpi.DpiScaleY, Width * dpi.DpiScaleX, Height * dpi.DpiScaleY);
        rect.Inflate(4, 4);
        return rect.Contains(cx, cy);
    }

    private void TryCollapse()
    {
        _collapseTimer.Stop();
        // Não recolhe se o mouse voltou, se algo capturou o mouse (ex.: ComboBox aberto)
        // ou se há um diálogo aberto.
        if (_pinned || Mouse.Captured is not null || SuppressCollapse > 0) return;
        if (CursorInsideWindow())
        {
            _collapseTimer.Start(); // confere de novo daqui a pouco
            return;
        }
        Collapse();
    }

    private DateTime _collapsedAt = DateTime.MinValue;

    private void Expand()
    {
        _handleWantHideSince = DateTime.MaxValue;
        _expanded = true;
        _collapsing = false;
        CollapsedHandle.BeginAnimation(OpacityProperty, null);
        CollapsedHandle.Visibility = Visibility.Collapsed;
        ExpandedPanel.Visibility = Visibility.Visible;
        // Fundo quase invisível: a janela inteira (com as margens) conta como "dentro" para o mouse.
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
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
            _collapsedAt = DateTime.Now;
            ExpandedPanel.Visibility = Visibility.Collapsed;
            // O painel voltou a ser a pílula: a ilha reaparece exatamente onde ele encolheu.
            if (_settings.Edge == DockEdge.Top) MenuBar.MenuBarWindow.TakeBackFromPanel();
            Background = Brushes.Transparent;
            // A alça só aparece depois que a janela já encolheu para o tamanho dela
            // (antes aparecia um pedaço azul no canto do painel por um instante).
            CollapsedHandle.Opacity = 0;
            CollapsedHandle.Visibility = Visibility.Visible;
            Reposition();
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                if (_expanded) return;
                // Como o avatar fica sempre escondido, ele volta ao topo sem aparecer.
                if (MenuBar.MenuBarWindow.Current is not null || (_settings.AvatarAutoHide && !CursorNearHandle())) SlideHandle(show: false, animate: false);
                AnimateHandleIn();
            }));
        });
    }

    private bool _collapsing;

    // ---------- Animações ----------

    // Estilo Dynamic Island: o painel não aparece "do nada" — a pílula preta da ilha cresce com
    // uma mola até virar o painel (passa um pouquinho do tamanho e acomoda) e o conteúdo vai
    // surgindo de dentro do preto. Ao fechar, encolhe de volta até virar a pílula de novo.
    // A forma é um recorte (Clip) que muda a cada quadro; o conteúdo do painel não é esticado.

    private Spring? _morph;
    private readonly RectangleGeometry _panelClip = new();
    private readonly RectangleGeometry _coverClip = new();
    private readonly ScaleTransform _panelScale = new();
    private readonly ScaleTransform _coverScale = new();
    private Border? _morphCover;
    private Rect _morphFrom;
    private Rect _morphFull;

    /// <summary>Preto da ilha (a "tinta" de que o painel nasce).</summary>
    private static readonly Brush IslandBlack = new SolidColorBrush(Colors.Black);

    private void EnsureMorph()
    {
        if (_morph is not null) return;
        _morphCover = new Border
        {
            Background = IslandBlack,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Clip = _coverClip,
            RenderTransform = _coverScale,
        };
        ((Grid)Content).Children.Add(_morphCover);
        _morph = new Spring(5, ApplyMorph);
    }

    /// <summary>Retângulo (nas coordenadas do painel) de onde o painel sai e para onde volta.</summary>
    private Rect PillRect(bool opening)
    {
        var m = ExpandedPanel.Margin;
        var originX = Left + m.Left;
        var originY = Top + m.Top;
        if (_settings.Edge == DockEdge.Top)
        {
            // Ao abrir, sai da ilha do jeito que ela estiver (fechada, um pouco ou toda aberta);
            // ao fechar, volta para o tamanho da pílula fechada.
            var island = opening ? MenuBar.MenuBarWindow.VisibleIslandRect ?? MenuBar.MenuBarWindow.PillRect : MenuBar.MenuBarWindow.PillRect;
            if (island is { } r) return new Rect(r.X - originX, r.Y - originY, r.Width, r.Height);
            var area = SystemParameters.WorkArea;
            var cx = area.Left + area.Width * Math.Clamp(_settings.Position, 0, 1) - originX;
            return new Rect(cx - 100, area.Top - originY, 200, MenuBar.MenuBarWindow.BarHeight);
        }
        // Painel na lateral: nasce de uma pílula em pé encostada na borda.
        var areaV = SystemParameters.WorkArea;
        var cy = areaV.Top + areaV.Height * Math.Clamp(_settings.Position, 0, 1) - originY;
        var x = _settings.Edge == DockEdge.Left ? areaV.Left - originX : areaV.Right - originX - 34;
        return new Rect(x, cy - 60, 34, 120);
    }

    private void ApplyMorph(double[] v)
    {
        double x = v[0], y = v[1], w = Math.Max(1, v[2]), h = Math.Max(1, v[3]);
        var full = _morphFull;
        var m = ExpandedPanel.Margin;

        // Passou do tamanho final (o "quique" da mola): estica o painel um tiquinho.
        var sx = full.Width > 0 ? Math.Max(1, w / full.Width) : 1;
        var sy = full.Height > 0 ? Math.Max(1, h / full.Height) : 1;
        var cx = _morphFrom.X + _morphFrom.Width / 2;
        _panelScale.CenterX = cx;
        _panelScale.CenterY = 0;
        _panelScale.ScaleX = sx;
        _panelScale.ScaleY = sy;
        _coverScale.CenterX = cx;
        _coverScale.CenterY = m.Top;
        _coverScale.ScaleX = sx;
        _coverScale.ScaleY = sy;

        // Recorte no espaço "sem esticar" (desfaz a escala).
        var local = new Rect(cx + (x - cx) / sx, y / sy, w / sx, h / sy);
        var radius = Math.Clamp(v[4], 0, Math.Min(local.Width, local.Height) / 2);
        _panelClip.Rect = local;
        _panelClip.RadiusX = _panelClip.RadiusY = radius;
        _coverClip.Rect = new Rect(local.X, local.Y + m.Top, local.Width, local.Height);
        _coverClip.RadiusX = _coverClip.RadiusY = radius;

        // Quanto já abriu (0 = pílula, 1 = painel): o preto vai sumindo e o conteúdo aparece.
        var span = full.Height - _morphFrom.Height;
        var p = span > 1 ? Math.Clamp((h - _morphFrom.Height) / span, 0, 1) : 1;
        if (_morphCover is not null) _morphCover.Opacity = 1 - SmoothStep(0.2, 0.8, p);
        PanelShadow.Opacity = SmoothStep(0.75, 1, p);
    }

    private static double SmoothStep(double a, double b, double x)
    {
        var t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private void AnimatePanel(bool show, Action? onDone)
    {
        EnsureMorph();
        var m = ExpandedPanel.Margin;
        _morphCover!.Margin = new Thickness(m.Left, 0, m.Right, m.Bottom);
        _morphCover.CornerRadius = ExpandedPanel.CornerRadius;
        var panelRadius = ExpandedPanel.CornerRadius.TopLeft;
        _morphFull = new Rect(0, 0, Math.Max(1, Width - m.Left - m.Right), Math.Max(1, Height - m.Top - m.Bottom));
        var token = ++_panelAnimToken;

        // Começando agora (e não mudando de ideia no meio): parte da ilha.
        var fresh = ExpandedPanel.Clip is null;
        if (show && fresh)
        {
            _morphFrom = PillRect(opening: true);
            var islandRadius = _settings.Edge == DockEdge.Top ? 16 : 17;
            _morph!.Snap(_morphFrom.X, _morphFrom.Y, _morphFrom.Width, _morphFrom.Height, islandRadius);
        }
        else if (!show)
        {
            if (fresh) _morph!.Snap(0, 0, _morphFull.Width, _morphFull.Height, panelRadius);
            // Volta para a pílula fechada (a ilha reaparece exatamente ali).
            _morphFrom = PillRect(opening: false);
        }

        // Enquanto anima, o painel vira uma "foto" (BitmapCache): recortar/esticar a foto é bem leve.
        var dpi = VisualTreeHelper.GetDpi(this);
        ExpandedPanel.CacheMode = new BitmapCache { RenderAtScale = dpi.DpiScaleX, SnapsToDevicePixels = true };
        ExpandedPanel.BeginAnimation(OpacityProperty, null);
        ExpandedPanel.Opacity = 1;
        ExpandedPanel.RenderTransformOrigin = new Point(0, 0);
        ExpandedPanel.RenderTransform = _panelScale;
        ExpandedPanel.Clip = _panelClip;
        PanelShadow.RenderTransform = _panelScale;
        _morphCover.Visibility = Visibility.Visible;
        if (show && fresh) ApplyMorph(new[] { _morph![0], _morph[1], _morph[2], _morph[3], _morph[4] });

        void Finish()
        {
            if (token != _panelAnimToken) return;
            ExpandedPanel.CacheMode = null;
            if (show)
            {
                // Pronto: tira recorte e escala para o painel ficar "normal" (sem custo extra).
                ExpandedPanel.Clip = null;
                ExpandedPanel.RenderTransform = Transform.Identity;
                PanelShadow.RenderTransform = Transform.Identity;
                PanelShadow.Opacity = 1;
                _morphCover!.Visibility = Visibility.Collapsed;
                onDone?.Invoke();
                return;
            }
            // Fechou: virou a pílula. Com a ilha ligada, ela aparece exatamente no lugar;
            // sem ilha, o preto some rapidinho antes de a janela encolher.
            var cover = _morphCover!;
            void End()
            {
                if (token != _panelAnimToken) return;
                ExpandedPanel.Clip = null;
                ExpandedPanel.RenderTransform = Transform.Identity;
                PanelShadow.RenderTransform = Transform.Identity;
                PanelShadow.Opacity = 1;
                cover.BeginAnimation(OpacityProperty, null);
                cover.Visibility = Visibility.Collapsed;
                onDone?.Invoke();
            }
            if (MenuBar.MenuBarWindow.Current is not null && _settings.Edge == DockEdge.Top)
            {
                End();
                return;
            }
            var fade = Tools.Anim.To(0, 120);
            fade.Completed += (_, _) => End();
            cover.BeginAnimation(OpacityProperty, fade);
        }

        _morphCover.BeginAnimation(OpacityProperty, null);
        if (show)
        {
            // Abre com um quique leve, como a Dynamic Island.
            _morph!.To(0.5, 0.72, Finish, 0, 0, _morphFull.Width, _morphFull.Height, panelRadius);
            if (_settings.Edge == DockEdge.Top) MenuBar.MenuBarWindow.HandOffToPanel();
        }
        else
        {
            // Fecha firme, sem quicar.
            _morph!.To(0.4, 1.0, Finish, _morphFrom.X, _morphFrom.Y, _morphFrom.Width, _morphFrom.Height, _settings.Edge == DockEdge.Top ? 16 : 17);
        }
    }

    private int _panelAnimToken;

    /// <summary>Animação por quadros-chave (tempo em ms, valor, suavização).</summary>
    private static DoubleAnimationUsingKeyFrames Keys(params (double Ms, double Value, IEasingFunction? Ease)[] frames)
    {
        var animation = new DoubleAnimationUsingKeyFrames();
        foreach (var (ms, value, ease) in frames)
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(value,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(Tools.Anim.Enabled ? ms : 0)), ease));
        return animation;
    }

    private void AnimateHandleIn()
    {
        CollapsedHandle.BeginAnimation(OpacityProperty, Tools.Anim.FromTo(0, 1, 160));
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

    /// <summary>Monta os botões estilo Mac e o item de configurações do rodapé.</summary>
    private void BuildChromeButtons()
    {
        SettingsRow.Child = Ui.NavLabel("\uE713", "Configurações");
        var hover = new SolidColorBrush(Colors.Transparent);
        SettingsRow.Background = hover;
        SettingsRow.MouseEnter += (_, _) => hover.BeginAnimation(SolidColorBrush.ColorProperty, Anim.ColorTo(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF), 120));
        SettingsRow.MouseLeave += (_, _) => hover.BeginAnimation(SolidColorBrush.ColorProperty, Anim.ColorTo(Colors.Transparent, 200));

        // Como no Mac: os símbolos dentro das bolinhas aparecem ao passar o mouse.
        WindowDots.MouseEnter += (_, _) =>
        {
            CloseGlyph.BeginAnimation(OpacityProperty, Anim.To(1, 120));
            PinGlyph.BeginAnimation(OpacityProperty, Anim.To(1, 120));
        };
        WindowDots.MouseLeave += (_, _) =>
        {
            CloseGlyph.BeginAnimation(OpacityProperty, Anim.To(0, 180));
            PinGlyph.BeginAnimation(OpacityProperty, Anim.To(0, 180));
        };
        foreach (var dot in new[] { CloseDot, PinDot })
        {
            var scale = new ScaleTransform(1, 1);
            dot.RenderTransformOrigin = new Point(0.5, 0.5);
            dot.RenderTransform = scale;
            dot.MouseEnter += (_, _) => { scale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.To(1.15, 140, Anim.Back)); scale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.To(1.15, 140, Anim.Back)); };
            dot.MouseLeave += (_, _) => { scale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.To(1, 160)); scale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.To(1, 160)); };
        }
    }

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
        HandleAvatar.Width = HandleAvatar.Height = _handleHasText ? 24 : AvatarSize;

        HandleText.Visibility = _handleHasText ? Visibility.Visible : Visibility.Collapsed;
        HandleIcon.Visibility = _hasAvatar ? Visibility.Collapsed : Visibility.Visible;
        HandleIcon.FontSize = _handleHasText ? 12 : 10;
        HandleIcon.Text = _handleHasText && _handleGlyph is not null ? _handleGlyph : DefaultHandleGlyph;

        if (_hasAvatar)
        {
            // Avatar: alça vira um círculo (ou pílula com o tempo) com borda na cor de destaque.
            CollapsedHandle.Padding = new Thickness(_handleHasText ? 4 : 3);
            CollapsedHandle.Margin = _settings.Edge switch
            {
                DockEdge.Top => new Thickness(0, EdgeGap, 0, 0),
                DockEdge.Left => new Thickness(EdgeGap, 0, 0, 0),
                _ => new Thickness(0, 0, EdgeGap, 0),
            };
            CollapsedHandle.CornerRadius = new CornerRadius(_handleHasText ? 16 : AvatarCircle / 2);
        }
        else
        {
            CollapsedHandle.Padding = new Thickness(0);
            CollapsedHandle.Margin = new Thickness(0);
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
        PinDot.Background = new SolidColorBrush(pinned ? Color.FromRgb(0x28, 0xC8, 0x40) : Color.FromRgb(0x4A, 0x4D, 0x57));
        PinDot.ToolTip = pinned ? "Desafixar" : "Fixar aberto";
        PinGlyph.Text = pinned ? "" : "";
    }

    // ---------- Posicionamento ----------

    /// <summary>Ajusta cantos arredondados conforme a borda da tela.</summary>
    public void ApplyEdge()
    {
        // Painel flutuante estilo Mac: cantos arredondados nos quatro lados e um respiro da borda
        // da tela (a margem extra embaixo é o espaço da sombra).
        ExpandedPanel.CornerRadius = new CornerRadius(26);
        ExpandedPanel.BorderThickness = new Thickness(1);
        Sidebar.CornerRadius = new CornerRadius(25, 0, 0, 25);
        ExpandedPanel.Margin = _settings.Edge switch
        {
            DockEdge.Top => new Thickness(16, 8, 16, 24),
            DockEdge.Left => new Thickness(8, 12, 22, 24),
            _ => new Thickness(22, 12, 8, 24),
        };
        switch (_settings.Edge)
        {
            case DockEdge.Top:
                CollapsedHandle.CornerRadius = new CornerRadius(0, 0, 8, 8);
                break;
            case DockEdge.Left:
                CollapsedHandle.CornerRadius = new CornerRadius(0, 8, 8, 0);
                break;
            default:
                CollapsedHandle.CornerRadius = new CornerRadius(8, 0, 0, 8);
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
            // Espaço em volta do painel flutuante (respiro da borda + sombra).
            var m = ExpandedPanel.Margin;
            width = Math.Min(area.Width, width + m.Left + m.Right);
            height = Math.Min(area.Height, height + m.Top + m.Bottom);
        }
        else
        {
            if (_hasAvatar)
            {
                // Círculo do avatar (ou pílula avatar + tempo).
                width = (_handleHasText ? 108 : AvatarCircle) + (top ? 0 : EdgeGap);
                height = (_handleHasText ? 32 : AvatarCircle) + (top ? EdgeGap : 0);
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
        HandleLayoutChanged?.Invoke();
    }

    /// <summary>Retângulo (em DIPs) da alça/avatar visível, para os mini widgets se alinharem.</summary>
    public Rect HandleVisualRect
    {
        get
        {
            var m = CollapsedHandle.Margin;
            return new Rect(Left + m.Left, Top + m.Top, Math.Max(0, Width - m.Left - m.Right), Math.Max(0, Height - m.Top - m.Bottom));
        }
    }

    // ---------- Avatar some quando há uma janela embaixo dele ----------

    private readonly DispatcherTimer _peek = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly TranslateTransform _handleSlide = new();
    private bool _handleHidden;
    private DateTime _handleWantHideSince = DateTime.MaxValue;

    /// <summary>
    /// O avatar fica sempre escondido acima da tela e só desce quando o mouse encosta no
    /// topo, perto dele. Sai de novo quando o mouse se afasta.
    /// </summary>
    private void Peek()
    {
        // Com a ilha (Dynamic Island) ligada, o assistente mora dentro dela: o avatar não aparece.
        if (MenuBar.MenuBarWindow.Current is not null && !_expanded)
        {
            _handleWantHideSince = DateTime.MaxValue;
            if (!_handleHidden) SlideHandle(show: false);
            return;
        }
        if (_expanded || !_settings.AvatarAutoHide)
        {
            _handleWantHideSince = DateTime.MaxValue;
            if (_handleHidden && !_expanded) SlideHandle(show: true);
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        double sx = dpi.DpiScaleX, sy = dpi.DpiScaleY;
        var (cx, cy) = Docking.Native.CursorPosition();
        var area = SystemParameters.WorkArea;

        if (_handleHidden)
        {
            var rect = Widgets.WidgetManager.UnionWith(new Rect(Left, Top, Width, Height));
            rect = new Rect(rect.Left * sx, rect.Top * sy, rect.Width * sx, rect.Height * sy);
            var atEdge = _settings.Edge switch
            {
                DockEdge.Top => cy >= area.Top * sy && cy <= area.Top * sy + 2 && cx >= rect.Left - 40 && cx <= rect.Right + 40,
                DockEdge.Left => cx >= area.Left * sx && cx <= area.Left * sx + 2 && cy >= rect.Top - 40 && cy <= rect.Bottom + 40,
                _ => cx >= area.Right * sx - 3 && cx < area.Right * sx && cy >= rect.Top - 40 && cy <= rect.Bottom + 40,
            };
            if (atEdge && !Docking.Native.IsForegroundFullscreen())
            {
                _handleWantHideSince = DateTime.MaxValue;
                SlideHandle(show: true);
            }
            return;
        }

        if (IsMouseOver || CursorNearHandle())
        {
            _handleWantHideSince = DateTime.MaxValue;
            return;
        }
        if (_handleWantHideSince == DateTime.MaxValue) _handleWantHideSince = DateTime.Now;
        if (DateTime.Now - _handleWantHideSince > TimeSpan.FromMilliseconds(600)) SlideHandle(show: false);
    }

    /// <summary>Mouse perto do avatar ou dos mini widgets ao lado dele?</summary>
    private bool CursorNearHandle()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var rect = Widgets.WidgetManager.UnionWith(new Rect(Left, Top, Width, Height));
        rect = new Rect(rect.Left * dpi.DpiScaleX, rect.Top * dpi.DpiScaleY, rect.Width * dpi.DpiScaleX, rect.Height * dpi.DpiScaleY);
        rect.Inflate(30, 30);
        var (cx, cy) = Docking.Native.CursorPosition();
        return rect.Contains(cx, cy);
    }

    private void SlideHandle(bool show, bool animate = true)
    {
        if (_handleHidden == !show && animate) return;
        _handleHidden = !show;
        // Os widgets do topo somem e voltam junto com o avatar.
        Widgets.WidgetManager.Suppressed = !show;
        var distance = (_settings.Edge == DockEdge.Top ? Height : Width) + 4;
        var to = show ? 0 : _settings.Edge == DockEdge.Right ? distance : -distance;
        var prop = _settings.Edge == DockEdge.Top ? TranslateTransform.YProperty : TranslateTransform.XProperty;
        _handleSlide.BeginAnimation(_settings.Edge == DockEdge.Top ? TranslateTransform.XProperty : TranslateTransform.YProperty, null);
        if (!animate)
        {
            _handleSlide.BeginAnimation(prop, null);
            if (prop == TranslateTransform.YProperty) _handleSlide.Y = to; else _handleSlide.X = to;
            return;
        }
        var ease = new CubicEase { EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn };
        _handleSlide.BeginAnimation(prop, Tools.Anim.To(to, show ? 220 : 180, ease));
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
        return Ui.NavLabel(group.Glyph, group.Name);
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
