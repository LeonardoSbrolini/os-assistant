using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Assistente.App.Services;
using Assistente.App.Tools;
using Microsoft.Win32;

namespace Assistente.App.Desktop;

/// <summary>
/// Janela "Editar atalho": nome, endereço/caminho, imagem própria e cor do fundo, com uma
/// prévia do atalho. Abre pelo botão direito no widget ou pela página de configurações.
/// </summary>
internal static class ShortcutEditor
{
    /// <summary>Cores prontas para o fundo do atalho.</summary>
    private static readonly string[] Palette =
    {
        "#FF3B30", "#FF9500", "#FFCC00", "#34C759", "#00C7BE", "#30B0C7", "#0A84FF",
        "#5E5CE6", "#AF52DE", "#FF2D55", "#A2845E", "#8E8E93", "#1C1C1E", "#FFFFFF",
    };

    /// <summary>Abre o editor. Devolve true se a pessoa salvou.</summary>
    public static bool Edit(AppSettings settings, DesktopShortcut item)
    {
        string? color = item.Color;       // null = automática
        string? image = item.Image;       // imagem atual (já guardada) ou nova (ainda no lugar original)
        var saved = false;

        var name = Ui.Input(item.Name);
        var target = Ui.Input(item.Target);
        var muted = (Brush)Application.Current.Resources["MutedBrush"];

        // ---------- Prévia ----------
        const double size = 96;
        var previewFace = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(26) };
        var previewIcon = new Grid { Width = 60, Height = 60 };
        previewFace.Child = previewIcon;
        var previewLabel = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 140 };
        previewLabel.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(TextBox.Text)) { Source = name });
        var previewBox = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 16) };
        previewBox.Children.Add(previewFace);
        previewBox.Children.Add(previewLabel);
        var previewArea = new Border
        {
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(0, 18, 0, 6),
            Margin = new Thickness(0, 0, 0, 14),
            // Um "papel de parede" de fundo para ver como fica com e sem quadrado.
            Background = new LinearGradientBrush(Color.FromRgb(0x2B, 0x3A, 0x67), Color.FromRgb(0x7A, 0x4E, 0x9E), 30),
            Child = previewBox,
        };

        var imageInfo = new TextBlock { FontSize = 12, Foreground = muted, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 6, 0, 0) };
        var swatches = new WrapPanel();
        var version = 0;

        async void RenderPreview()
        {
            var mine = ++version;
            previewIcon.Children.Clear();
            var custom = DesktopWidgetWindow.ParseColor(color);
            var card = color != "nenhum";
            previewIcon.Width = previewIcon.Height = card ? 60 : 84;
            previewFace.BorderThickness = new Thickness(card ? 1 : 0);
            previewFace.BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF));
            previewFace.Background = custom is { } c
                ? new LinearGradientBrush(Lighten(c, 0.12), Lighten(c, -0.12), new Point(0.2, 0), new Point(0.8, 1))
                : card ? new LinearGradientBrush(Color.FromArgb(0x5A, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF), 90)
                       : Brushes.Transparent;

            imageInfo.Text = string.IsNullOrEmpty(image)
                ? "Usando o ícone normal do " + (item.Kind switch { "site" => "site.", "pasta" => "Windows.", _ => "programa." })
                : "Imagem própria: " + Path.GetFileName(image);

            ImageSource? source = IconLoader.LoadFile(image);
            if (source is null && string.IsNullOrEmpty(image))
            {
                switch (item.Kind)
                {
                    case "pasta":
                    case "app":
                        source = IconLoader.AppIcon(target.Text.Trim()) ?? Docking.Native.GetIcon(target.Text.Trim(), 256);
                        break;
                    case "site":
                        source = await DesktopWidget.FaviconAsync(DesktopWidget.NormalizeUrl(target.Text));
                        if (mine != version) return;
                        break;
                }
            }
            if (source is null)
            {
                previewIcon.Children.Add(new TextBlock
                {
                    Text = item.Kind == "pasta" ? "" : item.Kind == "site" ? "" : "",
                    FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
                    FontSize = card ? 44 : 60,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                return;
            }
            var img = new Image { Source = source, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            if (!string.IsNullOrEmpty(image) || !card)
                img.Clip = new RectangleGeometry(new Rect(0, 0, previewIcon.Width, previewIcon.Height), card ? 12 : 18, card ? 12 : 18);
            previewIcon.Children.Add(img);
        }

        void RenderSwatches()
        {
            swatches.Children.Clear();
            swatches.Children.Add(Swatch("Automática (cor do ícone)", null, color is null, () => { color = null; Changed(); }));
            swatches.Children.Add(Swatch("Sem fundo (só o ícone)", "nenhum", color == "nenhum", () => { color = "nenhum"; Changed(); }));
            foreach (var hex in Palette)
            {
                var h = hex;
                swatches.Children.Add(Swatch(h, h, string.Equals(color, h, StringComparison.OrdinalIgnoreCase), () => { color = h; Changed(); }));
            }
            var isOther = color is { } cur && cur != "nenhum" && Array.FindIndex(Palette, p => p.Equals(cur, StringComparison.OrdinalIgnoreCase)) < 0;
            swatches.Children.Add(Swatch("Outra cor…", isOther ? color : "outra", isOther, () =>
            {
                var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true };
                if (DesktopWidgetWindow.ParseColor(color) is { } now) dialog.Color = System.Drawing.Color.FromArgb(now.R, now.G, now.B);
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    color = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
                    Changed();
                }
            }));
        }

        void Changed()
        {
            RenderSwatches();
            RenderPreview();
        }

        // ---------- Imagem ----------
        var pick = Ui.Btn("Escolher imagem…", () =>
        {
            var dialog = new OpenFileDialog
            {
                Title = "Escolha uma imagem para o atalho",
                Filter = "Imagens|*.png;*.jpg;*.jpeg;*.ico;*.bmp;*.gif;*.webp;*.tif;*.tiff|Todos os arquivos|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            };
            if (dialog.ShowDialog() != true) return;
            if (IconLoader.LoadFile(dialog.FileName) is null)
            {
                imageInfo.Text = "Não deu para abrir essa imagem. Tente um PNG ou JPG.";
                return;
            }
            image = dialog.FileName;
            RenderPreview();
        }, primary: true);
        var reset = Ui.Btn("Usar o ícone normal", () =>
        {
            image = null;
            RenderPreview();
        });
        reset.Margin = new Thickness(8, 0, 0, 0);

        // ---------- Endereço / caminho ----------
        var targetLabel = item.Kind switch { "site" => "Endereço do site", "pasta" => "Pasta", _ => "Programa ou arquivo" };
        UIElement targetRow = target;
        if (item.Kind != "site")
        {
            var browse = Ui.Btn("Procurar…", () =>
            {
                if (item.Kind == "pasta")
                {
                    var d = new OpenFolderDialog { Title = "Escolha a pasta" };
                    if (d.ShowDialog() == true) target.Text = d.FolderName;
                }
                else
                {
                    var d = new OpenFileDialog
                    {
                        Title = "Escolha um aplicativo ou arquivo",
                        Filter = "Aplicativos e atalhos|*.exe;*.lnk;*.url;*.appref-ms;*.bat;*.cmd|Todos os arquivos|*.*",
                        DereferenceLinks = false,
                    };
                    if (d.ShowDialog() == true) target.Text = d.FileName;
                }
                RenderPreview();
            });
            browse.Margin = new Thickness(8, 0, 0, 0);
            var dock = new DockPanel();
            DockPanel.SetDock(browse, System.Windows.Controls.Dock.Right);
            dock.Children.Add(browse);
            dock.Children.Add(target);
            targetRow = dock;
        }
        target.LostFocus += (_, _) => RenderPreview();

        // ---------- Janela ----------
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "Editar atalho", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) });
        stack.Children.Add(previewArea);
        var nameField = Ui.Field("Nome", name);
        nameField.Margin = new Thickness(0, 0, 0, 10);
        stack.Children.Add(nameField);
        var targetField = Ui.Field(targetLabel, targetRow);
        targetField.Margin = new Thickness(0, 0, 0, 14);
        stack.Children.Add(targetField);
        stack.Children.Add(Heading("Imagem", muted));
        var imageButtons = new StackPanel { Orientation = Orientation.Horizontal };
        imageButtons.Children.Add(pick);
        imageButtons.Children.Add(reset);
        stack.Children.Add(imageButtons);
        stack.Children.Add(imageInfo);
        var colorHeading = Heading("Cor do fundo", muted);
        colorHeading.Margin = new Thickness(0, 16, 0, 6);
        stack.Children.Add(colorHeading);
        stack.Children.Add(swatches);

        var window = new Window
        {
            Title = "Editar atalho",
            Width = 470,
            SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            Topmost = true,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            UseLayoutRounding = true,
        };
        window.SetResourceReference(Control.ForegroundProperty, "TextBrush");

        void Save()
        {
            var newName = name.Text.Trim();
            var newTarget = target.Text.Trim();
            if (newName.Length == 0 || newTarget.Length == 0) return;
            item.Name = newName;
            item.Target = item.Kind == "site" ? DesktopWidget.NormalizeUrl(newTarget) : newTarget;
            item.Color = color;
            if (!string.Equals(image, item.Image, StringComparison.OrdinalIgnoreCase))
            {
                // Guarda uma cópia da imagem nova (o atalho não quebra se o arquivo original sumir).
                var stored = string.IsNullOrEmpty(image) ? null : IconLoader.ImportImage(image);
                IconLoader.DeleteImported(item.Image);
                item.Image = stored;
            }
            settings.Save();
            DesktopWidget.Refresh();
            saved = true;
            window.Close();
        }

        var ok = Ui.Btn("Salvar", Save, primary: true);
        var cancel = Ui.Btn("Cancelar", window.Close);
        ok.Margin = new Thickness(8, 0, 0, 0);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        stack.Children.Add(buttons);

        window.Content = Ui.LiftShadow(new Border
        {
            CornerRadius = new CornerRadius(20),
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x17)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(22),
            Margin = new Thickness(14),
            Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 4, Opacity = 0.55 },
            Child = stack,
        });
        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) window.Close();
            else if (e.Key == Key.Enter) Save();
        };
        window.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is not TextBox && e.ButtonState == MouseButtonState.Pressed) window.DragMove();
        };
        window.Loaded += (_, _) =>
        {
            window.Activate();
            name.Focus();
            name.SelectAll();
        };

        RenderSwatches();
        RenderPreview();
        window.ShowDialog();
        return saved;
    }

    private static TextBlock Heading(string text, Brush muted) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = muted,
        Margin = new Thickness(0, 0, 0, 6),
    };

    /// <summary>Bolinha de cor (ou de opção) com anel quando escolhida.</summary>
    private static FrameworkElement Swatch(string tooltip, string? value, bool selected, Action click)
    {
        var dot = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13) };
        switch (value)
        {
            case null:
                // Automática: degradê de cores.
                dot.Background = new LinearGradientBrush(new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(0xFF, 0x3B, 0x30), 0),
                    new GradientStop(Color.FromRgb(0xFF, 0xCC, 0x00), 0.35),
                    new GradientStop(Color.FromRgb(0x34, 0xC7, 0x59), 0.6),
                    new GradientStop(Color.FromRgb(0x0A, 0x84, 0xFF), 1),
                }, new Point(0, 0), new Point(1, 1));
                dot.Child = SwatchGlyph("A");
                break;
            case "nenhum":
                dot.Background = Brushes.Transparent;
                dot.BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF));
                dot.BorderThickness = new Thickness(1.5);
                dot.Child = new System.Windows.Shapes.Line
                {
                    X1 = 6, Y1 = 20, X2 = 20, Y2 = 6,
                    Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0x5A, 0x5A)),
                    StrokeThickness = 2,
                };
                break;
            case "outra":
                dot.Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
                dot.Child = SwatchGlyph("+");
                break;
            default:
                var c = DesktopWidgetWindow.ParseColor(value) ?? Colors.Gray;
                dot.Background = new SolidColorBrush(c);
                dot.BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
                dot.BorderThickness = new Thickness(1);
                break;
        }
        var ring = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(17),
            BorderThickness = new Thickness(2),
            BorderBrush = selected ? (Brush)Application.Current.Resources["AccentBrush"] : Brushes.Transparent,
            Background = Brushes.Transparent,
            Child = dot,
            Margin = new Thickness(0, 0, 6, 6),
            Cursor = Cursors.Hand,
            ToolTip = tooltip,
        };
        ring.MouseLeftButtonUp += (_, _) => click();
        return ring;
    }

    private static TextBlock SwatchGlyph(string text) => new()
    {
        Text = text,
        FontSize = 13,
        FontWeight = FontWeights.Bold,
        Foreground = Brushes.White,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Color Lighten(Color c, double amount)
    {
        static byte Mix(byte v, double a) => (byte)Math.Clamp(a >= 0 ? v + (255 - v) * a : v * (1 + a), 0, 255);
        return Color.FromRgb(Mix(c.R, amount), Mix(c.G, amount), Mix(c.B, amount));
    }
}
