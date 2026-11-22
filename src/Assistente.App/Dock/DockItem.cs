using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Assistente.App.Tools;

namespace Assistente.App.Docking;

/// <summary>
/// Um ícone do dock: programa fixado, programa aberto, botão Iniciar ou separador.
/// O tamanho do ícone muda só por transformação (sem recalcular o layout), para o zoom ficar liso.
/// </summary>
internal sealed class DockItem
{
    public const double Gap = 8;
    public const double SeparatorWidth = 13;
    public const double DotSpace = 8;

    private readonly ScaleTransform _scale = new();
    private readonly TranslateTransform _bounce = new();
    private Grid? _view;
    private FrameworkElement? _icon;
    private Border? _dot;
    private bool _running;
    private bool _active;

    public DockItem(string key, string? launchPath, string name)
    {
        Key = key;
        LaunchPath = launchPath;
        Name = name;
    }

    public static DockItem StartButton() => new("::start", null, "Iniciar") { IsStart = true };
    public static DockItem Separator(string key = "::sep") => new(key, null, "") { IsSeparator = true };

    /// <summary>Botão com ícone de símbolo (Wi‑Fi, volume...) que executa uma ação.</summary>
    public static DockItem Glyph(string key, string glyph, string name, Action action) =>
        new(key, null, name) { GlyphText = glyph, Action = action };

    /// <summary>Relógio com data; clique abre notificações e calendário.</summary>
    public static DockItem Clock(Action action) => new("::clock", null, "Notificações e calendário (Win + N)") { IsClock = true, Action = action };

    public string Key { get; }
    public string? LaunchPath { get; }
    public string Name { get; }
    public bool IsStart { get; private init; }
    public bool IsSeparator { get; private init; }
    public List<OpenWindow> Windows { get; set; } = new();
    public ImageSource? IconOverride { get; init; }
    public string? GlyphText { get; private init; }
    public bool IsClock { get; private init; }
    public Action? Action { get; private init; }
    private TextBlock? _clockTime;
    private TextBlock? _clockDate;

    /// <summary>Zoom atual (1 = normal) e o desejado (para onde a animação está indo).</summary>
    public double Scale { get; set; } = 1;
    public double TargetScale { get; set; } = 1;

    public FrameworkElement View => _view!;

    /// <summary>Largura que o item ocupa no dock com o zoom atual.</summary>
    public double SlotWidth(double size) => IsSeparator ? SeparatorWidth : (BaseWidth(size)) * Scale;

    /// <summary>Largura sem zoom (o relógio é mais largo).</summary>
    public double BaseWidth(double size) => IsSeparator ? SeparatorWidth : IsClock ? size * 1.5 + Gap : size + Gap;

    public void Build(double size)
    {
        _view = new Grid { Background = Brushes.Transparent };
        if (IsSeparator)
        {
            _view.Width = SeparatorWidth;
            _view.Height = size + DotSpace;
            var line = new Border
            {
                Width = 1,
                Height = size * 0.7,
                Margin = new Thickness(0, 0, 0, DotSpace + size * 0.15),
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            line.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
            _view.Children.Add(line);
            return;
        }

        _view.Width = BaseWidth(size);
        _view.Height = size + DotSpace;
        _view.Cursor = Cursors.Hand;

        if (IsClock)
        {
            _clockTime = new TextBlock { FontSize = size * 0.3, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
            _clockDate = new TextBlock { FontSize = size * 0.21, HorizontalAlignment = HorizontalAlignment.Center };
            _clockTime.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            _clockDate.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(_clockTime);
            stack.Children.Add(_clockDate);
            var clock = new Border { CornerRadius = new CornerRadius(size * 0.24), Child = stack, Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)) };
            _icon = clock;
            Tick();
        }
        else if (GlyphText is not null)
        {
            var glyph = new Border
            {
                CornerRadius = new CornerRadius(size * 0.24),
                Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                Child = new TextBlock
                {
                    Text = GlyphText,
                    FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
                    FontSize = size * 0.42,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            ((TextBlock)glyph.Child).SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            _icon = glyph;
        }
        else if (IsStart)
        {
            var start = new Border { CornerRadius = new CornerRadius(size * 0.24), Child = WindowsLogo(size * 0.42) };
            start.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            _icon = start;
        }
        else if ((IconOverride ?? (LaunchPath is null ? null : Native.GetIcon(LaunchPath))) is { } source)
        {
            var image = new Image { Source = source, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            _icon = image;
        }
        else
        {
            var letter = new Border
            {
                CornerRadius = new CornerRadius(size * 0.24),
                Child = new TextBlock
                {
                    Text = Name.Length > 0 ? Name.Substring(0, 1).ToUpperInvariant() : "?",
                    FontSize = size * 0.42,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            letter.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
            _icon = letter;
        }

        _icon.Width = IsClock ? size * 1.5 : size;
        _icon.Height = size;
        _icon.VerticalAlignment = VerticalAlignment.Top;
        _icon.HorizontalAlignment = HorizontalAlignment.Center;
        _icon.RenderTransformOrigin = new Point(0.5, 1);
        _icon.RenderTransform = new TransformGroup { Children = { _scale, _bounce } };
        _icon.ToolTip = new ToolTip { Content = Name, Placement = PlacementMode.Top, VerticalOffset = -6 };
        ToolTipService.SetInitialShowDelay(_icon, 120);
        _view.Children.Add(_icon);

        _dot = new Border
        {
            Width = 4,
            Height = 4,
            CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 2),
            Opacity = 0,
        };
        _dot.SetResourceReference(Border.BackgroundProperty, "TextBrush");
        _view.Children.Add(_dot);

        _view.PreviewMouseLeftButtonDown += (_, _) => _icon.Opacity = 0.65;
        _view.PreviewMouseLeftButtonUp += (_, _) => _icon.Opacity = 1;
        _view.MouseLeave += (_, _) => _icon.Opacity = 1;
        ApplyScale();
    }

    /// <summary>Atualiza o relógio (chamado várias vezes por segundo; só muda o texto quando precisa).</summary>
    public void Tick()
    {
        if (_clockTime is null || _clockDate is null) return;
        var now = DateTime.Now;
        var time = now.ToString("HH:mm");
        if (_clockTime.Text != time)
        {
            _clockTime.Text = time;
            _clockDate.Text = now.ToString("ddd dd/MM", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
        }
    }

    public void ApplyScale()
    {
        if (_icon is null) return;
        _scale.ScaleX = _scale.ScaleY = Scale;
    }

    public void SetRunning(bool running, bool active)
    {
        if (_dot is null || (running == _running && active == _active)) return;
        _running = running;
        _active = active;
        _dot.BeginAnimation(FrameworkElement.WidthProperty, Anim.To(active ? 12 : 4, 220));
        _dot.BeginAnimation(UIElement.OpacityProperty, Anim.To(running ? (active ? 1 : 0.65) : 0, 220));
    }

    /// <summary>Pulinho do ícone ao abrir o programa (como no Mac).</summary>
    public void Bounce()
    {
        if (!Anim.Enabled) return;
        var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1100) };
        foreach (var (ms, y) in new[] { (0, 0.0), (170, -20.0), (360, 0.0), (520, -10.0), (690, 0.0), (1100, 0.0) })
            a.KeyFrames.Add(new EasingDoubleKeyFrame(y, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), new QuadraticEase()));
        _bounce.BeginAnimation(TranslateTransform.YProperty, a);
    }

    /// <summary>Logo de 4 quadrados (botão Iniciar).</summary>
    private static UIElement WindowsLogo(double size)
    {
        var g = new Grid { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        g.RowDefinitions.Add(new RowDefinition());
        g.RowDefinitions.Add(new RowDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < 4; i++)
        {
            var r = new Rectangle { Fill = Brushes.White, Margin = new Thickness(1), RadiusX = 1, RadiusY = 1 };
            Grid.SetRow(r, i / 2);
            Grid.SetColumn(r, i % 2);
            g.Children.Add(r);
        }
        return g;
    }
}
