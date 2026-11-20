using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Assistente.App.Services;

/// <summary>Um avatar para a alça do widget.</summary>
public sealed record Avatar(string Id, string Name, string Inspiration, ImageSource Image);

/// <summary>
/// 10 avatares originais, desenhados em vetor (sem imagens externas), inspirados
/// em estilos de desenhos, séries e jogos. Nenhum é cópia de personagem existente.
/// </summary>
public static class Avatars
{
    private static readonly Lazy<IReadOnlyList<Avatar>> LazyAll = new(Build);

    public static IReadOnlyList<Avatar> All => LazyAll.Value;

    public static Avatar? Find(string? id) =>
        string.IsNullOrEmpty(id) ? null : All.FirstOrDefault(a => a.Id == id);

    private static IReadOnlyList<Avatar> Build() => new List<Avatar>
    {
        new("robo", "Robô", "Desenho animado", Robot()),
        new("astronauta", "Astronauta", "Série espacial", Astronaut()),
        new("ninja", "Ninja", "Jogo de luta", Ninja()),
        new("gato-mago", "Gato Mago", "Fantasia", CatWizard()),
        new("heroi-pixel", "Herói Pixel", "Jogo 8-bit", PixelHero()),
        new("alien", "Alienzinho", "Ficção científica", Alien()),
        new("cavaleiro", "Cavaleiro", "RPG medieval", Knight()),
        new("fantasma", "Fantasminha", "Desenho de terror fofo", Ghost()),
        new("raposa", "Raposa", "Anime", Fox()),
        new("zumbi", "Zumbi Fofo", "Jogo de zumbi", Zombie()),
    };

    // ---------- Desenhos (tela de 100 × 100) ----------

    private static ImageSource Robot() => Draw("#3B82F6", g =>
    {
        Line(g, "#475569", 3, 50, 30, 50, 17);
        Circle(g, "#EF4444", 50, 15, 5);
        Rect(g, "#94A3B8", 15, 46, 8, 16, 3);
        Rect(g, "#94A3B8", 77, 46, 8, 16, 3);
        Rect(g, "#E2E8F0", 22, 30, 56, 50, 14);
        Rect(g, "#1E293B", 29, 39, 42, 22, 9);
        Circle(g, "#22D3EE", 40, 50, 6);
        Circle(g, "#22D3EE", 60, 50, 6);
        Circle(g, "#FFFFFF", 42, 48, 2);
        Circle(g, "#FFFFFF", 62, 48, 2);
        Rect(g, "#64748B", 38, 67, 24, 5, 2.5);
        Rect(g, "#E2E8F0", 40, 67, 3, 5, 0);
        Rect(g, "#E2E8F0", 48.5, 67, 3, 5, 0);
        Rect(g, "#E2E8F0", 57, 67, 3, 5, 0);
    });

    private static ImageSource Astronaut() => Draw("#1E1B4B", g =>
    {
        foreach (var (x, y, r) in new[] { (16.0, 20.0, 1.5), (82.0, 16.0, 2.0), (88.0, 40.0, 1.2), (12.0, 46.0, 1.2), (24.0, 84.0, 1.5) })
            Circle(g, "#FFFFFF", x, y, r);
        Rect(g, "#94A3B8", 30, 78, 40, 14, 7);
        Circle(g, "#F8FAFC", 50, 52, 30);
        Rect(g, "#0F172A", 28, 38, 44, 30, 15);
        Path(g, "#38BDF8", "M 34,47 Q 38,41 46,41 L 44,44 Q 39,44 36,49 Z", 0.85);
        Circle(g, "#F472B6", 66, 30, 3.5);
        Rect(g, "#CBD5E1", 22, 50, 5, 12, 2);
        Rect(g, "#CBD5E1", 73, 50, 5, 12, 2);
    });

    private static ImageSource Ninja() => Draw("#DC2626", g =>
    {
        Path(g, "#111827", "M 72,38 Q 92,34 94,44 Q 84,42 76,46 Z");
        Path(g, "#111827", "M 72,42 Q 90,46 88,58 Q 82,50 74,48 Z");
        Circle(g, "#111827", 50, 54, 30);
        Rect(g, "#FCD9B6", 24, 44, 52, 17, 8.5);
        Path(g, "#111827", "M 33,47 L 46,50 L 45,52 L 33,50 Z");
        Path(g, "#111827", "M 67,47 L 54,50 L 55,52 L 67,50 Z");
        Ellipse(g, "#111827", 40, 55, 3.5, 3);
        Ellipse(g, "#111827", 60, 55, 3.5, 3);
        Rect(g, "#F8FAFC", 21, 34, 58, 5, 2.5);
    });

    private static ImageSource CatWizard() => Draw("#7C3AED", g =>
    {
        Path(g, "#FB923C", "M 28,58 L 30,36 L 44,48 Z");
        Path(g, "#FB923C", "M 72,58 L 70,36 L 56,48 Z");
        Circle(g, "#FB923C", 50, 64, 24);
        Path(g, "#FDBA74", "M 34,72 Q 50,92 66,72 Q 50,80 34,72 Z");
        Path(g, "#4C1D95", "M 26,42 L 74,42 L 56,6 Z");
        Ellipse(g, "#3B0764", 50, 43, 28, 5);
        Path(g, "#FDE047", "M 52,20 L 54,25 L 59,25 L 55,28 L 57,33 L 52,30 L 47,33 L 49,28 L 45,25 L 50,25 Z");
        Ellipse(g, "#22C55E", 42, 62, 4, 5);
        Ellipse(g, "#22C55E", 58, 62, 4, 5);
        Ellipse(g, "#111827", 42, 62, 1.5, 4);
        Ellipse(g, "#111827", 58, 62, 1.5, 4);
        Path(g, "#F472B6", "M 47,70 L 53,70 L 50,73 Z");
        Line(g, "#7C2D12", 1, 30, 70, 42, 72);
        Line(g, "#7C2D12", 1, 70, 70, 58, 72);
        Line(g, "#7C2D12", 1, 30, 76, 42, 74);
        Line(g, "#7C2D12", 1, 70, 76, 58, 74);
    });

    private static ImageSource PixelHero() => Draw("#10B981", g =>
    {
        // Herói em pixel art 10 × 12 (R vermelho, B marrom, S pele, K preto, Y amarelo, U azul).
        string[] rows =
        {
            "...RRRR...",
            "..RRRRRRR.",
            "..BBSSKS..",
            ".BSBSSKSS.",
            ".BSBBSSSB.",
            "..SSSSBB..",
            "...SSSS...",
            "..RUURUR..",
            ".RRUUUURR.",
            ".SSUYUYSS.",
            "...UU.UU..",
            "..BBB.BBB.",
        };
        var colors = new Dictionary<char, string>
        {
            ['R'] = "#EF4444", ['B'] = "#78350F", ['S'] = "#FCD9B6", ['K'] = "#111827", ['Y'] = "#FDE047", ['U'] = "#2563EB",
        };
        const double size = 6.2;
        var ox = 50 - rows[0].Length * size / 2;
        var oy = 52 - rows.Length * size / 2;
        for (var y = 0; y < rows.Length; y++)
            for (var x = 0; x < rows[y].Length; x++)
                if (colors.TryGetValue(rows[y][x], out var hex))
                    Rect(g, hex, ox + x * size, oy + y * size, size + 0.3, size + 0.3, 0);
    });

    private static ImageSource Alien() => Draw("#0EA5E9", g =>
    {
        Line(g, "#65A30D", 2.5, 38, 30, 30, 14);
        Line(g, "#65A30D", 2.5, 62, 30, 70, 14);
        Circle(g, "#FDE047", 30, 13, 4.5);
        Circle(g, "#FDE047", 70, 13, 4.5);
        Path(g, "#84CC16", "M 50,26 C 78,26 82,52 72,70 C 64,84 36,84 28,70 C 18,52 22,26 50,26 Z");
        Path(g, "#111827", "M 30,50 C 32,40 44,42 46,52 C 46,60 34,62 30,50 Z");
        Path(g, "#111827", "M 70,50 C 68,40 56,42 54,52 C 54,60 66,62 70,50 Z");
        Circle(g, "#FFFFFF", 38, 48, 2.5);
        Circle(g, "#FFFFFF", 62, 48, 2.5);
        Path(g, null, "M 43,70 Q 50,75 57,70", 1, "#365314", 2.5);
    });

    private static ImageSource Knight() => Draw("#F59E0B", g =>
    {
        Path(g, "#DC2626", "M 50,24 C 54,10 72,6 82,14 C 70,14 62,18 58,28 Z");
        Rect(g, "#CBD5E1", 26, 24, 48, 60, 22);
        Rect(g, "#94A3B8", 47, 24, 6, 60, 0);
        Rect(g, "#1E293B", 31, 48, 38, 8, 4);
        Rect(g, "#94A3B8", 47, 46, 6, 12, 0);
        foreach (var x in new[] { 36.0, 42.0, 58.0, 64.0 }) Circle(g, "#1E293B", x, 68, 1.6);
        Path(g, null, "M 33,34 Q 36,28 42,27", 0.9, "#FFFFFF", 2.5);
    });

    private static ImageSource Ghost() => Draw("#A78BFA", g =>
    {
        Path(g, "#F8FAFC", "M 24,86 L 24,48 C 24,30 36,20 50,20 C 64,20 76,30 76,48 L 76,86 Q 71,80 66,86 Q 61,92 56,86 Q 50,80 44,86 Q 39,92 34,86 Q 29,80 24,86 Z");
        Ellipse(g, "#1E1B4B", 41, 48, 4.5, 6);
        Ellipse(g, "#1E1B4B", 59, 48, 4.5, 6);
        Circle(g, "#FFFFFF", 42.5, 46, 1.6);
        Circle(g, "#FFFFFF", 60.5, 46, 1.6);
        Ellipse(g, "#F9A8D4", 34, 58, 5, 3, 0.8);
        Ellipse(g, "#F9A8D4", 66, 58, 5, 3, 0.8);
        Ellipse(g, "#1E1B4B", 50, 62, 3.5, 4);
    });

    private static ImageSource Fox() => Draw("#14B8A6", g =>
    {
        Path(g, "#EA580C", "M 22,22 L 42,40 L 26,52 Z");
        Path(g, "#EA580C", "M 78,22 L 58,40 L 74,52 Z");
        Path(g, "#7C2D12", "M 26,30 L 36,40 L 29,45 Z");
        Path(g, "#7C2D12", "M 74,30 L 64,40 L 71,45 Z");
        Path(g, "#F97316", "M 50,34 C 68,34 82,44 80,58 C 76,72 60,84 50,86 C 40,84 24,72 20,58 C 18,44 32,34 50,34 Z");
        Path(g, "#FFF7ED", "M 22,60 C 30,62 40,68 50,86 C 40,84 26,74 22,60 Z");
        Path(g, "#FFF7ED", "M 78,60 C 70,62 60,68 50,86 C 60,84 74,74 78,60 Z");
        Path(g, "#111827", "M 34,54 Q 40,48 46,54 Q 40,52 34,54 Z");
        Path(g, "#111827", "M 66,54 Q 60,48 54,54 Q 60,52 66,54 Z");
        Ellipse(g, "#111827", 50, 76, 4, 3);
    });

    private static ImageSource Zombie() => Draw("#4B5563", g =>
    {
        Circle(g, "#86EFAC", 50, 54, 30);
        Path(g, "#14532D", "M 28,40 C 30,22 46,18 58,22 C 52,24 50,28 52,32 C 44,28 34,30 28,40 Z");
        Circle(g, "#FFFFFF", 40, 50, 8);
        Circle(g, "#111827", 41, 51, 3.5);
        Circle(g, "#FFFFFF", 62, 52, 5);
        Circle(g, "#111827", 62, 53, 2.2);
        Path(g, null, "M 36,70 Q 50,62 64,70", 1, "#14532D", 2.5);
        Rect(g, "#FFFFFF", 45, 66.5, 5, 5, 1);
        Line(g, "#14532D", 1.5, 70, 32, 76, 46);
        foreach (var t in new[] { 0.2, 0.5, 0.8 })
        {
            var x = 70 + 6 * t;
            var y = 32 + 14 * t;
            Line(g, "#14532D", 1.5, x - 3, y + 1.5, x + 3, y - 1.5);
        }
    });

    // ---------- Auxiliares de desenho ----------

    private static ImageSource Draw(string background, Action<DrawingGroup> paint)
    {
        var group = new DrawingGroup { ClipGeometry = new EllipseGeometry(new Point(50, 50), 50, 50) };
        group.Children.Add(new GeometryDrawing(Brush(background), null, new RectangleGeometry(new Rect(0, 0, 100, 100))));
        // Leve brilho no topo para dar volume.
        group.Children.Add(new GeometryDrawing(
            new LinearGradientBrush(Color.FromArgb(0x30, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90),
            null, new RectangleGeometry(new Rect(0, 0, 100, 55))));
        paint(group);
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    private static Brush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private static void Circle(DrawingGroup g, string fill, double cx, double cy, double r) =>
        g.Children.Add(new GeometryDrawing(Brush(fill), null, new EllipseGeometry(new Point(cx, cy), r, r)));

    private static void Ellipse(DrawingGroup g, string fill, double cx, double cy, double rx, double ry, double opacity = 1)
    {
        var drawing = new GeometryDrawing(Brush(fill), null, new EllipseGeometry(new Point(cx, cy), rx, ry));
        if (opacity < 1) g.Children.Add(new DrawingGroup { Opacity = opacity, Children = { drawing } });
        else g.Children.Add(drawing);
    }

    private static void Rect(DrawingGroup g, string fill, double x, double y, double w, double h, double radius) =>
        g.Children.Add(new GeometryDrawing(Brush(fill), null, new RectangleGeometry(new Rect(x, y, w, h), radius, radius)));

    private static void Line(DrawingGroup g, string stroke, double thickness, double x1, double y1, double x2, double y2) =>
        g.Children.Add(new GeometryDrawing(null, new Pen(Brush(stroke), thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
            new LineGeometry(new Point(x1, y1), new Point(x2, y2))));

    private static void Path(DrawingGroup g, string? fill, string data, double opacity = 1, string? stroke = null, double thickness = 0)
    {
        var pen = stroke is null ? null : new Pen(Brush(stroke), thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var drawing = new GeometryDrawing(fill is null ? null : Brush(fill), pen, Geometry.Parse(data));
        if (opacity < 1) g.Children.Add(new DrawingGroup { Opacity = opacity, Children = { drawing } });
        else g.Children.Add(drawing);
    }
}
