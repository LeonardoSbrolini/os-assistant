using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Assistente.App.Tools;

namespace Assistente.App.Widgets;

/// <summary>
/// Botão redondo dos mini widgets (desenhado do zero, sem o visual padrão do Windows):
/// "cheio" = círculo branco com ícone escuro (como o play do Spotify/Apple Music);
/// "vazio" = só o ícone, com um círculo claro aparecendo no hover.
/// </summary>
public sealed class PillButton : Border
{
    private readonly TextBlock _glyph;
    private readonly ScaleTransform _scale = new();
    private readonly bool _filled;
    private readonly Brush _rest;
    private readonly Brush _hover;
    private bool _pressed;

    public PillButton(string glyph, Action onClick, bool filled, double size)
    {
        _filled = filled;
        Width = size;
        Height = size;
        CornerRadius = new CornerRadius(size / 2);
        Cursor = Cursors.Hand;
        VerticalAlignment = VerticalAlignment.Center;
        Margin = new Thickness(2, 0, 0, 0);
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = _scale;

        _rest = filled ? new SolidColorBrush(Color.FromRgb(0xF5, 0xF6, 0xF8)) : new SolidColorBrush(Color.FromArgb(0x01, 0xFF, 0xFF, 0xFF));
        _hover = filled ? Brushes.White : new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
        Background = _rest;

        _glyph = new TextBlock
        {
            Text = glyph,
            FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
            FontSize = filled ? size * 0.4 : size * 0.44,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = filled ? new SolidColorBrush(Color.FromRgb(0x12, 0x13, 0x16)) : Brushes.White,
        };
        Child = _glyph;

        MouseEnter += (_, _) => { Background = _hover; Scale(filled ? 1.1 : 1.05); };
        MouseLeave += (_, _) => { Background = _rest; _pressed = false; Scale(1); };
        MouseLeftButtonDown += (_, e) => { _pressed = true; Scale(0.88); e.Handled = true; };
        MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (!_pressed) return;
            _pressed = false;
            Scale(filled ? 1.1 : 1.05);
            onClick();
            Clicked?.Invoke();
        };
    }

    /// <summary>Clique extra (além da ação do construtor).</summary>
    public event Action? Clicked;

    public string Glyph
    {
        get => _glyph.Text;
        set => _glyph.Text = value;
    }

    private void Scale(double to)
    {
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.To(to, 120, Anim.Back));
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.To(to, 120, Anim.Back));
    }
}
