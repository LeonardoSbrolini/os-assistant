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

    /// <summary>Temas: cor de fundo das telas (a transparência é ajustada à parte).</summary>
    public static readonly (string Name, string Hex)[] Themes =
    {
        ("Escuro", "#1C1D22"),
        ("Meia-noite", "#0F172A"),
        ("Grafite", "#262626"),
        ("Noite roxa", "#1E1530"),
        ("Floresta", "#0E1F1A"),
        ("Vinho", "#2A1218"),
        ("Oceano", "#0B2230"),
    };

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

        var themeHex = Array.Find(Themes, t => t.Name == settings.Theme).Hex ?? Themes[0].Hex;
        var baseColor = ParseColor(themeHex, Color.FromRgb(0x1C, 0x1D, 0x22));
        resources["ThemeBaseColor"] = baseColor;

        var panelAlpha = (byte)Math.Round(Math.Clamp(settings.PanelOpacity, 0.6, 1) * 255);
        resources["PanelBrush"] = Gradient(baseColor, panelAlpha);

        var windowAlpha = (byte)Math.Round(Math.Clamp(settings.WindowOpacity, 0.6, 1) * 255);
        resources["WindowBrush"] = Gradient(baseColor, windowAlpha);

        // Brilho suave da cor de destaque no canto de cima (dá profundidade ao painel).
        var glow = new RadialGradientBrush
        {
            Center = new Point(0.15, 0),
            GradientOrigin = new Point(0.15, 0),
            RadiusX = 0.9,
            RadiusY = 0.7,
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x30, accent.R, accent.G, accent.B), 0),
                new GradientStop(Color.FromArgb(0x10, accent.R, accent.G, accent.B), 0.5),
                new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 1),
            },
        };
        glow.Freeze();
        resources["PanelGlowBrush"] = glow;
    }

    /// <summary>Fundo em degradê: um pouco mais claro em cima, mais escuro embaixo.</summary>
    private static Brush Gradient(Color c, byte alpha)
    {
        // Estilo "Dynamic Island": quase preto, só um toque da cor do tema.
        static byte Dark(byte v, double k) => (byte)Math.Clamp(v * k, 0, 255);
        var brush = new LinearGradientBrush(
            Color.FromArgb(alpha, Dark(c.R, 0.45), Dark(c.G, 0.45), Dark(c.B, 0.5)),
            Color.FromArgb(alpha, Dark(c.R, 0.12), Dark(c.G, 0.12), Dark(c.B, 0.14)), 90);
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
