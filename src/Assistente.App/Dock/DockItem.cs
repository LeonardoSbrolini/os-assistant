using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Assistente.App.Tools;

namespace Assistente.App.Docking;

/// <summary>Um ícone do dock: programa fixado, programa aberto, botão Iniciar ou separador.</summary>
internal sealed class DockItem
{
    private Grid? _view;
    private Image? _image;
    private Border? _glyph;
    private Border? _dot;
    private readonly TranslateTransform _bounce = new();

    public DockItem(string key, string? launchPath, string name)
    {
        Key = key;
        LaunchPath = launchPath;
        Name = name;
    }

    public static DockItem StartButton() => new("::start", null, "Iniciar") { IsStart = true };
    public static DockItem Separator() => new("::sep", null, "") { IsSeparator = true };

    public string Key { get; }
    public string? LaunchPath { get; }
    public string Name { get; }
    public bool IsStart { get; private init; }
    public bool IsSeparator { get; private init; }
    public List<OpenWindow> Windows { get; set; } = new();
    public ImageSource? IconOverride { get; init; }
    public double Scale { get; set; } = 1;

    public FrameworkElement View => _view!;

    public void Build(double size)
    {
        _view = new Grid { VerticalAlignment = VerticalAlignment.Bottom, Background = Brushes.Transparent };
        if (IsSeparator)
        {
            var line = new Border
            {
                Width = 1,
                Height = size * 0.7,
                Margin = new Thickness(6, 0, 6, 10),
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            line.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
            _view.Children.Add(line);
            return;
        }

        _view.Cursor = Cursors.Hand;
        _view.ToolTip = new ToolTip { Content = Name, Placement = System.Windows.Controls.Primitives.PlacementMode.Top, VerticalOffset = -4 };
        ToolTipService.SetInitialShowDelay(_view, 150);
        _view.Margin = new Thickness(4, 0, 4, 0);
        _view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _view.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
        _view.RenderTransform = _bounce;

        FrameworkElement icon;
        if (IsStart)
        {
            _glyph = new Border { CornerRadius = new CornerRadius(12), Child = WindowsLogo() };
            _glyph.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            icon = _glyph;
        }
        else
        {
            var source = IconOverride ?? (LaunchPath is null ? null : Native.GetIcon(LaunchPath));
            if (source is null)
            {
                _glyph = new Border
                {
                    CornerRadius = new CornerRadius(12),
                    Child = new TextBlock
                    {
                        Text = Name.Length > 0 ? Name.Substring(0, 1).ToUpperInvariant() : "?",
                        FontSize = 20,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                _glyph.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
                icon = _glyph;
            }
            else
            {
                _image = new Image { Source = source, Stretch = Stretch.Uniform };
                RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
                icon = _image;
            }
        }
        icon.VerticalAlignment = VerticalAlignment.Bottom;
        _view.Children.Add(icon);

        _dot = new Border { Width = 4, Height = 4, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center, Opacity = 0 };
        _dot.SetResourceReference(Border.BackgroundProperty, "TextBrush");
        Grid.SetRow(_dot, 1);
        _view.Children.Add(_dot);

        _view.MouseLeftButtonDown += (_, _) => Press(true);
        _view.MouseLeftButtonUp += (_, _) => Press(false);
        _view.MouseLeave += (_, _) => Press(false);

        ApplyScale(size);
    }

    public void ApplyScale(double size)
    {
        if (_view is null || IsSeparator) return;
        var s = size * Scale;
        FrameworkElement? icon = (FrameworkElement?)_image ?? _glyph;
        if (icon is null) return;
        icon.Width = s;
        icon.Height = s;
        if (_glyph is not null)
        {
            _glyph.CornerRadius = new CornerRadius(s * 0.24);
            if (_glyph.Child is FrameworkElement child)
            {
                child.Width = child is TextBlock ? double.NaN : s * 0.42;
                child.Height = child is TextBlock ? double.NaN : s * 0.42;
                if (child is TextBlock text) text.FontSize = s * 0.42;
            }
        }
    }

    private bool _running;
    private bool _active;

    public void SetRunning(bool running, bool active)
    {
        if (_dot is null || (running == _running && active == _active)) return;
        _running = running;
        _active = active;
        _dot.Width = active ? 10 : 4;
        _dot.BeginAnimation(UIElement.OpacityProperty, Anim.To(running ? (active ? 1 : 0.7) : 0, 200));
    }

    private void Press(bool down)
    {
        if (_view is null) return;
        _view.Opacity = down ? 0.7 : 1;
    }

    /// <summary>Pulinho do ícone ao abrir o programa (como no Mac).</summary>
    public void Bounce()
    {
        if (!Anim.Enabled) return;
        var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1200) };
        foreach (var (ms, y) in new[] { (0, 0.0), (180, -18.0), (380, 0.0), (560, -10.0), (740, 0.0), (1200, 0.0) })
            a.KeyFrames.Add(new EasingDoubleKeyFrame(y, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), new QuadraticEase()));
        _bounce.BeginAnimation(TranslateTransform.YProperty, a);
    }

    /// <summary>Logo de 4 quadrados (botão Iniciar).</summary>
    private static UIElement WindowsLogo()
    {
        {
            var g = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
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
}
