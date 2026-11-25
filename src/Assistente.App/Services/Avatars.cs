using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Assistente.App.Services;

/// <summary>Um avatar para a alça do widget. Os animados têm vários quadros (<see cref="Frames"/>).</summary>
public sealed record Avatar(string Id, string Name, string Inspiration, ImageSource Image)
{
    /// <summary>Quadros da animação (nulo = avatar parado).</summary>
    public IReadOnlyList<ImageSource>? Frames { get; init; }

    /// <summary>Duração de cada quadro, em milissegundos.</summary>
    public double FrameMs { get; init; } = 90;

    public bool IsAnimated => Frames is { Count: > 1 };
}

/// <summary>
/// 10 avatares originais, desenhados em vetor (sem imagens externas), inspirados
/// em estilos de desenhos, séries e jogos. Nenhum é cópia de personagem existente.
/// </summary>
public static class Avatars
{
    private static readonly Lazy<IReadOnlyList<Avatar>> LazyAll = new(Build);

    public static IReadOnlyList<Avatar> All => LazyAll.Value;

    /// <summary>Só os animados (para a seção "Animados" das configurações).</summary>
    public static IEnumerable<Avatar> Animated => All.Where(a => a.IsAnimated);

    public const string PhotoId = "foto";

    /// <summary>Arquivo da foto do usuário (cópia guardada na pasta do app).</summary>
    public static string? PhotoPath { get; set; }

    private static Avatar? _photo;
    private static string? _photoKey;

    /// <summary>"Minha foto": a foto escolhida, recortada em círculo.</summary>
    public static Avatar? Photo
    {
        get
        {
            if (PhotoPath is null || !File.Exists(PhotoPath)) return null;
            var key = PhotoPath + File.GetLastWriteTimeUtc(PhotoPath).Ticks;
            if (key == _photoKey) return _photo;
            _photoKey = key;
            _photo = null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad; // não prende o arquivo
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bmp.DecodePixelWidth = 256;
                bmp.UriSource = new Uri(PhotoPath);
                bmp.EndInit();
                bmp.Freeze();
                // Preenche o círculo sem distorcer (corta as sobras, como "preencher").
                var ratio = bmp.PixelWidth / (double)Math.Max(1, bmp.PixelHeight);
                var w = ratio >= 1 ? 100 * ratio : 100;
                var h = ratio >= 1 ? 100 : 100 / ratio;
                var group = new DrawingGroup { ClipGeometry = new EllipseGeometry(new Point(50, 50), 50, 50) };
                group.Children.Add(new ImageDrawing(bmp, new Rect(50 - w / 2, 50 - h / 2, w, h)));
                var image = new DrawingImage(group);
                image.Freeze();
                _photo = new Avatar(PhotoId, "Minha foto", "Sua foto", image);
            }
            catch (Exception)
            {
                // Arquivo que não é imagem: fica sem foto.
            }
            return _photo;
        }
    }

    public static Avatar? Find(string? id) =>
        string.IsNullOrEmpty(id) ? null : id == PhotoId ? Photo : All.FirstOrDefault(a => a.Id == id);

    /// <summary>Copia a foto escolhida para a pasta do app (assim continua funcionando se o original sumir).</summary>
    public static string SavePhoto(string source)
    {
        var folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Assistente");
        Directory.CreateDirectory(folder);
        foreach (var old in Directory.GetFiles(folder, "avatar-foto.*"))
        {
            try { File.Delete(old); } catch (Exception) { }
        }
        var target = System.IO.Path.Combine(folder, "avatar-foto" + System.IO.Path.GetExtension(source).ToLowerInvariant());
        File.Copy(source, target, overwrite: true);
        return target;
    }

    /// <summary>Mostra o avatar numa imagem; se for animado, a animação roda sem parar.</summary>
    public static void Show(Image image, Avatar? avatar)
    {
        image.BeginAnimation(Image.SourceProperty, null);
        image.Source = avatar?.Image;
        if (avatar?.Frames is not { Count: > 1 } frames || !Tools.Anim.Enabled) return;
        var anim = new ObjectAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(frames.Count * avatar.FrameMs),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        for (var i = 0; i < frames.Count; i++)
            anim.KeyFrames.Add(new DiscreteObjectKeyFrame(frames[i], KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(i * avatar.FrameMs))));
        image.BeginAnimation(Image.SourceProperty, anim);
    }

    /// <summary>Uma imagem pronta do avatar (animada, se for o caso).</summary>
    public static Image View(Avatar avatar, double size)
    {
        var image = new Image { Width = size, Height = size, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        Show(image, avatar);
        return image;
    }

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
        AnimatedAvatar("pinguim", "Pinguim", "Animado · acenando", Penguin),
        AnimatedAvatar("fantasma-voa", "Fantasma Voador", "Animado · flutuando", FloatingGhost),
        AnimatedAvatar("foguete", "Foguete", "Animado · decolando", Rocket),
        AnimatedAvatar("planeta", "Planetinha", "Animado · lua girando", Planet),
        AnimatedAvatar("robo-pisca", "Robô Pisca", "Animado · piscando", BlinkRobot),
        AnimatedAvatar("gatinho", "Gatinho Soneca", "Animado · dormindo", SleepyCat),
        AnimatedAvatar("slime", "Slime", "Animado · pulando", Slime),
        AnimatedAvatar("sol", "Solzinho", "Animado · brilhando", Sun),
        AnimatedAvatar("coracao", "Coração", "Animado · batendo", Heart),
        AnimatedAvatar("peixe", "Peixinho", "Animado · nadando", Fish),
    };

    // ---------- Avatares animados (quadros desenhados com t de 0 a 1) ----------

    private const int FrameCount = 16;

    private static Avatar AnimatedAvatar(string id, string name, string hint, Func<double, ImageSource> frame)
    {
        var frames = Enumerable.Range(0, FrameCount).Select(i => frame(i / (double)FrameCount)).ToList();
        return new Avatar(id, name, hint, frames[0]) { Frames = frames, FrameMs = 85 };
    }

    private static double Wave(double t, double phase = 0) => Math.Sin((t + phase) * 2 * Math.PI);

    private static void Group(DrawingGroup g, Transform transform, Action<DrawingGroup> paint)
    {
        var sub = new DrawingGroup { Transform = transform };
        paint(sub);
        g.Children.Add(sub);
    }

    private static ImageSource Penguin(double t) => Draw("#38BDF8", g =>
    {
        Ellipse(g, "#E0F2FE", 50, 92, 34, 8);
        // Asa direita acenando.
        Group(g, new RotateTransform(-35 + 30 * Wave(t), 70, 58), w => Ellipse(w, "#0F172A", 80, 58, 7, 16));
        Ellipse(g, "#0F172A", 22, 64, 6, 14);
        Ellipse(g, "#0F172A", 50, 58, 27, 32);
        Ellipse(g, "#F8FAFC", 50, 64, 18, 24);
        Circle(g, "#FFFFFF", 41, 44, 6);
        Circle(g, "#FFFFFF", 59, 44, 6);
        Circle(g, "#0F172A", 42, 45, 3);
        Circle(g, "#0F172A", 58, 45, 3);
        Path(g, "#F59E0B", "M 44,52 L 56,52 L 50,60 Z");
        Ellipse(g, "#F59E0B", 40, 90, 7, 3);
        Ellipse(g, "#F59E0B", 60, 90, 7, 3);
        Ellipse(g, "#F9A8D4", 35, 54, 4, 2.5, 0.8);
        Ellipse(g, "#F9A8D4", 65, 54, 4, 2.5, 0.8);
    });

    private static ImageSource FloatingGhost(double t) => Draw("#312E81", g =>
    {
        foreach (var (x, y) in new[] { (18.0, 20.0), (82.0, 26.0), (14.0, 70.0), (86.0, 74.0) })
            Circle(g, "#FFFFFF", x, y, 1.2 + 0.6 * (Wave(t, x / 100) + 1) / 2);
        var dy = 5 * Wave(t);
        Ellipse(g, "#1E1B4B", 50, 92, 18 - dy, 3.5, 0.6);
        var blink = t is > 0.55 and < 0.62;
        Group(g, new TranslateTransform(0, dy), b =>
        {
            Path(b, "#F8FAFC", "M 26,80 L 26,46 C 26,28 38,20 50,20 C 62,20 74,28 74,46 L 74,80 Q 70,74 66,80 Q 62,86 58,80 Q 54,74 50,80 Q 46,86 42,80 Q 38,74 34,80 Q 30,86 26,80 Z");
            if (blink)
            {
                Rect(b, "#1E1B4B", 37, 47, 9, 2, 1);
                Rect(b, "#1E1B4B", 54, 47, 9, 2, 1);
            }
            else
            {
                Ellipse(b, "#1E1B4B", 41, 47, 4, 5.5);
                Ellipse(b, "#1E1B4B", 59, 47, 4, 5.5);
                Circle(b, "#FFFFFF", 42.5, 45, 1.5);
                Circle(b, "#FFFFFF", 60.5, 45, 1.5);
            }
            Ellipse(b, "#F9A8D4", 34, 57, 4.5, 2.6, 0.8);
            Ellipse(b, "#F9A8D4", 66, 57, 4.5, 2.6, 0.8);
            Path(b, null, "M 45,60 Q 50,65 55,60", 1, "#1E1B4B", 2.2);
        });
    });

    private static ImageSource Rocket(double t) => Draw("#0B1026", g =>
    {
        // Estrelas descendo (sensação de subida).
        foreach (var (x, y0) in new[] { (16.0, 0.0), (30.0, 0.35), (76.0, 0.15), (86.0, 0.6), (60.0, 0.8), (22.0, 0.7) })
        {
            var y = (y0 + t) % 1 * 110 - 5;
            Rect(g, "#FFFFFF", x, y, 1.4, 5, 0.7);
        }
        var shake = 1.2 * Wave(t * 4);
        Group(g, new TranslateTransform(shake, 0), r =>
        {
            var flame = 10 + 5 * (Wave(t * 3) + 1) / 2;
            Path(r, "#F97316", FormattableString.Invariant($"M 42,72 Q 50,{72 + flame * 2.2:0.##} 58,72 Z"));
            Path(r, "#FDE047", FormattableString.Invariant($"M 45,72 Q 50,{72 + flame * 1.4:0.##} 55,72 Z"));
            Path(r, "#EF4444", "M 36,70 L 40,54 L 40,72 Z");
            Path(r, "#EF4444", "M 64,70 L 60,54 L 60,72 Z");
            Path(r, "#F1F5F9", "M 50,14 C 62,24 62,44 60,72 L 40,72 C 38,44 38,24 50,14 Z");
            Path(r, "#EF4444", "M 50,14 C 56,19 59,25 60,31 L 40,31 C 41,25 44,19 50,14 Z");
            Circle(r, "#334155", 50, 46, 7);
            Circle(r, "#38BDF8", 50, 46, 5);
            Circle(r, "#FFFFFF", 48, 44, 1.5);
        });
    });

    private static ImageSource Planet(double t) => Draw("#0F172A", g =>
    {
        foreach (var (x, y) in new[] { (14.0, 18.0), (84.0, 14.0), (88.0, 80.0), (12.0, 82.0), (70.0, 90.0) })
            Circle(g, "#FFFFFF", x, y, 1.1);
        var a = t * 2 * Math.PI;
        var mx = 50 + 36 * Math.Cos(a);
        var my = 50 + 13 * Math.Sin(a);
        var behind = Math.Sin(a) < 0;
        if (behind) Circle(g, "#CBD5E1", mx, my, 5);
        Path(g, null, "M 14,56 C 14,44 86,44 86,56", 1, "#FBBF24", 3);
        Circle(g, "#A855F7", 50, 50, 22);
        Path(g, "#C084FC", "M 32,42 Q 50,36 68,42 L 70,48 Q 50,42 30,48 Z", 0.8);
        Path(g, "#7E22CE", "M 30,56 Q 50,62 70,56 L 68,62 Q 50,66 32,62 Z", 0.7);
        Circle(g, "#0F172A", 43, 50, 2.2);
        Circle(g, "#0F172A", 57, 50, 2.2);
        Path(g, null, "M 46,56 Q 50,59 54,56", 1, "#0F172A", 1.8);
        Path(g, null, "M 14,56 C 14,68 86,68 86,56", 1, "#FBBF24", 3);
        if (!behind) Circle(g, "#E2E8F0", mx, my, 5);
    });

    private static ImageSource BlinkRobot(double t) => Draw("#6366F1", g =>
    {
        var glow = 0.4 + 0.6 * (Wave(t) + 1) / 2;
        Line(g, "#475569", 3, 50, 30, 50, 16);
        Ellipse(g, "#FDE047", 50, 14, 7 * glow + 2, 7 * glow + 2, 0.35);
        Circle(g, "#FDE047", 50, 14, 4.5);
        Rect(g, "#94A3B8", 14, 46, 8, 16, 3);
        Rect(g, "#94A3B8", 78, 46, 8, 16, 3);
        Rect(g, "#F1F5F9", 21, 30, 58, 50, 16);
        Rect(g, "#1E293B", 28, 38, 44, 26, 10);
        var blink = t is > 0.8 and < 0.9;
        var look = 3 * Wave(t * 0.5);
        if (blink)
        {
            Rect(g, "#34D399", 34, 50, 12, 3, 1.5);
            Rect(g, "#34D399", 54, 50, 12, 3, 1.5);
        }
        else
        {
            Rect(g, "#34D399", 34 + look, 44, 12, 14, 5);
            Rect(g, "#34D399", 54 + look, 44, 12, 14, 5);
        }
        Path(g, null, "M 40,71 Q 50,77 60,71", 1, "#6366F1", 3);
    });

    private static ImageSource SleepyCat(double t) => Draw("#1E3A8A", g =>
    {
        Circle(g, "#FDE68A", 80, 20, 8);
        Circle(g, "#1E3A8A", 84, 17, 7);
        var breathe = 1 + 0.04 * Wave(t);
        Group(g, new ScaleTransform(breathe, breathe, 50, 80), c =>
        {
            Path(c, "#FB923C", "M 74,80 C 92,78 92,60 82,58 C 88,64 86,74 72,74 Z");
            Ellipse(c, "#FB923C", 52, 72, 30, 16);
            Path(c, "#FB923C", "M 24,58 L 28,40 L 38,52 Z");
            Path(c, "#FB923C", "M 50,58 L 46,40 L 36,52 Z");
            Circle(c, "#FB923C", 37, 62, 15);
            Path(c, null, "M 28,62 Q 32,65 35,62", 1, "#7C2D12", 1.8);
            Path(c, null, "M 39,62 Q 43,65 46,62", 1, "#7C2D12", 1.8);
            Path(c, "#F472B6", "M 35,67 L 39,67 L 37,69 Z");
        });
        // "Zzz" subindo e sumindo.
        for (var i = 0; i < 2; i++)
        {
            var p = (t + i * 0.5) % 1;
            var x = 56 + p * 14;
            var y = 42 - p * 26;
            var size = 5 + p * 4;
            Group(g, new TranslateTransform(x, y), z =>
                z.Children.Add(new DrawingGroup
                {
                    Opacity = 1 - p,
                    Children = { new GeometryDrawing(null, new Pen(Brush("#FFFFFF"), 1.8) { LineJoin = PenLineJoin.Round },
                        Geometry.Parse(FormattableString.Invariant($"M 0,0 L {size:0.##},0 L 0,{size:0.##} L {size:0.##},{size:0.##}"))) },
                }));
        }
    });

    private static ImageSource Slime(double t) => Draw("#FCE7F3", g =>
    {
        // Pula: sobe e desce, achata ao tocar o chão e estica no ar.
        var h = Math.Abs(Math.Sin(t * Math.PI));
        var y = -26 * h;
        var squash = h < 0.15 ? 1 - (0.15 - h) * 1.6 : 1 + 0.08 * h;
        var sx = 1 / squash;
        Ellipse(g, "#F9A8D4", 50, 88, 24 * (1 - 0.5 * h), 4, 0.7);
        Group(g, new TransformGroup { Children = { new ScaleTransform(sx, squash, 50, 86), new TranslateTransform(0, y) } }, s =>
        {
            Path(s, "#22C55E", "M 22,86 C 20,60 34,42 50,42 C 66,42 80,60 78,86 Z");
            Path(s, "#86EFAC", "M 30,62 C 32,52 40,48 46,48 C 40,52 36,56 34,64 Z", 0.9);
            Circle(s, "#14532D", 42, 64, 3.6);
            Circle(s, "#14532D", 58, 64, 3.6);
            Circle(s, "#FFFFFF", 43, 63, 1.2);
            Circle(s, "#FFFFFF", 59, 63, 1.2);
            Path(s, null, "M 46,72 Q 50,76 54,72", 1, "#14532D", 2);
        });
    });

    private static ImageSource Sun(double t) => Draw("#38BDF8", g =>
    {
        Group(g, new RotateTransform(t * 45, 50, 50), r =>
        {
            for (var i = 0; i < 12; i++)
            {
                var a = i * Math.PI / 6;
                var len = i % 2 == 0 ? 42 : 36;
                Line(r, "#FDE047", 5, 50 + 28 * Math.Cos(a), 50 + 28 * Math.Sin(a), 50 + len * Math.Cos(a), 50 + len * Math.Sin(a));
            }
        });
        var pulse = 22 + 1.2 * Wave(t * 2);
        Circle(g, "#FACC15", 50, 50, pulse);
        Circle(g, "#FDE047", 50, 50, pulse - 4);
        Path(g, null, "M 38,48 Q 42,44 46,48", 1, "#92400E", 2.2);
        Path(g, null, "M 54,48 Q 58,44 62,48", 1, "#92400E", 2.2);
        Path(g, null, "M 42,57 Q 50,64 58,57", 1, "#92400E", 2.4);
        Ellipse(g, "#FB923C", 37, 55, 4, 2.5, 0.7);
        Ellipse(g, "#FB923C", 63, 55, 4, 2.5, 0.7);
    });

    private static ImageSource Heart(double t) => Draw("#FFE4E6", g =>
    {
        // Batida dupla: tum-tum, pausa.
        var beat = t < 0.12 ? Math.Sin(t / 0.12 * Math.PI) : t is > 0.2 and < 0.32 ? 0.7 * Math.Sin((t - 0.2) / 0.12 * Math.PI) : 0;
        var s = 1 + 0.14 * beat;
        Group(g, new ScaleTransform(s, s, 50, 54), h =>
        {
            Path(h, "#F43F5E", "M 50,82 C 22,62 16,46 24,34 C 32,24 46,26 50,38 C 54,26 68,24 76,34 C 84,46 78,62 50,82 Z");
            Path(h, "#FDA4AF", "M 30,40 C 32,34 38,32 42,34 C 36,36 34,40 32,46 Z", 0.9);
            Circle(h, "#4C0519", 42, 52, 3);
            Circle(h, "#4C0519", 58, 52, 3);
            Path(h, null, "M 46,59 Q 50,63 54,59", 1, "#4C0519", 2);
        });
    });

    private static ImageSource Fish(double t) => Draw("#0EA5E9", g =>
    {
        // Bolhas subindo.
        for (var i = 0; i < 3; i++)
        {
            var p = (t + i / 3.0) % 1;
            Circle(g, "#E0F2FE", 26 + 6 * Wave(p, i * 0.3), 80 - p * 70, 2 + i * 0.8);
        }
        Path(g, "#0369A1", "M 0,92 Q 20,84 40,92 T 80,92 T 120,92 L 120,100 L 0,100 Z", 0.6);
        var dy = 3 * Wave(t);
        var tail = 10 * Wave(t * 2);
        Group(g, new TranslateTransform(0, dy), f =>
        {
            Group(f, new RotateTransform(tail, 70, 52), tl => Path(tl, "#F97316", "M 68,52 L 88,38 Q 84,52 88,66 Z"));
            Ellipse(f, "#FB923C", 50, 52, 24, 17);
            Path(f, "#FDBA74", "M 42,38 Q 50,28 60,38 Z");
            Path(f, "#FFFFFF", "M 52,37 Q 56,52 52,67 L 48,67 Q 52,52 48,37 Z", 0.8);
            Circle(f, "#FFFFFF", 38, 48, 5.5);
            Circle(f, "#0F172A", 37, 48, 3);
            Circle(f, "#FFFFFF", 36, 46.5, 1);
            Path(f, null, "M 28,56 Q 31,59 34,56", 1, "#7C2D12", 1.8);
        });
    });

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
