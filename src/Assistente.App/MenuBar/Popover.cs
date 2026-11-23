using System.Windows.Documents;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Assistente.App.Tools;

namespace Assistente.App.MenuBar;

/// <summary>
/// Janelinha flutuante (popover) com cantos arredondados, que abre ao passar o mouse num
/// item e fecha quando o mouse sai do item e dela.
/// </summary>
public sealed class Popover
{
    private readonly Popup _popup;
    private readonly Border _card;
    private readonly ScaleTransform _scale = new(0.96, 0.96);
    private readonly DispatcherTimer _close = new() { Interval = TimeSpan.FromMilliseconds(260) };
    private FrameworkElement? _owner;
    private Action? _update;

    public Popover()
    {
        _card = new Border
        {
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(10, 6, 10, 14),
            MinWidth = 250,
            BorderThickness = new Thickness(1),
            BorderBrush = new LinearGradientBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), 90),
            Background = new LinearGradientBrush(Color.FromArgb(0xF5, 0x22, 0x23, 0x2B), Color.FromArgb(0xF5, 0x17, 0x18, 0x1E), 90),
            RenderTransformOrigin = new Point(0.5, 0),
            RenderTransform = _scale,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = 0.45 },
        };
        TextElement.SetForeground(_card, Brushes.White);
        TextElement.SetFontFamily(_card, new FontFamily("Segoe UI"));
        TextElement.SetFontSize(_card, 12.5);

        _popup = new Popup
        {
            AllowsTransparency = true,
            Placement = PlacementMode.Bottom,
            StaysOpen = true,
            Child = _card,
            PopupAnimation = PopupAnimation.None,
        };
        _card.MouseEnter += (_, _) => _close.Stop();
        _card.MouseLeave += (_, _) => _close.Start();
        _close.Tick += (_, _) =>
        {
            _close.Stop();
            if (_owner?.IsMouseOver == true || _card.IsMouseOver) return;
            Close();
        };
    }

    public bool IsOpen => _popup.IsOpen;

    /// <summary>Liga o popover a um item: abre ao passar o mouse, com o conteúdo montado na hora.</summary>
    public void Attach(FrameworkElement owner, Func<(UIElement View, Action? Update)> build)
    {
        owner.MouseEnter += (_, _) => Open(owner, build);
        owner.MouseLeave += (_, _) => _close.Start();
    }

    public void Open(FrameworkElement owner, Func<(UIElement View, Action? Update)> build)
    {
        _close.Stop();
        if (_popup.IsOpen && ReferenceEquals(_owner, owner)) return;
        _owner = owner;
        var (view, update) = build();
        _update = update;
        _card.Child = view;
        update?.Invoke();
        _popup.PlacementTarget = owner;
        _popup.HorizontalOffset = -12;
        _popup.IsOpen = true;
        _card.BeginAnimation(UIElement.OpacityProperty, Anim.FromTo(0, 1, 140));
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.FromTo(0.96, 1, 180, Anim.Back));
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.FromTo(0.96, 1, 180, Anim.Back));
    }

    /// <summary>Atualiza os números do conteúdo aberto.</summary>
    public void Refresh()
    {
        if (_popup.IsOpen) _update?.Invoke();
    }

    public void Close()
    {
        _close.Stop();
        _popup.IsOpen = false;
        _owner = null;
        _update = null;
    }
}
