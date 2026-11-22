using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Assistente.App.Services;
using Assistente.App.Tools;

namespace Assistente.App;

/// <summary>
/// Janela de configurações (separada do painel): geral, aparência e temas,
/// avatar, posição, chat de IA e atualizações.
/// </summary>
public sealed class SettingsWindow : Window
{
    private const string ReleasesUrl = "https://github.com/LeonardoSbrolini/os-assistant/releases";

    private static SettingsWindow? _instance;

    private readonly MainWindow _main;
    private readonly AppSettings _settings;
    private readonly ListBox _nav = new() { Padding = new Thickness(0, 6, 0, 6) };
    private readonly ContentControl _host = new() { Focusable = false };
    private readonly Dictionary<string, Func<FrameworkElement>> _pages = new();
    private readonly Dictionary<string, FrameworkElement> _built = new();

    /// <summary>Abre a janela (ou traz para frente, se já estiver aberta).</summary>
    public static void Open(string? page = null)
    {
        if (_instance is null)
        {
            _instance = new SettingsWindow();
            _instance.Closed += (_, _) => _instance = null;
            _instance.Show();
        }
        if (_instance.WindowState == WindowState.Minimized) _instance.WindowState = WindowState.Normal;
        _instance.Activate();
        if (page is not null) _instance.Navigate(page);
    }

    private SettingsWindow()
    {
        _main = MainWindow.Current!;
        _settings = _main.Settings;

        Title = "Configurações do Assistente";
        Width = 820;
        Height = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        SetResourceReference(ForegroundProperty, "TextBrush");
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.ico"));

        _pages["Geral"] = GeneralPage;
        _pages["Aparência"] = AppearancePage;
        _pages["Avatar"] = AvatarPage;
        _pages["Posição"] = PositionPage;
        _pages["Barra de tarefas"] = TaskbarPage;
        _pages["Dock"] = DockPage;
        _pages["Chat IA"] = ChatPage;
        _pages["Atualizações"] = UpdatesPage;

        var glyphs = new Dictionary<string, string>
        {
            ["Geral"] = "", ["Aparência"] = "", ["Avatar"] = "",
            ["Barra de tarefas"] = "\uE737", ["Dock"] = "\uE7C4", ["Posição"] = "", ["Chat IA"] = "", ["Atualizações"] = "",
        };
        foreach (var name in _pages.Keys)
        {
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            label.Children.Add(new TextBlock
            {
                Text = glyphs[name],
                FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
                FontSize = 15,
                Width = 28,
                VerticalAlignment = VerticalAlignment.Center,
            });
            label.Children.Add(new TextBlock { Text = name, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            _nav.Items.Add(new ListBoxItem { Tag = name, Content = label });
        }
        _nav.SelectionChanged += (_, _) =>
        {
            if (_nav.SelectedItem is ListBoxItem { Tag: string page }) ShowPage(page);
        };

        Content = BuildChrome();
        _nav.SelectedIndex = 0;

        Loaded += (_, _) =>
        {
            var scale = new ScaleTransform(0.96, 0.96);
            var root = (FrameworkElement)Content;
            root.RenderTransformOrigin = new Point(0.5, 0.5);
            root.RenderTransform = scale;
            root.BeginAnimation(OpacityProperty, Anim.FromTo(0, 1, 220));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.FromTo(0.96, 1, 260));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.FromTo(0.96, 1, 260));
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
    }

    private void Navigate(string page)
    {
        var item = _nav.Items.OfType<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == page);
        if (item is not null) _nav.SelectedItem = item;
    }

    private FrameworkElement BuildChrome()
    {
        // Barra de título própria (arrastar para mover, botão fechar).
        var title = new DockPanel { Height = 46, Margin = new Thickness(16, 0, 8, 0), Background = Brushes.Transparent };
        title.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        var close = new Button { Style = (Style)Application.Current.Resources["IconButton"], Content = "", ToolTip = "Fechar" };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        title.Children.Add(close);
        var icon = new TextBlock
        {
            Text = "",
            FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
            FontSize = 16,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        title.Children.Add(icon);
        title.Children.Add(new TextBlock { Text = "Configurações", FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });

        var sidebar = new Border
        {
            Width = 190,
            Background = Ui.Res("SidebarBrush"),
            CornerRadius = new CornerRadius(0, 0, 0, 14),
            Child = _nav,
        };

        var body = new DockPanel();
        DockPanel.SetDock(title, Dock.Top);
        body.Children.Add(title);
        DockPanel.SetDock(sidebar, Dock.Left);
        body.Children.Add(sidebar);
        _host.Margin = new Thickness(20, 4, 16, 16);
        body.Children.Add(_host);

        var root = new Border
        {
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Child = body,
            Margin = new Thickness(10), // espaço para a sombra
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = 0.45 },
        };
        root.SetResourceReference(Border.BackgroundProperty, "WindowBrush");
        root.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        return root;
    }

    private void ShowPage(string page)
    {
        if (!_built.TryGetValue(page, out var view))
        {
            view = _pages[page]();
            _built[page] = view;
        }
        _host.Content = view;
        Anim.EnterView(view);
    }

    private static FrameworkElement Page(string title, params UIElement[] content)
    {
        var all = new List<UIElement> { Ui.Title(title) };
        all.AddRange(content);
        return new UserControl { Content = Ui.Scroll(Ui.Stack(all.ToArray())) };
    }

    // ---------- Páginas ----------

    private FrameworkElement GeneralPage()
    {
        var startup = Ui.Check("Iniciar o Assistente junto com o Windows", StartupManager.IsEnabled);
        startup.FontSize = 14;
        startup.Checked += (_, _) => StartupManager.SetEnabled(true);
        startup.Unchecked += (_, _) => StartupManager.SetEnabled(false);

        var delay = SliderRow("Tempo para recolher depois que o mouse sai", 150, 2000, _settings.CloseDelayMs,
            v => $"{v / 1000:0.0} s", v => _settings.CloseDelayMs = (int)v);

        var music = Ui.Check("Widget de música à direita do avatar (quando algo estiver tocando)", _settings.MusicWidget);
        music.FontSize = 14;
        music.Margin = new Thickness(0, 0, 0, 10);
        music.Click += (_, _) => { _settings.MusicWidget = music.IsChecked == true; _settings.Save(); Widgets.WidgetManager.Refresh(); };
        var avatarHide = Ui.Check("Esconder o avatar quando uma janela estiver embaixo dele (volta ao levar o mouse até o topo)", _settings.AvatarAutoHide);
        avatarHide.FontSize = 14;
        avatarHide.Margin = new Thickness(0, 0, 0, 10);
        avatarHide.Click += (_, _) => { _settings.AvatarAutoHide = avatarHide.IsChecked == true; _settings.Save(); };
        var time = Ui.Check("Widget de timer e cronômetro à esquerda do avatar", _settings.TimeWidget);
        time.FontSize = 14;
        time.Click += (_, _) => { _settings.TimeWidget = time.IsChecked == true; _settings.Save(); Widgets.WidgetManager.Refresh(); };

        return Page("Geral",
            Ui.Card("Inicialização", "", startup,
                Ui.Caption("Quando ligado, o Assistente abre sozinho assim que você entra no Windows.")),
            Ui.Card("Avatar e mini widgets", "\uE8A9", avatarHide, music, time,
                Ui.Caption("Aparecem ao lado do avatar com o painel fechado, para controlar a música e o timer sem abrir o Assistente.")),
            Ui.Card("Comportamento", "", delay));
    }

    private FrameworkElement AppearancePage()
    {
        var themes = new WrapPanel();
        void BuildThemes()
        {
            themes.Children.Clear();
            foreach (var (name, hex) in ThemeService.Themes)
            {
                var selected = name == _settings.Theme;
                var swatch = new Border
                {
                    Width = 96,
                    Height = 54,
                    CornerRadius = new CornerRadius(10),
                    Background = new SolidColorBrush(ThemeService.ParseColor(hex, Colors.Black)),
                    BorderThickness = new Thickness(selected ? 2 : 1),
                    Margin = new Thickness(0, 0, 8, 8),
                    Cursor = Cursors.Hand,
                    Child = new TextBlock
                    {
                        Text = name,
                        FontSize = 12,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(0, 0, 0, 6),
                    },
                };
                if (selected) swatch.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                else swatch.BorderBrush = Ui.Res("BorderBrush");
                var themeName = name;
                swatch.MouseLeftButtonUp += (_, _) =>
                {
                    Change(() => _settings.Theme = themeName);
                    BuildThemes();
                };
                swatch.MouseEnter += (_, _) => Anim.Pop(swatch);
                themes.Children.Add(swatch);
            }
        }
        BuildThemes();

        var accents = new WrapPanel();
        void BuildAccents()
        {
            accents.Children.Clear();
            var current = ThemeService.ParseColor(_settings.AccentColor, Colors.DodgerBlue);
            foreach (var (name, hex) in ThemeService.AccentPresets)
            {
                var color = ThemeService.ParseColor(hex, Colors.DodgerBlue);
                var swatch = new Border
                {
                    Width = 32,
                    Height = 32,
                    CornerRadius = new CornerRadius(16),
                    Background = new SolidColorBrush(color),
                    BorderBrush = Brushes.White,
                    BorderThickness = new Thickness(color == current ? 3 : 0),
                    Margin = new Thickness(0, 0, 8, 4),
                    Cursor = Cursors.Hand,
                    ToolTip = name,
                };
                var accentHex = hex;
                swatch.MouseLeftButtonUp += (_, _) =>
                {
                    Change(() => _settings.AccentColor = accentHex);
                    BuildAccents();
                };
                swatch.MouseEnter += (_, _) => Anim.Pop(swatch);
                accents.Children.Add(swatch);
            }
        }
        BuildAccents();

        var panelOpacity = SliderRow("Transparência do painel", 60, 100, _settings.PanelOpacity * 100,
            v => $"{100 - v:0}% transparente", v => _settings.PanelOpacity = v / 100, live: true);
        var windowOpacity = SliderRow("Transparência das janelas (como esta)", 60, 100, _settings.WindowOpacity * 100,
            v => $"{100 - v:0}% transparente", v => _settings.WindowOpacity = v / 100, live: true);
        var size = SliderRow("Tamanho do painel (em relação à tela)", 30, 50, _settings.PanelSize * 100,
            v => $"{v:0}%", v => _settings.PanelSize = v / 100);

        var animations = Ui.Check("Animações", _settings.Animations);
        animations.Checked += (_, _) => Change(() => _settings.Animations = true);
        animations.Unchecked += (_, _) => Change(() => _settings.Animations = false);

        return Page("Aparência",
            Ui.Card("Tema", "", themes),
            Ui.Card("Cor de destaque", "", accents),
            Ui.Card("Transparência", "", panelOpacity, windowOpacity),
            Ui.Card("Painel", "", size, animations));
    }

    private FrameworkElement AvatarPage()
    {
        var grid = new WrapPanel();
        void Build()
        {
            grid.Children.Clear();
            grid.Children.Add(AvatarOption(null, "Setinha", "Padrão", null));
            foreach (var avatar in Avatars.All)
                grid.Children.Add(AvatarOption(avatar.Id, avatar.Name, avatar.Inspiration, avatar.Image));
        }

        FrameworkElement AvatarOption(string? id, string name, string hint, ImageSource? image)
        {
            var selected = (_settings.Avatar ?? "") == (id ?? "");
            var picture = image is null
                ? (UIElement)new Border
                {
                    Width = 64,
                    Height = 64,
                    CornerRadius = new CornerRadius(32),
                    Background = Ui.Res("AccentBrush"),
                    Child = new TextBlock
                    {
                        Text = "",
                        FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
                        Foreground = Brushes.White,
                        FontSize = 18,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                }
                : new Image { Source = image, Width = 64, Height = 64 };

            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(picture);
            stack.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) });
            var caption = Ui.Caption(hint);
            caption.HorizontalAlignment = HorizontalAlignment.Center;
            caption.FontSize = 11;
            stack.Children.Add(caption);

            var card = new Border
            {
                Width = 112,
                Padding = new Thickness(8, 10, 8, 8),
                Margin = new Thickness(0, 0, 10, 10),
                CornerRadius = new CornerRadius(12),
                Background = Ui.Res("CardBrush"),
                BorderThickness = new Thickness(2),
                Cursor = Cursors.Hand,
                Child = stack,
            };
            if (selected) card.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            else card.BorderBrush = Brushes.Transparent;

            card.MouseEnter += (_, _) => Anim.Pop(picture as FrameworkElement ?? card);
            card.MouseLeftButtonUp += (_, _) =>
            {
                Change(() => _settings.Avatar = id);
                Build();
            };
            return card;
        }

        Build();
        return Page("Avatar",
            Ui.Caption("Escolha o personagem que fica na borda da tela no lugar da setinha. Todos são desenhos originais, inspirados em estilos de desenhos, séries e jogos."),
            grid);
    }

    private FrameworkElement PositionPage()
    {
        var top = Ui.Radio("Topo", "edge", _settings.Edge == DockEdge.Top);
        var right = Ui.Radio("Direita", "edge", _settings.Edge == DockEdge.Right);
        var left = Ui.Radio("Esquerda", "edge", _settings.Edge == DockEdge.Left);
        top.Checked += (_, _) => Change(() => _settings.Edge = DockEdge.Top);
        right.Checked += (_, _) => Change(() => _settings.Edge = DockEdge.Right);
        left.Checked += (_, _) => Change(() => _settings.Edge = DockEdge.Left);

        var position = SliderRow("Posição ao longo da borda", 0, 100, _settings.Position * 100,
            v => v < 35 ? "Início" : v > 65 ? "Fim" : "Centro", v => _settings.Position = v / 100);

        return Page("Posição",
            Ui.Card("Borda da tela", "", Ui.Inline(top, right, left), position));
    }

    private FrameworkElement TaskbarPage()
    {
        var status = Ui.Caption("");
        var list = new StackPanel();
        var boxes = new List<(CheckBox Box, TaskbarStyle.Option Option)>();
        foreach (var option in TaskbarStyle.Options)
        {
            var opt = option;
            var box = Ui.Check(opt.Label, TaskbarStyle.IsMac(opt));
            box.FontSize = 14;
            box.Margin = new Thickness(0, 0, 0, 10);
            box.Click += (_, _) =>
            {
                var ok = TaskbarStyle.Set(_settings, opt, box.IsChecked == true);
                box.IsChecked = TaskbarStyle.IsMac(opt);
                status.Text = ok ? "Pronto. Se a barra não mudar, clique em \"Reiniciar a barra\"." : "O Windows não deixou mudar essa opção neste computador.";
            };
            boxes.Add((box, opt));
            list.Children.Add(box);
        }

        var autoHide = Ui.Check("Ocultar automaticamente (aparece ao levar o mouse para baixo, como no Mac)", TaskbarStyle.AutoHide);
        autoHide.FontSize = 14;
        autoHide.Click += (_, _) => TaskbarStyle.SetAutoHide(_settings, autoHide.IsChecked == true);

        void Sync()
        {
            foreach (var (box, opt) in boxes) box.IsChecked = TaskbarStyle.IsMac(opt);
            autoHide.IsChecked = TaskbarStyle.AutoHide;
        }

        var apply = Ui.Btn("Aplicar visual do Mac", () =>
        {
            var failed = TaskbarStyle.ApplyMacLook(_settings);
            Sync();
            status.Text = failed == 0 ? "Visual do Mac aplicado." : $"Aplicado (o Windows bloqueou {failed} opção(ões)).";
        }, primary: true);
        var restore = Ui.Btn("Voltar como era", () =>
        {
            TaskbarStyle.Restore(_settings);
            Sync();
            status.Text = "A barra voltou como estava antes.";
        });
        var restart = Ui.Btn("Reiniciar a barra", () =>
        {
            if (!Ui.Confirm("A barra de tarefas e as janelas do Explorador de Arquivos vão fechar e abrir de novo. Continuar?")) return;
            TaskbarStyle.RestartExplorer();
            status.Text = "Reiniciando a barra…";
        });

        return Page("Barra de tarefas",
            Ui.Card("Barra do Windows com cara de Mac", "\uE737", list, Ui.Inline(apply, restore, restart), status,
                Ui.Caption("Usa os ajustes oficiais do Windows (os mesmos de Configurações › Personalização › Barra de tarefas), " +
                           "então a barra continua funcionando normal: Wi‑Fi, volume, relógio e bandeja ficam onde sempre estiveram. " +
                           "O formato e a transparência da barra o Windows não deixa mudar sem programas que alteram o sistema.")),
            Ui.Card("Esconder", "\uE7B3", autoHide,
                Ui.Caption("A barra some quando você não está usando e volta ao encostar o mouse embaixo.")));
    }

    private FrameworkElement DockPage()
    {
        void DockChange(Action update)
        {
            update();
            _settings.Save();
            Docking.DockWindow.Apply(_settings);
        }

        CheckBox Toggle(string text, bool value, Action<bool> set)
        {
            var box = Ui.Check(text, value);
            box.FontSize = 14;
            box.Margin = new Thickness(0, 0, 0, 10);
            box.Click += (_, _) => DockChange(() => set(box.IsChecked == true));
            return box;
        }

        var enabled = Toggle("Mostrar o dock embaixo da tela", _settings.DockEnabled, v => _settings.DockEnabled = v);
        var hideTaskbar = Toggle("Com o dock ligado, esconder a barra do Windows (só o dock aparece embaixo)", _settings.DockHideTaskbar, v => _settings.DockHideTaskbar = v);
        var tray = Toggle("Wi‑Fi, volume, bateria e relógio no canto direito do dock", _settings.DockShowTray, v => _settings.DockShowTray = v);
        RadioButton Mode(string text, string value)
        {
            var radio = Ui.Radio(text, "dockhide", _settings.DockHideMode == value);
            radio.Checked += (_, _) => DockChange(() => _settings.DockHideMode = value);
            return radio;
        }
        var autoHide = new StackPanel();
        autoHide.Children.Add(Mode("Some quando uma janela encosta nele (como no Mac)", "janela"));
        autoHide.Children.Add(Mode("Sempre escondido (aparece ao levar o mouse para baixo)", "sempre"));
        autoHide.Children.Add(Mode("Sempre visível (janelas maximizadas param acima dele)", "nunca"));
        var running = Toggle("Mostrar também os programas abertos que não estão fixados", _settings.DockShowRunning, v => _settings.DockShowRunning = v);

        var size = SliderRow("Tamanho dos ícones", 32, 72, _settings.DockIconSize,
            v => $"{v:0} px", v => { _settings.DockIconSize = Math.Round(v); Docking.DockWindow.Apply(_settings); });
        var zoom = SliderRow("Zoom com o mouse em cima", 1, 2, _settings.DockMagnify,
            v => v < 1.05 ? "Sem zoom" : $"{v:0.0}×", v => { _settings.DockMagnify = Math.Round(v, 2); Docking.DockWindow.Apply(_settings); });

        var list = new StackPanel();
        void Fill()
        {
            list.Children.Clear();
            var pinned = _settings.DockPinned ??= Docking.DockWindow.DefaultPinned();
            for (var i = 0; i < pinned.Count; i++)
            {
                var index = i;
                var path = pinned[i];
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var icon = new Image { Source = Docking.Native.GetIcon(path), Width = 24, Height = 24, Margin = new Thickness(0, 0, 10, 0) };
                DockPanel.SetDock(icon, System.Windows.Controls.Dock.Left);
                row.Children.Add(icon);

                var buttons = new StackPanel { Orientation = Orientation.Horizontal };
                Button Small(string glyph, string tip, Action action)
                {
                    var b = new Button
                    {
                        Content = glyph,
                        FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
                        Width = 30,
                        Height = 30,
                        Padding = new Thickness(0),
                        Margin = new Thickness(4, 0, 0, 0),
                        ToolTip = tip,
                        Style = (Style)Application.Current.Resources["RoundButton"],
                        Tag = new CornerRadius(15),
                    };
                    b.Click += (_, _) => action();
                    return b;
                }
                void Move(int delta)
                {
                    var j = index + delta;
                    if (j < 0 || j >= pinned.Count) return;
                    (pinned[index], pinned[j]) = (pinned[j], pinned[index]);
                    DockChange(() => { });
                    Fill();
                }
                buttons.Children.Add(Small("\uE70E", "Mover para a esquerda", () => Move(-1)));
                buttons.Children.Add(Small("\uE70D", "Mover para a direita", () => Move(1)));
                buttons.Children.Add(Small("\uE711", "Remover do dock", () =>
                {
                    pinned.RemoveAt(index);
                    DockChange(() => { });
                    Fill();
                }));
                DockPanel.SetDock(buttons, System.Windows.Controls.Dock.Right);
                row.Children.Add(buttons);

                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                text.Children.Add(new TextBlock { Text = Docking.Native.DisplayName(path), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                var caption = Ui.Caption(path);
                caption.TextTrimming = TextTrimming.CharacterEllipsis;
                text.Children.Add(caption);
                row.Children.Add(text);
                list.Children.Add(row);
            }
            if (pinned.Count == 0) list.Children.Add(Ui.Caption("Nenhum programa fixado."));
        }
        Fill();

        var add = Ui.Btn("Adicionar programa…", () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Escolha um programa ou atalho",
                Filter = "Programas e atalhos (*.exe;*.lnk)|*.exe;*.lnk|Todos os arquivos (*.*)|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                DereferenceLinks = false,
            };
            if (dialog.ShowDialog(this) != true) return;
            (_settings.DockPinned ??= Docking.DockWindow.DefaultPinned()).Add(dialog.FileName);
            DockChange(() => { });
            Fill();
        }, primary: true);
        var reset = Ui.Btn("Restaurar padrão", () =>
        {
            _settings.DockPinned = Docking.DockWindow.DefaultPinned();
            DockChange(() => { });
            Fill();
        });

        return Page("Dock",
            Ui.Card("Dock estilo Mac", "\uE7C4", enabled,
                Ui.Caption("Barra embaixo e centralizada com os seus programas. Os ícones crescem quando o mouse passa, " +
                           "e uma bolinha mostra quais estão abertos. Clique para abrir ou alternar; botão direito para mais opções; " +
                           "botão do meio abre uma nova janela.")),
            Ui.Card("Barra do Windows", "\uE737", hideTaskbar,
                Ui.Caption("A barra volta sozinha se você desligar o dock ou fechar o Assistente. " +
                           "Botão direito no botão Iniciar do dock › \"Mostrar a barra do Windows\" também traz ela de volta.")),
            Ui.Card("Canto direito", "\uE701", tray,
                Ui.Caption("Clique no Wi‑Fi/volume/bateria para abrir as configurações rápidas do Windows, e no relógio para ver notificações e calendário.")),
            Ui.Card("Quando esconder o dock", "\uE713", autoHide, running,
                Ui.Caption("Escondido, ele volta quando você leva o mouse até a borda de baixo da tela.")),
            Ui.Card("Tamanho", "\uE740", size, zoom),
            Ui.Card("Programas fixados", "\uE718", list, Ui.Inline(add, reset),
                Ui.Caption("Dica: no próprio dock, clique com o botão direito num programa aberto e escolha \"Manter no dock\".")));
    }

    private FrameworkElement ChatPage()
    {
        var free = Ui.Radio("Gratuito, sem cadastro (Pollinations)", "chat", _settings.ChatProvider != "personalizado");
        var custom = Ui.Radio("Minha chave (qualquer serviço compatível com OpenAI)", "chat", _settings.ChatProvider == "personalizado");

        var url = Ui.Input(_settings.ChatBaseUrl);
        var model = Ui.Input(_settings.ChatModel);
        var key = new PasswordBox { Password = _settings.ChatApiKey ?? "", Padding = new Thickness(6, 5, 6, 5) };
        key.SetResourceReference(BackgroundProperty, "InputBrush");
        key.SetResourceReference(ForegroundProperty, "TextBrush");
        key.SetResourceReference(BorderBrushProperty, "BorderBrush");
        key.CaretBrush = Brushes.White;

        var customFields = Ui.Stack(
            Ui.Field("Endereço da API", url),
            Ui.Field("Modelo", model),
            Ui.Field("Chave da API (fica criptografada no seu computador)", key),
            Ui.Caption("Dica: a Groq (console.groq.com) e a OpenRouter (openrouter.ai) dão chaves gratuitas. " +
                       "O padrão já vem configurado para a Groq: é só colar a chave."));
        customFields.IsEnabled = custom.IsChecked == true;

        var status = Ui.Caption("");
        var save = Ui.Btn("Salvar", () =>
        {
            Change(() =>
            {
                _settings.ChatProvider = custom.IsChecked == true ? "personalizado" : "gratuito";
                _settings.ChatBaseUrl = url.Text.Trim();
                _settings.ChatModel = model.Text.Trim();
                _settings.ChatApiKey = key.Password;
            });
            status.Text = "Salvo!";
            Anim.Pop(status);
        }, primary: true);

        free.Checked += (_, _) => customFields.IsEnabled = false;
        custom.Checked += (_, _) => customFields.IsEnabled = true;

        return Page("Chat IA",
            Ui.Card("Serviço de IA", "", free,
                Ui.Caption("Funciona sem conta e sem chave, mas pode ficar lento ou limitado em horários de muito uso."),
                custom, customFields, Ui.Inline(save, status)));
    }

    private FrameworkElement UpdatesPage()
    {
        var version = new TextBlock { Text = $"Versão {UpdateService.CurrentVersionText}", FontSize = 22, FontWeight = FontWeights.SemiBold };
        var status = Ui.Caption(UpdateService.Instance.LastCheck is { } last ? $"Última verificação: {last:dd/MM HH:mm}" : "");
        Button? check = null;
        check = Ui.Btn("Buscar atualizações", async () =>
        {
            check!.IsEnabled = false;
            status.Text = "Procurando atualizações…";
            try
            {
                status.Text = await UpdateService.Instance.CheckAsync(manual: false);
            }
            finally
            {
                check.IsEnabled = true;
                Anim.Pop(status);
            }
        }, primary: true);

        var auto = Ui.Check("Buscar atualizações automaticamente (ao abrir e a cada 6 horas)", _settings.AutoUpdate);
        auto.Checked += (_, _) => Change(() => _settings.AutoUpdate = true);
        auto.Unchecked += (_, _) => Change(() => _settings.AutoUpdate = false);

        var notes = Ui.Btn("Ver novidades", () => Process.Start(new ProcessStartInfo(ReleasesUrl) { UseShellExecute = true }));

        return Page("Atualizações",
            Ui.Card("Assistente", "", version, Ui.Inline(check, notes), status, auto));
    }

    // ---------- Auxiliares ----------

    /// <summary>Salva e aplica na hora.</summary>
    private void Change(Action update)
    {
        update();
        _settings.Save();
        _main.ApplySettings();
    }

    /// <param name="live">Aplica enquanto arrasta (bom para transparência); senão, ao soltar.</param>
    private FrameworkElement SliderRow(string label, double min, double max, double value,
        Func<double, string> format, Action<double> apply, bool live = false)
    {
        var valueText = new TextBlock { FontWeight = FontWeights.SemiBold, MinWidth = 120, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), VerticalAlignment = VerticalAlignment.Center };
        valueText.Text = format(slider.Value);

        slider.ValueChanged += (_, e) =>
        {
            valueText.Text = format(e.NewValue);
            if (live && IsLoaded)
            {
                apply(e.NewValue);
                ThemeService.Apply(_settings);
            }
        };
        void Commit() => Change(() => apply(slider.Value));
        slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => Commit()));
        slider.PreviewMouseLeftButtonUp += (_, _) => Commit();
        slider.KeyUp += (_, _) => Commit();

        var row = new DockPanel();
        DockPanel.SetDock(valueText, Dock.Right);
        row.Children.Add(valueText);
        row.Children.Add(slider);
        return Ui.Field(label, row);
    }
}
