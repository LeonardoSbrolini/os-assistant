using System;
using System.Windows;
using System.Windows.Media;

namespace Assistente.App.Services;

/// <summary>Aplica as cores escolhidas nas configurações (os estilos usam DynamicResource).</summary>
public static class ThemeService
{
    /// <summary>Cores sugeridas na tela de configurações.</summary>
    public static readonly (string Name, string Hex)[] AccentPresets =
    {
        ("Azul", "#4F8CFF"),
        ("Roxo", "#8B5CF6"),
        ("Rosa", "#EC4899"),
        ("Vermelho", "#EF4444"),
        ("Laranja", "#F97316"),
        ("Amarelo", "#EAB308"),
        ("Verde", "#22C55E"),
        ("Ciano", "#06B6D4"),
    };

    private static readonly Color PanelBase = Color.FromRgb(0x1C, 0x1D, 0x22);

    public static Color ParseColor(string? hex, Color fallback)
    {
        try
        {
            return hex is null ? fallback : (Color)ColorConverter.ConvertFromString(hex);
        }
        catch (FormatException)
        {
            return fallback;
        }
    }

    public static void Apply(AppSettings settings)
    {
        var resources = Application.Current.Resources;
        var accent = ParseColor(settings.AccentColor, Color.FromRgb(0x4F, 0x8C, 0xFF));

        resources["AccentBrush"] = Frozen(accent);
        resources["AccentSoftBrush"] = Frozen(Color.FromArgb(0x33, accent.R, accent.G, accent.B));

        var alpha = (byte)Math.Round(Math.Clamp(settings.PanelOpacity, 0.6, 1) * 255);
        resources["PanelBrush"] = Frozen(Color.FromArgb(alpha, PanelBase.R, PanelBase.G, PanelBase.B));
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
