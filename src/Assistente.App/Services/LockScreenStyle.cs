using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Assistente.App.Services;

/// <summary>
/// Deixa a tela de bloqueio do Windows mais limpa, estilo Mac: tira dicas, "curiosidades" e
/// a foto que troca sozinha (Destaque do Windows) e põe um papel de parede liso em degradê.
/// O relógio e a posição dos textos são do próprio Windows e não podem ser trocados.
/// </summary>
public static class LockScreenStyle
{
    private const string CdmKey = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";

    /// <summary>Dicas, curiosidades e Destaque do Windows (1 = ligado).</summary>
    private static readonly string[] Values =
    {
        "RotatingLockScreenEnabled",
        "RotatingLockScreenOverlayEnabled",
        "SubscribedContent-338387Enabled",
    };

    public static readonly (string Key, string Name, Color[] Colors)[] Styles =
    {
        ("noite", "Noite", new[] { Color.FromRgb(0x07, 0x0B, 0x1E), Color.FromRgb(0x5B, 0x3D, 0xF5), Color.FromRgb(0x1E, 0x7B, 0xFF), Color.FromRgb(0xC2, 0x4D, 0xFF) }),
        ("aurora", "Aurora", new[] { Color.FromRgb(0x03, 0x14, 0x1A), Color.FromRgb(0x00, 0xB3, 0x8F), Color.FromRgb(0x1F, 0x6F, 0xFF), Color.FromRgb(0x7C, 0xFF, 0xC4) }),
        ("por-do-sol", "Pôr do sol", new[] { Color.FromRgb(0x1A, 0x0A, 0x1F), Color.FromRgb(0xFF, 0x6A, 0x3D), Color.FromRgb(0xE8, 0x3E, 0x8C), Color.FromRgb(0xFF, 0xC1, 0x4D) }),
        ("grafite", "Grafite", new[] { Color.FromRgb(0x0C, 0x0C, 0x0E), Color.FromRgb(0x3A, 0x3A, 0x42), Color.FromRgb(0x24, 0x24, 0x2A), Color.FromRgb(0x5A, 0x5A, 0x66) }),
    };

    /// <summary>Tira dicas/curiosidades e o Destaque, guardando como estava para poder voltar.</summary>
    public static void HideClutter(AppSettings settings)
    {
        using var key = Registry.CurrentUser.CreateSubKey(CdmKey, writable: true);
        if (settings.LockScreenOriginal is null)
        {
            var original = new Dictionary<string, int>();
            foreach (var name in Values)
                original[name] = key.GetValue(name) is int v ? v : -1;
            settings.LockScreenOriginal = original;
            settings.Save();
        }
        foreach (var name in Values) key.SetValue(name, 0, RegistryValueKind.DWord);
    }

    /// <summary>Volta dicas/curiosidades/Destaque como estavam antes.</summary>
    public static void Restore(AppSettings settings)
    {
        if (settings.LockScreenOriginal is not { } original) return;
        using var key = Registry.CurrentUser.CreateSubKey(CdmKey, writable: true);
        foreach (var (name, value) in original)
        {
            if (value < 0) key.DeleteValue(name, throwOnMissingValue: false);
            else key.SetValue(name, value, RegistryValueKind.DWord);
        }
        settings.LockScreenOriginal = null;
        settings.Save();
    }

    /// <summary>Gera o papel de parede em degradê (na thread da interface) e salva em PNG.</summary>
    public static string RenderWallpaper(string style)
    {
        var colors = Array.Find(Styles, s => s.Key == style).Colors ?? Styles[0].Colors;
        var (w, h) = Docking.Native.PrimaryScreenPixels();
        w = Math.Max(1280, w);
        h = Math.Max(720, h);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var rect = new Rect(0, 0, w, h);
            dc.DrawRectangle(new SolidColorBrush(colors[0]), null, rect);
            // Manchas de luz grandes e bem suaves, como os papéis de parede do macOS.
            Blob(dc, colors[1], w * 0.18, h * 0.85, w * 0.75, 0.85);
            Blob(dc, colors[2], w * 0.85, h * 0.25, w * 0.65, 0.75);
            Blob(dc, colors[3], w * 0.55, h * 1.05, w * 0.55, 0.55);
            // Escurece o topo para o relógio branco ficar legível.
            var shade = new LinearGradientBrush(Color.FromArgb(0x99, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 90);
            dc.DrawRectangle(shade, null, new Rect(0, 0, w, h * 0.55));
        }
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Assistente");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"tela-de-bloqueio-{style}.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(path)) encoder.Save(file);
        return path;
    }

    private static void Blob(DrawingContext dc, Color color, double cx, double cy, double radius, double strength)
    {
        var brush = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb((byte)(255 * strength), color.R, color.G, color.B), 0),
                new GradientStop(Color.FromArgb((byte)(110 * strength), color.R, color.G, color.B), 0.45),
                new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1),
            },
        };
        dc.DrawEllipse(brush, null, new Point(cx, cy), radius, radius);
    }

    /// <summary>Define a imagem da tela de bloqueio do usuário atual.</summary>
    public static async Task SetImageAsync(string path)
    {
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
        await Windows.System.UserProfile.LockScreen.SetImageFileAsync(file);
    }
}
