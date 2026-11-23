using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Assistente.App.Tools;

/// <summary>Animações reaproveitáveis. Respeitam a opção "Animações" das configurações.</summary>
public static class Anim
{
    /// <summary>Desligado nas configurações = tudo acontece instantaneamente.</summary>
    public static bool Enabled { get; set; } = true;

    public static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    public static readonly IEasingFunction EaseIn = new CubicEase { EasingMode = EasingMode.EaseIn };
    public static readonly IEasingFunction EaseInOut = new CubicEase { EasingMode = EasingMode.EaseInOut };
    public static readonly IEasingFunction Back = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };

    public static Duration D(double ms) => new(TimeSpan.FromMilliseconds(Enabled ? ms : 0));

    public static DoubleAnimation To(double to, double ms, IEasingFunction? ease = null, double delayMs = 0) => new()
    {
        To = to,
        Duration = D(ms),
        EasingFunction = ease ?? EaseOut,
        BeginTime = TimeSpan.FromMilliseconds(Enabled ? delayMs : 0),
    };

    public static DoubleAnimation FromTo(double from, double to, double ms, IEasingFunction? ease = null, double delayMs = 0)
    {
        var a = To(to, ms, ease, delayMs);
        a.From = from;
        return a;
    }

    public static ColorAnimation ColorTo(Color to, double ms) => new(to, D(ms)) { EasingFunction = EaseOut };

    /// <summary>Elemento aparece subindo um pouco, com fade.</summary>
    public static void FadeUp(UIElement element, double delayMs = 0, double offset = 12)
    {
        var translate = new TranslateTransform(0, offset);
        element.RenderTransform = translate;
        element.Opacity = Enabled ? 0 : 1;
        element.BeginAnimation(UIElement.OpacityProperty, FromTo(0, 1, 260, EaseOut, delayMs));
        translate.BeginAnimation(TranslateTransform.YProperty, FromTo(offset, 0, 320, EaseOut, delayMs));
    }

    /// <summary>
    /// Entrada da tela de uma ferramenta: os blocos aparecem um após o outro (efeito cascata).
    /// </summary>
    public static void EnterView(FrameworkElement view)
    {
        var panel = FindStack(view);
        if (panel is null || panel.Children.Count == 0)
        {
            FadeUp(view);
            return;
        }
        // Só os primeiros blocos entram em cascata (o resto aparece junto): mais leve.
        var i = 0;
        foreach (UIElement child in panel.Children)
        {
            if (i < 8) FadeUp(child, delayMs: i * 30, offset: 10);
            i++;
        }
    }

    private static Panel? FindStack(DependencyObject root)
    {
        // Procura o primeiro StackPanel/DockPanel "de conteúdo" (UserControl → ScrollViewer → StackPanel).
        object? current = root;
        for (var depth = 0; depth < 4 && current is not null; depth++)
        {
            switch (current)
            {
                case StackPanel sp: return sp;
                case DockPanel dp when dp.Children.Count > 0 && dp.Children[0] is StackPanel first: return first;
                case UserControl uc: current = uc.Content; break;
                case ScrollViewer sv: current = sv.Content; break;
                case ContentControl cc: current = cc.Content; break;
                default: return null;
            }
        }
        return null;
    }

    /// <summary>Anima o valor de uma barra (CPU, memória, disco).</summary>
    public static void Bar(RangeBase bar, double value)
    {
        bar.BeginAnimation(RangeBase.ValueProperty, To(value, 450));
    }

    /// <summary>Pequeno "pulo" para chamar atenção (ex.: botão copiado).</summary>
    public static void Pop(FrameworkElement element)
    {
        var scale = new ScaleTransform(1, 1);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = scale;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, FromTo(0.9, 1, 350, Back));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, FromTo(0.9, 1, 350, Back));
    }
}
