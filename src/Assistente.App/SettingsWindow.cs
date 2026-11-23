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
/// Janela de configurações no mesmo estilo da ilha: fundo preto, cantos bem arredondados,
/// bolinhas de janela estilo Mac e um menu lateral agrupado por seções.
/// </summary>
public sealed class SettingsWindow : Window
{
    private const string ReleasesUrl = "https://github.com/LeonardoSbrolini/os-assistant/releases";

    private static SettingsWindow? _instance;

    private readonly MainWindow _main;
    private readonly AppSettings _settings;
    private readonly StackPanel _nav = new() { Margin = new Thickness(10, 0, 10, 12) };
    private readonly Dictionary<string, Border> _navRows = new();
    private readonly ContentControl _host = new() { Focusable = false };
    private readonly Dictionary<string, Func<FrameworkElement>> _pages = new();
    private readonly Dictionary<string, FrameworkElement> _built = new();
    private string? _current;

    private static readonly Dictionary<string, string> Subtitles = new()
    {
        ["Geral"] = "O básico: iniciar com o Windows e o tempo para recolher.",
        ["Ilha e menus"] = "A ilha do topo, os menus dos cantos e o menu em círculo.",
        ["Barra de menus"] = "A barra estilo Mac ao lado da ilha: escolha o que aparece de cada lado.",
        ["Tela de bloqueio"] = "Deixe a tela de bloqueio do Windows limpa, com cara de Mac.",
        ["Dock"] = "A barra de aplicativos que aparece embaixo.",
        ["Barra de tarefas"] = "Como a barra do Windows se comporta.",
        ["Aparência"] = "Cores, tema e transparência.",
        ["Avatar"] = "O personagem do Assistente.",
        ["Posição"] = "Onde o Assistente fica na tela.",
        ["Chat IA"] = "Conecte uma IA para conversar com o Assistente.",
        ["Atualizações"] = "Versão instalada e novidades.",
    };

    private static readonly Brush WindowBlack = Frozen(new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0D)));
    private static readonly Brush SidebarBlack = Frozen(new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x17)));
    private static readonly Brush RowHover = Frozen(new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush RowSelected = Frozen(new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush Muted = Frozen(new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x96)));

    private static Brush Frozen(Brush b)
    {
        b.Freeze();
        return b;
    }

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
        Width = 860;
        Height = 620;
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
        _pages["Ilha e menus"] = IslandPage;
        _pages["Barra de menus"] = TopBarPage;
        _pages["Tela de bloqueio"] = LockScreenPage;
        _pages["Dock"] = DockPage;
        _pages["Barra de tarefas"] = TaskbarPage;
        _pages["Aparência"] = AppearancePage;
        _pages["Avatar"] = AvatarPage;
        _pages["Posição"] = PositionPage;
        _pages["Chat IA"] = ChatPage;
        _pages["Atualizações"] = UpdatesPage;

        NavRow("Geral", "");
        NavSection("Topo e menus");
        NavRow("Ilha e menus", "");
        NavRow("Barra de menus", "\uE700");
        NavRow("Dock", "");
        NavRow("Barra de tarefas", "");
        NavSection("Visual");
        NavRow("Aparência", "");
        NavRow("Avatar", "");
        NavRow("Posição", "");
        NavRow("Tela de bloqueio", "\uE72E");
        NavSection("Mais");
        NavRow("Chat IA", "");
        NavRow("Atualizações", "");

        Content = BuildChrome();
        ShowPage("Geral");

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
        if (_pages.ContainsKey(page)) ShowPage(page);
    }

    // ---------- Menu lateral ----------

    private void NavSection(string text)
    {
        _nav.Children.Add(new TextBlock
        {
            Text = text.ToUpperInvariant(),
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Muted,
            Margin = new Thickness(10, 16, 0, 6),
        });
    }

    private void NavRow(string page, string glyph)
    {
        var icon = new TextBlock
        {
            Text = glyph,
            FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
            FontSize = 14,
            Width = 22,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        var label = new TextBlock { Text = page, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center };
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(icon);
        content.Children.Add(label);

        var row = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 1, 0, 1),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = content,
        };
        row.MouseEnter += (_, _) => { if (_current != page) row.Background = RowHover; };
        row.MouseLeave += (_, _) => { if (_current != page) row.Background = Brushes.Transparent; };
        row.MouseLeftButtonUp += (_, _) => ShowPage(page);
        _navRows[page] = row;
        _nav.Children.Add(row);
    }

    private void MarkSelected(string page)
    {
        foreach (var (name, row) in _navRows)
        {
            row.Background = name == page ? RowSelected : Brushes.Transparent;
            if (row.Child is StackPanel { Children.Count: 2 } sp && sp.Children[1] is TextBlock t)
                t.FontWeight = name == page ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private static Border Dot(Color color, string tip, Action click)
    {
        var dot = new Border
        {
            Width = 13,
            Height = 13,
            CornerRadius = new CornerRadius(6.5),
            Background = new SolidColorBrush(color),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand,
            ToolTip = tip,
        };
        // O "apertar" é consumido aqui para a área de arrastar a janela não roubar o clique.
        dot.MouseLeftButtonDown += (_, e) => e.Handled = true;
        dot.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        dot.MouseEnter += (_, _) => dot.Opacity = 0.75;
        dot.MouseLeave += (_, _) => dot.Opacity = 1;
        return dot;
    }

    private FrameworkElement BuildChrome()
    {
        // Bolinhas estilo Mac (fechar / minimizar) e arrastar pela parte de cima.
        var dots = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 18, 0, 6), Background = Brushes.Transparent };
        dots.Children.Add(Dot(Color.FromRgb(0xFF, 0x5F, 0x57), "Fechar", Close));
        dots.Children.Add(Dot(Color.FromRgb(0xFE, 0xBC, 0x2E), "Minimizar", () => WindowState = WindowState.Minimized));

        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 14, 0, 2) };
        var logo = new TextBlock
        {
            Text = "",
            FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        logo.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        brand.Children.Add(logo);
        brand.Children.Add(new TextBlock { Text = "Configurações", FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });

        var sideStack = new DockPanel { Background = Brushes.Transparent };
        DockPanel.SetDock(dots, System.Windows.Controls.Dock.Top);
        sideStack.Children.Add(dots);
        DockPanel.SetDock(brand, System.Windows.Controls.Dock.Top);
        sideStack.Children.Add(brand);
        sideStack.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _nav });
        sideStack.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not TextBlock) DragMove();
        };

        var sidebar = new Border
        {
            Width = 214,
            Background = SidebarBlack,
            CornerRadius = new CornerRadius(25, 0, 0, 25),
            Child = sideStack,
        };

        // Faixa superior do conteúdo também arrasta a janela.
        var grip = new Border { Height = 22, Background = Brushes.Transparent };
        grip.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };

        var right = new DockPanel();
        DockPanel.SetDock(grip, System.Windows.Controls.Dock.Top);
        right.Children.Add(grip);
        _host.Margin = new Thickness(28, 0, 4, 14);
        right.Children.Add(_host);

        var body = new DockPanel();
        DockPanel.SetDock(sidebar, System.Windows.Controls.Dock.Left);
        body.Children.Add(sidebar);
        body.Children.Add(right);

        return new Border
        {
            CornerRadius = new CornerRadius(26),
            Background = WindowBlack,
            BorderThickness = new Thickness(1),
            BorderBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF))),
            Child = body,
            Margin = new Thickness(14), // espaço para a sombra
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 26, ShadowDepth = 4, Opacity = 0.55 },
        };
    }

    private void ShowPage(string page)
    {
        if (_current == page) return;
        _current = page;
        MarkSelected(page);
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
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        header.Children.Add(new TextBlock { Text = title, FontSize = 26, FontWeight = FontWeights.Bold });
        if (Subtitles.TryGetValue(title, out var sub))
            header.Children.Add(new TextBlock { Text = sub, FontSize = 13, Foreground = Muted, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
        var all = new List<UIElement> { header };
        all.AddRange(content);
        var scroll = new ScrollViewer
        {
            Style = (Style)Application.Current.Resources["MacScroll"],
            Padding = new Thickness(0, 0, 20, 0),
            Content = Ui.Stack(all.ToArray()),
        };
        return new UserControl { Content = scroll };
    }

    /// <summary>Linha de opção estilo Mac: título e explicação à esquerda, interruptor à direita.</summary>
    private static FrameworkElement ToggleRow(string title, string caption, bool value, Action<bool> changed, bool last = false)
    {
        var toggle = new CheckBox { IsChecked = value, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        toggle.Click += (_, _) => changed(toggle.IsChecked == true);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap });
        if (caption.Length > 0)
            text.Children.Add(new TextBlock { Text = caption, FontSize = 12, Foreground = Muted, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap });
        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
        DockPanel.SetDock(toggle, System.Windows.Controls.Dock.Right);
        row.Children.Add(toggle);
        row.Children.Add(text);
        if (last) return row;
        var wrap = new StackPanel { Margin = new Thickness(0) };
        wrap.Children.Add(row);
        wrap.Children.Add(new Border { Height = 1, Background = Frozen(new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF))), Margin = new Thickness(0, 4, 0, 4) });
        return wrap;
    }

    // ---------- Páginas ----------

    private FrameworkElement GeneralPage()
    {
        var startup = ToggleRow("Iniciar junto com o Windows", "O Assistente abre sozinho assim que você entra no Windows.",
            StartupManager.IsEnabled, v => StartupManager.SetEnabled(v), last: true);

        var delay = SliderRow("Tempo para recolher depois que o mouse sai", 150, 2000, _settings.CloseDelayMs,
            v => $"{v / 1000:0.0} s", v => _settings.CloseDelayMs = (int)v);

        var avatarHide = ToggleRow("Avatar escondido", "Aparece quando você leva o mouse até o topo, perto dele.",
            _settings.AvatarAutoHide, v => { _settings.AvatarAutoHide = v; _settings.Save(); });
        var music = ToggleRow("Widget de música", "Ao lado do avatar quando algo estiver tocando.",
            _settings.MusicWidget, v => { _settings.MusicWidget = v; _settings.Save(); Widgets.WidgetManager.Refresh(); });
        var time = ToggleRow("Widget de timer e cronômetro", "Ao lado do avatar, para usar sem abrir o Assistente.",
            _settings.TimeWidget, v => { _settings.TimeWidget = v; _settings.Save(); Widgets.WidgetManager.Refresh(); }, last: true);

        return Page("Geral",
            Ui.Card("Inicialização", "", startup),
            Ui.Card("Comportamento", "", delay),
            Ui.Card("Avatar e mini widgets", "", avatarHide, music, time));
    }

    private FrameworkElement IslandPage()
    {
        var island = ToggleRow("Ilha no topo", "Estilo Dynamic Island: assistente, relógio, CPU, bateria, música, timer e bandeja de arquivos.",
            _settings.MenuBarEnabled, v => { _settings.MenuBarEnabled = v; _settings.Save(); MenuBar.MenuBarWindow.Apply(_settings); }, last: true);
        var corners = ToggleRow("Menus de canto", "Borda esquerda com seus apps, pastas e acessos remotos (só na área de trabalho) e canto de baixo à direita com Wi‑Fi, volume e atalhos do Windows.",
            _settings.CornerMenus, v => { _settings.CornerMenus = v; _settings.Save(); Corners.CornerMenu.Apply(_settings); }, last: true);
        var radial = ToggleRow("Menu em círculo", "Segure um arquivo no Explorer ou na área de trabalho e clique com o botão direito: zipar, extrair, converter, remover fundo…",
            _settings.RadialMenu, v => { _settings.RadialMenu = v; _settings.Save(); Radial.RadialHook.Apply(v); }, last: true);

        var rest = new StackPanel();
        foreach (var (key, title, caption) in new[]
        {
            ("fechada", "Fechada", "Só a pílula preta, até você passar o mouse."),
            ("peek", "Um pouco aberta", "Na área de trabalho fica mostrando saudação ou música e a hora."),
            ("aberta", "Aberta na página inicial", "Na área de trabalho fica aberta por completo, na aba Início."),
        })
        {
            var radio = new RadioButton { GroupName = "islandRest", IsChecked = _settings.IslandRest == key, Margin = new Thickness(0, 4, 0, 4) };
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.Medium });
            text.Children.Add(new TextBlock { Text = caption, FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap });
            radio.Content = text;
            var k = key;
            radio.Checked += (_, _) => { _settings.IslandRest = k; _settings.Save(); MenuBar.MenuBarWindow.Reconfigure(); };
            rest.Children.Add(radio);
        }

        var hover = new StackPanel();
        foreach (var (key, title, caption) in new[]
        {
            ("aberta", "Aberta", "Abre por completo, com as abas Início, Bandeja e Sistema."),
            ("peek", "Um pouco aberta", "Mostra saudação ou música e a hora; clique para abrir tudo."),
        })
        {
            var radio = new RadioButton { GroupName = "islandHover", IsChecked = _settings.IslandHover == key, Margin = new Thickness(0, 4, 0, 4) };
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.Medium });
            text.Children.Add(new TextBlock { Text = caption, FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap });
            radio.Content = text;
            var k = key;
            radio.Checked += (_, _) => { _settings.IslandHover = k; _settings.Save(); MenuBar.MenuBarWindow.Reconfigure(); };
            hover.Children.Add(radio);
        }

        return Page("Ilha e menus",
            Ui.Card("Topo da tela", "", island),
            Ui.Card("Ao passar o mouse", "\uE8B0", hover),
            Ui.Card("Parada na área de trabalho", "\uE8A0", rest,
                Ui.Caption("Com um app aberto ela sempre volta a ficar fechada.")),
            Ui.Card("Cantos da tela", "", corners),
            Ui.Card("Arquivos", "", radial));
    }

    private FrameworkElement TopBarPage()
    {
        var enabled = ToggleRow("Mostrar a barra de menus", "Ao lado da ilha, de ponta a ponta no topo.",
            _settings.TopBarEnabled, v => { _settings.TopBarEnabled = v; _settings.Save(); MenuBar.MenuBarWindow.Reconfigure(); }, last: true);

        var when = new StackPanel();
        foreach (var (key, title, caption) in new[]
        {
            ("desktop", "Só na área de trabalho", "Some quando um app está aberto na frente."),
            ("topo", "Também ao levar o mouse ao topo", "Com um app aberto, aparece quando o mouse encosta no topo da tela."),
        })
        {
            var radio = new RadioButton { GroupName = "topBarShow", IsChecked = _settings.TopBarShow == key, Margin = new Thickness(0, 4, 0, 4) };
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.Medium });
            text.Children.Add(new TextBlock { Text = caption, FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap });
            radio.Content = text;
            var k = key;
            radio.Checked += (_, _) => { _settings.TopBarShow = k; _settings.Save(); MenuBar.MenuBarWindow.Reconfigure(); };
            when.Children.Add(radio);
        }

        var styles = new WrapPanel();
        foreach (var (key, name) in new[] { ("escura", "Escura"), ("preta", "Preta (igual à ilha)"), ("transparente", "Transparente") })
        {
            var radio = new RadioButton { GroupName = "topBarStyle", Content = name, IsChecked = _settings.TopBarStyle == key, Margin = new Thickness(0, 4, 18, 4) };
            var k = key;
            radio.Checked += (_, _) => { _settings.TopBarStyle = k; _settings.Save(); MenuBar.MenuBarWindow.Reconfigure(); };
            styles.Children.Add(radio);
        }

        var left = ItemsEditor(MenuBar.TopBar.LeftCatalog, () => MenuBar.TopBar.LeftItems(_settings), list => _settings.TopBarLeft = list,
            () => _settings.TopBarLeft = null);
        var right = ItemsEditor(MenuBar.TopBar.RightCatalog, () => MenuBar.TopBar.RightItems(_settings), list => _settings.TopBarRight = list,
            () => _settings.TopBarRight = null);

        return Page("Barra de menus",
            Ui.Card("Barra", "\uE700", enabled, styles),
            Ui.Card("Quando aparece", "\uE7B3", when),
            Ui.Card("Lado esquerdo", "\uE8E4", left,
                Ui.Caption("Ligue o que quer ver e use as setas para mudar a ordem.")),
            Ui.Card("Lado direito", "\uE8E2", right,
                Ui.Caption("A data e a hora ficam bem à direita, como no Mac. Role a rodinha em cima do volume para mudar o som.")));
    }

    /// <summary>Lista de itens com interruptor e setas para reordenar.</summary>
    private FrameworkElement ItemsEditor((string Key, string Name)[] catalog, Func<List<string>> current,
        Action<List<string>> save, Action reset)
    {
        var host = new StackPanel();
        void Render()
        {
            host.Children.Clear();
            var enabled = current();
            var order = enabled.Where(k => catalog.Any(c => c.Key == k)).ToList();
            order.AddRange(catalog.Select(c => c.Key).Where(k => !order.Contains(k)));
            for (var i = 0; i < order.Count; i++)
            {
                var key = order[i];
                var name = catalog.First(c => c.Key == key).Name;
                var on = enabled.Contains(key);
                var toggle = new CheckBox { IsChecked = on, Content = name, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0) };
                toggle.Click += (_, _) =>
                {
                    var list = current();
                    if (toggle.IsChecked == true)
                    {
                        // Entra na posição em que aparece na lista.
                        var before = order.Take(order.IndexOf(key)).Count(k => list.Contains(k));
                        list.Insert(Math.Min(before, list.Count), key);
                    }
                    else list.Remove(key);
                    Commit(list);
                };
                var up = ArrowButton("\uE70E", "Subir");
                var down = ArrowButton("\uE70D", "Descer");
                up.IsEnabled = on && enabled.IndexOf(key) > 0;
                down.IsEnabled = on && enabled.IndexOf(key) < enabled.Count - 1;
                up.Click += (_, _) => Move(key, -1);
                down.Click += (_, _) => Move(key, 1);
                var arrows = new StackPanel { Orientation = Orientation.Horizontal };
                arrows.Children.Add(up);
                arrows.Children.Add(down);
                var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                DockPanel.SetDock(arrows, System.Windows.Controls.Dock.Right);
                row.Children.Add(arrows);
                row.Children.Add(toggle);
                row.Opacity = on ? 1 : 0.6;
                host.Children.Add(row);
            }
            var resetButton = Ui.Btn("Voltar ao padrão", () => { reset(); Commit(null); });
            resetButton.HorizontalAlignment = HorizontalAlignment.Left;
            resetButton.Margin = new Thickness(0, 8, 0, 4);
            host.Children.Add(resetButton);
        }

        void Move(string key, int delta)
        {
            var list = current();
            var i = list.IndexOf(key);
            var j = i + delta;
            if (i < 0 || j < 0 || j >= list.Count) return;
            (list[i], list[j]) = (list[j], list[i]);
            Commit(list);
        }

        void Commit(List<string>? list)
        {
            if (list is not null) save(list);
            _settings.Save();
            MenuBar.MenuBarWindow.Reconfigure();
            Render();
        }

        Render();
        return host;
    }

    private static Button ArrowButton(string glyph, string tip) => new()
    {
        Style = (Style)Application.Current.Resources["IconButton"],
        Content = glyph,
        ToolTip = tip,
        Width = 30,
        Height = 28,
        Margin = new Thickness(2, 0, 0, 0),
    };

    private FrameworkElement LockScreenPage()
    {
        var status = Ui.Caption("");
        void Say(string text) => status.Text = text;

        var clutter = ToggleRow("Sem dicas e curiosidades", "Tira os textos de dicas, \"curiosidades\" e a foto que troca sozinha (Destaque do Windows).",
            _settings.LockScreenOriginal is not null, v =>
            {
                try
                {
                    if (v) LockScreenStyle.HideClutter(_settings); else LockScreenStyle.Restore(_settings);
                    Say(v ? "Pronto: a tela de bloqueio ficou sem dicas." : "Dicas e Destaque voltaram como estavam.");
                }
                catch (Exception ex) { Say("Não deu certo: " + ex.Message); }
            }, last: true);

        var swatches = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        foreach (var (key, name, colors) in LockScreenStyle.Styles)
        {
            var preview = new Border
            {
                Width = 128,
                Height = 76,
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(0, 0, 10, 10),
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(2),
                BorderBrush = _settings.LockScreenWallpaper == key ? Ui.Res("AccentBrush") : Brushes.Transparent,
                Background = new LinearGradientBrush(new GradientStopCollection
                {
                    new GradientStop(colors[0], 0), new GradientStop(colors[1], 0.55), new GradientStop(colors[2], 0.8), new GradientStop(colors[3], 1),
                }, 35),
                Child = new TextBlock { Text = name, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 0, 8), VerticalAlignment = VerticalAlignment.Bottom },
                ToolTip = "Usar \"" + name + "\" na tela de bloqueio",
            };
            var k = key;
            preview.MouseLeftButtonUp += async (_, _) =>
            {
                try
                {
                    Say("Aplicando…");
                    var path = LockScreenStyle.RenderWallpaper(k);
                    await LockScreenStyle.SetImageAsync(path);
                    _settings.LockScreenWallpaper = k;
                    _settings.Save();
                    foreach (Border b in swatches.Children) b.BorderBrush = Brushes.Transparent;
                    preview.BorderBrush = Ui.Res("AccentBrush");
                    Say("Pronto! Aperte Windows + L para ver.");
                }
                catch (Exception ex) { Say("Não deu certo: " + ex.Message); }
            };
            swatches.Children.Add(preview);
        }

        var own = Ui.Btn("Usar uma foto minha…", async () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Imagens|*.jpg;*.jpeg;*.png;*.bmp" };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                await LockScreenStyle.SetImageAsync(dialog.FileName);
                Say("Pronto! Aperte Windows + L para ver.");
            }
            catch (Exception ex) { Say("Não deu certo: " + ex.Message); }
        });
        var windowsSettings = Ui.Btn("Abrir configurações do Windows", () =>
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:lockscreen") { UseShellExecute = true }); } catch (Exception) { }
        });

        return Page("Tela de bloqueio",
            Ui.Card("Limpar", "\uE72E", clutter),
            Ui.Card("Papel de parede estilo Mac", "\uE771", swatches, Ui.Inline(own), status),
            Ui.Card("Para ficar ainda mais clean", "\uE946",
                Ui.Caption("Nas configurações do Windows, em \"Status da tela de bloqueio\", escolha \"Nenhum\" para tirar clima e widgets, e desligue \"Mostrar a imagem de fundo na tela de entrada\" se quiser a tela de senha lisa. O relógio e a posição dos textos são do próprio Windows e não dá para mudar."),
                Ui.Inline(windowsSettings)));
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
        DockPanel.SetDock(valueText, System.Windows.Controls.Dock.Right);
        row.Children.Add(valueText);
        row.Children.Add(slider);
        return Ui.Field(label, row);
    }
}
