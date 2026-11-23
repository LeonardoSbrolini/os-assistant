using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Assistente.App.Docking;
using Assistente.App.Services;
using Assistente.App.Tools;
using Forms = System.Windows.Forms;

namespace Assistente.App.Corners;

/// <summary>
/// Menus de canto da área de trabalho: ao encostar o mouse na borda esquerda abre um painel
/// com os apps e pastas escolhidos; na borda direita, atalhos úteis do Windows.
/// Só funcionam com a área de trabalho à vista (nenhum app na frente).
/// </summary>
public sealed class CornerMenu : Window
{
    private const double PanelWidth = 312;
    private const double ShadowSpace = 18;

    private static CornerMenu? _left, _right;
    private static readonly DispatcherTimer Watcher = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private static AppSettings? _settings;

    private readonly bool _isLeft;
    private readonly Border _card;
    private readonly Grid _root = new();
    private readonly ScaleTransform _unfold = new(1, 1);
    private const double EarSize = 16;
    private readonly WrapPanel _tiles = new();
    private readonly ContentControl _body = new() { Focusable = false };
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 4, 12) };
    private TextBlock _title = null!;
    private TextBlock _caption = null!;
    private string _tab = "atalhos";
    private readonly TranslateTransform _slide = new();
    private bool _open;
    private DateTime _leftSince = DateTime.MaxValue;
    private DateTime _edgeSince = DateTime.MaxValue;

    // ---------- Ligar / desligar ----------

    public static void Apply(AppSettings settings)
    {
        _settings = settings;
        if (settings.CornerMenus)
        {
            _left ??= new CornerMenu(true);
            _right ??= new CornerMenu(false);
            if (!Watcher.IsEnabled)
            {
                Watcher.Tick -= OnWatch;
                Watcher.Tick += OnWatch;
                Watcher.Start();
            }
        }
        else
        {
            Watcher.Stop();
            _left?.Close();
            _right?.Close();
            _left = _right = null;
        }
    }

    private static void OnWatch(object? sender, EventArgs e)
    {
        try
        {
            var mainEdge = _settings?.Edge;
            _left?.Watch(enabled: mainEdge != DockEdge.Left);
            _right?.Watch(enabled: true);
        }
        catch (Exception)
        {
            // Melhor esforço: nunca derruba o app por causa do menu de canto.
        }
    }

    private CornerMenu(bool isLeft)
    {
        _isLeft = isLeft;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        UseLayoutRounding = true;
        Title = isLeft ? "Assistente Canto Esquerdo" : "Assistente Canto Direito";
        FontFamily = new FontFamily("Segoe UI");
        Foreground = Brushes.White;
        SizeToContent = SizeToContent.WidthAndHeight;

        var title = new TextBlock
        {
            Text = isLeft ? "Meus atalhos" : "Central do Windows",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(6, 0, 0, 2),
        };
        var caption = new TextBlock
        {
            Text = isLeft ? "Apps e pastas que você escolheu" : "Wi‑Fi, som, notificações e atalhos",
            FontSize = 11.5,
            Opacity = 0.55,
            Margin = new Thickness(6, 0, 0, 12),
        };

        _title = title;
        _caption = caption;
        var content = new StackPanel();
        content.Children.Add(title);
        content.Children.Add(caption);
        if (isLeft) content.Children.Add(_tabs);
        content.Children.Add(_body);

        // Preto e grudado na borda da tela, como a ilha do topo: só os cantos "de dentro" são
        // arredondados e as orelhas curvas se encontram com a borda.
        _card = new Border
        {
            Width = PanelWidth,
            CornerRadius = isLeft ? new CornerRadius(0, 28, 28, 0) : new CornerRadius(28, 0, 0, 0),
            Padding = isLeft ? new Thickness(14, 18, 16, 16) : new Thickness(16, 18, 14, 16),
            Background = Ears.Black,
            Child = content,
        };
        _root.RenderTransformOrigin = isLeft ? new Point(0, 0.5) : new Point(1, 1);
        _root.RenderTransform = _unfold;
        if (isLeft)
        {
            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var top = Ears.Make(Ears.Kind.LeftTop, EarSize, Ears.Black);
            var bottom = Ears.Make(Ears.Kind.LeftBottom, EarSize, Ears.Black);
            top.HorizontalAlignment = bottom.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetRow(_card, 1);
            Grid.SetRow(bottom, 2);
            _root.Children.Add(top);
            _root.Children.Add(_card);
            _root.Children.Add(bottom);
        }
        else
        {
            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var top = Ears.Make(Ears.Kind.RightTop, EarSize, Ears.Black);
            var left = Ears.Make(Ears.Kind.BottomLeft, EarSize, Ears.Black);
            top.HorizontalAlignment = HorizontalAlignment.Right;
            left.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetColumn(top, 1);
            Grid.SetRow(left, 1);
            Grid.SetRow(_card, 1);
            Grid.SetColumn(_card, 1);
            _root.Children.Add(top);
            _root.Children.Add(left);
            _root.Children.Add(_card);
        }
        Content = _root;
        // Conteúdo mudou de tamanho (aba, formulário): continua grudado na borda certa.
        SizeChanged += (_, _) =>
        {
            if (!_open) return;
            if (_isLeft)
            {
                var area = SystemParameters.WorkArea;
                Top = Math.Max(area.Top, area.Top + (area.Height - ActualHeight) / 2);
            }
            else
            {
                Left = SystemParameters.PrimaryScreenWidth - ActualWidth;
                Top = Math.Max(0, SystemParameters.PrimaryScreenHeight - ActualHeight);
            }
        };

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = GetWindowLong(hwnd, GwlExStyle);
            SetWindowLong(hwnd, GwlExStyle, (style | WsExToolWindow | WsExNoActivate) & ~WsExAppWindow);
        };
    }

    // ---------- Abrir / fechar ----------

    private void Watch(bool enabled)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var (cx, cy) = Native.CursorPosition();
        var screenW = SystemParameters.PrimaryScreenWidth * dpi.DpiScaleX;
        var area = SystemParameters.WorkArea;

        var screenH = SystemParameters.PrimaryScreenHeight * dpi.DpiScaleY;
        bool AtTrigger() => _isLeft
            ? cx >= 0 && cx <= 1 && cy > (area.Top + 60) * dpi.DpiScaleY && cy < (area.Bottom - 90) * dpi.DpiScaleY
            : cx >= screenW - 3 && cx < screenW && cy >= screenH - 3 && cy < screenH; // canto de baixo à direita (só na tela principal)

        if (!_open)
        {
            if (!enabled) return;
            var atEdge = AtTrigger();
            if (!atEdge)
            {
                _edgeSince = DateTime.MaxValue;
                return;
            }
            // A borda esquerda só na área de trabalho; o canto de baixo funciona sempre (menos em tela cheia).
            if ((_isLeft && Native.ForegroundAppRect() is not null) || Native.IsForegroundFullscreen() || Mouse.LeftButton == MouseButtonState.Pressed) return;
            if (_edgeSince == DateTime.MaxValue) _edgeSince = DateTime.Now;
            if (DateTime.Now - _edgeSince > TimeSpan.FromMilliseconds(120)) Open();
            return;
        }

        var rect = new Rect(Left * dpi.DpiScaleX, Top * dpi.DpiScaleY, ActualWidth * dpi.DpiScaleX, ActualHeight * dpi.DpiScaleY);
        rect.Inflate(12, 12);
        var inside = rect.Contains(cx, cy) || AtTrigger();
        if (inside || _busy || (!_isLeft && Docking.TrayPanel.IsOpen))
        {
            _leftSince = DateTime.MaxValue;
            return;
        }
        if (_leftSince == DateTime.MaxValue) _leftSince = DateTime.Now;
        if (DateTime.Now - _leftSince > TimeSpan.FromMilliseconds(350)) Hide(true);
    }

    private bool _busy;

    private void Open()
    {
        _open = true;
        _edgeSince = DateTime.MaxValue;
        _leftSince = DateTime.MaxValue;
        Build();
        Show();
        UpdateLayout();

        var area = SystemParameters.WorkArea;
        var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
        if (_isLeft)
        {
            // Grudado na borda esquerda, no meio da tela.
            Left = 0;
            Top = Math.Max(area.Top, area.Top + (area.Height - ActualHeight) / 2);

        }
        else
        {
            // Grudado no canto de baixo à direita da tela (por cima da barra do Windows).
            Left = SystemParameters.PrimaryScreenWidth - ActualWidth;
            Top = Math.Max(0, SystemParameters.PrimaryScreenHeight - ActualHeight);

            Native.BringToTop(new WindowInteropHelper(this).Handle);
        }
        // Mesma animação do assistente: desdobra a partir da borda, sem quique.
        Anim.Unfold(_root, _unfold, true, _isLeft ? 0.2 : 0.5, _isLeft ? 0.6 : 0.3);
    }

    private void Hide(bool animate)
    {
        if (!_open) return;
        _open = false;
        EndTyping();
        _leftSince = DateTime.MaxValue;
        if (!animate)
        {
            base.Hide();
            return;
        }
        Anim.Unfold(_root, _unfold, false, _isLeft ? 0.2 : 0.5, _isLeft ? 0.6 : 0.3, () => { if (!_open) base.Hide(); });
    }

    // ---------- Conteúdo ----------

    private sealed record Shortcut(string Name, string Glyph, string Target, string? Args = null);

    private static readonly Shortcut[] WindowsShortcuts =
    {
        new("Impressoras", "", "ms-settings:printers"),
        new("Painel de controle", "", "control.exe"),
        new("Gerenciador de tarefas", "", "taskmgr.exe"),
        new("Configurações", "", "ms-settings:"),
        new("Rede e Wi‑Fi", "", "ms-settings:network"),
        new("Bluetooth", "", "ms-settings:bluetooth"),
        new("Som", "", "ms-settings:sound"),
        new("Tela", "", "ms-settings:display"),
        new("Apps instalados", "", "ms-settings:appsfeatures"),
        new("Dispositivos", "", "devmgmt.msc"),
        new("Este computador", "", "explorer.exe", "shell:MyComputerFolder"),
        new("Lixeira", "", "explorer.exe", "shell:RecycleBinFolder"),
        new("Limpeza de disco", "", "cleanmgr.exe"),
        new("Windows Update", "", "ms-settings:windowsupdate"),
        new("Terminal", "", "powershell.exe"),
        new("Info. do sistema", "", "msinfo32.exe"),
    };

    private void Build()
    {
        _tiles.Children.Clear();
        _body.Content = _tiles;
        if (_isLeft) BuildTabs();
        if (_isLeft && _tab == "remoto")
        {
            _title.Text = "Acesso remoto";
            _caption.Text = "Conecte em outros computadores com um clique";
            _body.Content = RemoteView();
            return;
        }
        if (_isLeft)
        {
            _title.Text = "Meus atalhos";
            _caption.Text = "Apps e pastas que você escolheu";
        }
        if (!_isLeft)
        {
            _body.Content = CornerView();
            return;
        }
        foreach (var path in UserItems())
        {
            var name = Directory.Exists(path) ? new DirectoryInfo(path).Name : Native.DisplayName(path);
            if (string.IsNullOrWhiteSpace(name)) name = path;
            FrameworkElement icon = Native.GetIcon(path, 48) is { } img
                ? new Image { Source = img, Width = 26, Height = 26 }
                : GlyphIcon(Directory.Exists(path) ? "" : "");
            var p = path;
            _tiles.Children.Add(Tile(name, icon, () => Launch(p, null), () => Remove(p)));
        }
        _tiles.Children.Add(Tile("Adicionar app", GlyphIcon(""), AddFile, null, dashed: true));
        _tiles.Children.Add(Tile("Adicionar pasta", GlyphIcon(""), AddFolder, null, dashed: true));
    }

    /// <summary>Itens do menu da esquerda (padrão: pastas do usuário).</summary>
    private static List<string> UserItems()
    {
        if (_settings?.CornerLeftItems is { } items) return items.Where(i => File.Exists(i) || Directory.Exists(i)).ToList();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(home, "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        }.Where(Directory.Exists).ToList();
    }

    private void Save(List<string> items)
    {
        if (_settings is null) return;
        _settings.CornerLeftItems = items;
        _settings.Save();
        Build();
    }

    private void Remove(string path) => Save(UserItems().Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)).ToList());

    private void AddFile()
    {
        _busy = true;
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Escolha um app ou arquivo",
                Filter = "Apps e atalhos|*.exe;*.lnk;*.url;*.bat|Todos os arquivos|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            };
            if (dialog.ShowDialog() == true) Save(UserItems().Append(dialog.FileName).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        }
        finally
        {
            _busy = false;
        }
    }

    private void AddFolder()
    {
        _busy = true;
        try
        {
            using var dialog = new Forms.FolderBrowserDialog { Description = "Escolha uma pasta para o menu", UseDescriptionForTitle = true };
            if (dialog.ShowDialog() == Forms.DialogResult.OK) Save(UserItems().Append(dialog.SelectedPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        }
        finally
        {
            _busy = false;
        }
    }

    private void Launch(string target, string? args)
    {
        Hide(true);
        try
        {
            var info = new ProcessStartInfo(target) { UseShellExecute = true };
            if (args is not null) info.Arguments = args;
            Process.Start(info);
        }
        catch (Exception)
        {
            // Atalho indisponível neste Windows.
        }
    }

    private static TextBlock GlyphIcon(string glyph) => new()
    {
        Text = glyph,
        FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
        FontSize = 19,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Bloco com ícone num quadradinho arredondado e o nome embaixo.</summary>
    private Border Tile(string name, FrameworkElement icon, Action click, Action? remove, bool dashed = false)
    {
        var iconBox = new Border
        {
            Width = _isLeft ? 46 : 38,
            Height = _isLeft ? 46 : 38,
            CornerRadius = new CornerRadius(_isLeft ? 13 : 11),
            HorizontalAlignment = HorizontalAlignment.Center,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(dashed ? (byte)0x30 : (byte)0x18, 0xFF, 0xFF, 0xFF)),
            Child = icon,
        };
        if (dashed) iconBox.Background = Brushes.Transparent;
        else iconBox.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
        if (icon is TextBlock t) t.SetResourceReference(TextBlock.ForegroundProperty, dashed ? "MutedBrush" : "AccentBrush");

        var label = new TextBlock
        {
            Text = name,
            FontSize = 11,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 6, 0, 0),
            Opacity = dashed ? 0.6 : 0.9,
        };
        var stack = new StackPanel();
        stack.Children.Add(iconBox);
        stack.Children.Add(label);

        var hover = new SolidColorBrush(Colors.Transparent);
        var scale = new ScaleTransform(1, 1);
        var tile = new Border
        {
            Width = _isLeft ? 92 : 70,
            Padding = new Thickness(4, 8, 4, 8),
            CornerRadius = new CornerRadius(12),
            Background = hover,
            Cursor = Cursors.Hand,
            ToolTip = name,
            Child = stack,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = scale,
        };
        tile.MouseEnter += (_, _) =>
        {
            hover.BeginAnimation(SolidColorBrush.ColorProperty, Anim.ColorTo(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF), 120));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.To(1.05, 140));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.To(1.05, 140));
        };
        tile.MouseLeave += (_, _) =>
        {
            hover.BeginAnimation(SolidColorBrush.ColorProperty, Anim.ColorTo(Colors.Transparent, 180));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.To(1, 160));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.To(1, 160));
        };
        tile.MouseLeftButtonUp += (_, _) => click();
        if (remove is not null)
        {
            var menu = new ContextMenu();
            var item = new MenuItem { Header = "Remover do menu" };
            item.Click += (_, _) => remove();
            menu.Items.Add(item);
            menu.Opened += (_, _) => _busy = true;
            menu.Closed += (_, _) => _busy = false;
            tile.ContextMenu = menu;
        }
        return tile;
    }

    // ---------- Canto de baixo à direita ----------

    private FrameworkElement CornerView()
    {
        var root = new StackPanel();

        // Relógio grande + data (clique abre notificações e calendário do Windows).
        var time = new TextBlock { Text = DateTime.Now.ToString("HH:mm"), FontSize = 34, FontWeight = FontWeights.Light };
        var date = new TextBlock
        {
            Text = DateTime.Now.ToString("dddd, d 'de' MMMM", Ui.PtBr),
            FontSize = 12.5,
            Opacity = 0.65,
        };
        var clock = new StackPanel { Margin = new Thickness(6, 0, 0, 12) };
        clock.Children.Add(time);
        clock.Children.Add(date);
        var clockRow = HoverRow(clock);
        clockRow.ToolTip = "Notificações e calendário";
        clockRow.MouseLeftButtonUp += (_, _) => { Hide(true); Native.WinCombo(0x4E); };
        root.Children.Add(clockRow);

        // Controles rápidos (como a central de controle do Mac).
        var quick = new UniformGrid { Columns = 2, Margin = new Thickness(0, 4, 0, 8) };
        quick.Children.Add(QuickTile("", "Wi‑Fi e rede", NetworkText(), () => { Hide(true); Native.WinCombo(0x41); }));
        quick.Children.Add(QuickTile("", "Bluetooth", "Dispositivos", () => Launch("ms-settings:bluetooth", null)));
        quick.Children.Add(QuickTile("", "Notificações", "Central do Windows", () => { Hide(true); Native.WinCombo(0x4E); }));
        var battery = BatteryText();
        if (battery is not null) quick.Children.Add(QuickTile("", "Bateria", battery, () => Launch("ms-settings:batterysaver", null)));
        else quick.Children.Add(QuickTile("", "Energia", "Opções de energia", () => Launch("ms-settings:powersleep", null)));
        root.Children.Add(quick);

        // Volume: mudo, menos e mais.
        var volume = new DockPanel { Margin = new Thickness(4, 0, 4, 10) };
        var volLabel = new TextBlock { Text = "Volume", FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 };
        var volButtons = new StackPanel { Orientation = Orientation.Horizontal };
        volButtons.Children.Add(new Widgets.PillButton("", MediaService.VolumeMute, false, 30) { ToolTip = "Mudo" });
        volButtons.Children.Add(new Widgets.PillButton("", MediaService.VolumeDown, false, 30) { ToolTip = "Diminuir" });
        volButtons.Children.Add(new Widgets.PillButton("", MediaService.VolumeUp, true, 30) { ToolTip = "Aumentar" });
        DockPanel.SetDock(volButtons, System.Windows.Controls.Dock.Right);
        volume.Children.Add(volButtons);
        var volIcon = GlyphIcon("");
        volIcon.FontSize = 15;
        volIcon.Margin = new Thickness(2, 0, 8, 0);
        DockPanel.SetDock(volIcon, System.Windows.Controls.Dock.Left);
        volume.Children.Add(volIcon);
        volume.Children.Add(volLabel);
        var volumeCard = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(6, 6, 6, 6),
            Margin = new Thickness(3, 0, 3, 10),
            Background = new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)),
            Child = volume,
        };
        volume.Margin = new Thickness(0);
        root.Children.Add(volumeCard);

        // Apps rodando escondidos (Discord, Steam…).
        var background = AddRow("", "Apps em segundo plano", () => { });
        background.MouseLeftButtonUp += (_, _) => Docking.TrayPanel.Toggle(background);
        root.Children.Add(background);

        root.Children.Add(new TextBlock { Text = "Atalhos do Windows", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Opacity = 0.8, Margin = new Thickness(6, 10, 0, 6) });
        var tiles = new WrapPanel();
        foreach (var s in WindowsShortcuts)
            tiles.Children.Add(Tile(s.Name, GlyphIcon(s.Glyph), () => Launch(s.Target, s.Args), null));
        root.Children.Add(tiles);

        _busy = false;
        return root;
    }

    private Border QuickTile(string glyph, string title, string subtitle, Action click)
    {
        var icon = GlyphIcon(glyph);
        icon.FontSize = 14;
        icon.Foreground = Brushes.White;
        var circle = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Margin = new Thickness(0, 0, 8, 0), Child = icon };
        circle.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        texts.Children.Add(new TextBlock { Text = subtitle, FontSize = 10.5, Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis });
        var panel = new DockPanel();
        DockPanel.SetDock(circle, System.Windows.Controls.Dock.Left);
        panel.Children.Add(circle);
        panel.Children.Add(texts);

        var hover = new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF));
        var tile = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8),
            Margin = new Thickness(3),
            Background = hover,
            Cursor = Cursors.Hand,
            Child = panel,
        };
        tile.MouseEnter += (_, _) => hover.BeginAnimation(SolidColorBrush.ColorProperty, Anim.ColorTo(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF), 120));
        tile.MouseLeave += (_, _) => hover.BeginAnimation(SolidColorBrush.ColorProperty, Anim.ColorTo(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF), 180));
        tile.MouseLeftButtonUp += (_, _) => click();
        return tile;
    }

    private static string NetworkText()
    {
        try
        {
            return System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable() ? "Conectado" : "Sem conexão";
        }
        catch (Exception)
        {
            return "Rede";
        }
    }

    private static string? BatteryText()
    {
        var power = Forms.SystemInformation.PowerStatus;
        if (power.BatteryChargeStatus == Forms.BatteryChargeStatus.NoSystemBattery || power.BatteryLifePercent > 1) return null;
        var text = $"{power.BatteryLifePercent * 100:0}%";
        return power.PowerLineStatus == Forms.PowerLineStatus.Online ? text + " · carregando" : text;
    }

    // ---------- Abas ----------

    private void BuildTabs()
    {
        _tabs.Children.Clear();
        var track = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(3),
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0x00, 0x00, 0x00)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (key, glyph, text) in new[] { ("atalhos", "", "Atalhos"), ("remoto", "", "Acesso remoto") })
        {
            var selected = _tab == key;
            var label = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            label.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 12, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            label.Children.Add(new TextBlock { Text = text, FontSize = 12.5, FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center });
            var pill = new Border
            {
                Width = 134,
                Padding = new Thickness(0, 6, 0, 6),
                CornerRadius = new CornerRadius(8),
                Cursor = Cursors.Hand,
                Opacity = selected ? 1 : 0.65,
                Child = label,
            };
            if (selected) pill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            else pill.Background = Brushes.Transparent;
            var k = key;
            pill.MouseLeftButtonUp += (_, _) =>
            {
                if (_tab == k) return;
                _tab = k;
                EndTyping();
                Build();
                Anim.EnterView(_body);
            };
            row.Children.Add(pill);
        }
        track.Child = row;
        _tabs.Children.Add(track);
    }

    // ---------- Acesso remoto ----------

    private TextBlock? _remoteError;

    private static System.Collections.Generic.List<RemoteConnection> Connections() => _settings?.RemoteConnections ?? new();

    private FrameworkElement RemoteView()
    {
        var list = new StackPanel();
        foreach (var c in Connections()) list.Children.Add(RemoteRow(c));
        if (Connections().Count == 0)
        {
            list.Children.Add(new TextBlock
            {
                Text = "Nenhum acesso cadastrado ainda.",
                Opacity = 0.55,
                FontSize = 12.5,
                Margin = new Thickness(6, 0, 0, 8),
            });
        }
        list.Children.Add(AddRow("", "Novo acesso remoto", () => ShowForm(null)));
        _remoteError = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 8, 6, 0), Visibility = Visibility.Collapsed };
        _remoteError.SetResourceReference(TextBlock.ForegroundProperty, "ErrorBrush");
        list.Children.Add(_remoteError);
        return list;
    }

    private Border RemoteRow(RemoteConnection c)
    {
        var glyph = c.Kind switch { "rdp" => "", "vnc" => "", _ => "" };
        var iconBox = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 0, 10, 0),
            Child = GlyphIcon(glyph),
        };
        iconBox.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
        ((TextBlock)iconBox.Child).FontSize = 16;
        ((TextBlock)iconBox.Child).SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(c.Name) ? c.Address : c.Name, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        texts.Children.Add(new TextBlock { Text = c.KindLabel + (string.IsNullOrWhiteSpace(c.Host) ? "" : " · " + c.Address), FontSize = 11, Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis });

        var edit = new Widgets.PillButton("", () => ShowForm(c), false, 26) { ToolTip = "Editar", Opacity = 0, VerticalAlignment = VerticalAlignment.Center };
        var panel = new DockPanel();
        DockPanel.SetDock(iconBox, System.Windows.Controls.Dock.Left);
        DockPanel.SetDock(edit, System.Windows.Controls.Dock.Right);
        panel.Children.Add(iconBox);
        panel.Children.Add(edit);
        panel.Children.Add(texts);

        var row = HoverRow(panel);
        row.ToolTip = "Clique para conectar";
        row.MouseEnter += (_, _) => edit.BeginAnimation(OpacityProperty, Anim.To(1, 120));
        row.MouseLeave += (_, _) => edit.BeginAnimation(OpacityProperty, Anim.To(0, 160));
        row.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && IsInside(d, edit)) return;
            var error = RemoteAccess.Connect(c);
            if (error is null)
            {
                Hide(true);
                return;
            }
            if (_remoteError is not null)
            {
                _remoteError.Text = error;
                _remoteError.Visibility = Visibility.Visible;
            }
        };

        var menu = new ContextMenu();
        var editItem = new MenuItem { Header = "Editar" };
        editItem.Click += (_, _) => ShowForm(c);
        var removeItem = new MenuItem { Header = "Remover" };
        removeItem.Click += (_, _) =>
        {
            Connections().RemoveAll(x => x.Id == c.Id);
            _settings?.Save();
            Build();
        };
        menu.Items.Add(editItem);
        menu.Items.Add(removeItem);
        menu.Opened += (_, _) => _busy = true;
        menu.Closed += (_, _) => _busy = false;
        row.ContextMenu = menu;
        return row;
    }

    private static bool IsInside(DependencyObject d, DependencyObject parent)
    {
        for (var cur = d; cur is not null; cur = VisualTreeHelper.GetParent(cur))
            if (ReferenceEquals(cur, parent)) return true;
        return false;
    }

    private Border AddRow(string glyph, string text, Action click)
    {
        var icon = GlyphIcon(glyph);
        icon.FontSize = 14;
        icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        var box = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 0, 10, 0),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            Child = icon,
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(box);
        panel.Children.Add(new TextBlock { Text = text, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 });
        var row = HoverRow(panel);
        row.MouseLeftButtonUp += (_, _) => click();
        return row;
    }

    private static Border HoverRow(UIElement child)
    {
        var hover = new SolidColorBrush(Colors.Transparent);
        var row = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(6),
            Margin = new Thickness(0, 1, 0, 1),
            Background = hover,
            Cursor = Cursors.Hand,
            Child = child,
        };
        row.MouseEnter += (_, _) => hover.BeginAnimation(SolidColorBrush.ColorProperty, Anim.ColorTo(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF), 120));
        row.MouseLeave += (_, _) => hover.BeginAnimation(SolidColorBrush.ColorProperty, Anim.ColorTo(Colors.Transparent, 180));
        return row;
    }

    // ---------- Cadastro ----------

    private bool _typing;

    /// <summary>Para digitar, a janela precisa poder receber o teclado.</summary>
    private void BeginTyping()
    {
        if (_typing) return;
        _typing = true;
        _busy = true;
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) & ~WsExNoActivate);
        Activate();
    }

    private void EndTyping()
    {
        if (!_typing) return;
        _typing = false;
        _busy = false;
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | WsExNoActivate);
    }

    private void ShowForm(RemoteConnection? existing)
    {
        var c = existing ?? new RemoteConnection();
        var kind = c.Kind;

        TextBox Box(string text) => new() { Text = text, FontSize = 13, Margin = new Thickness(0, 3, 0, 9) };
        TextBlock Label(string text) => new() { Text = text, FontSize = 11.5, Opacity = 0.65, Margin = new Thickness(2, 0, 0, 0) };

        var name = Box(c.Name);
        var host = Box(c.Host);
        var port = Box(c.Port?.ToString() ?? "");
        port.Width = 70;
        var user = Box(c.User);
        var password = new PasswordBox { Password = c.Password, FontSize = 13, Margin = new Thickness(0, 3, 0, 9) };
        var program = Box(c.Program);
        var args = Box(c.Args);

        var programRow = new DockPanel();
        var browse = new Widgets.PillButton("", () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Programa de acesso remoto", Filter = "Programas|*.exe|Todos os arquivos|*.*" };
            if (dialog.ShowDialog() == true) program.Text = dialog.FileName;
            Activate();
        }, false, 30) { ToolTip = "Procurar…", Margin = new Thickness(6, 0, 0, 6) };
        DockPanel.SetDock(browse, System.Windows.Controls.Dock.Right);
        programRow.Children.Add(browse);
        programRow.Children.Add(program);

        var programLabel = Label("Programa");
        var argsLabel = Label("Argumentos ({host}, {port}, {user}, {password})");
        var vncHint = new TextBlock { FontSize = 11, Opacity = 0.55, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, -4, 0, 8) };

        // Tipo: três "pílulas"
        var kinds = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 10) };
        void Refresh()
        {
            kinds.Children.Clear();
            foreach (var (key, text) in new[] { ("rdp", "Remota do Windows"), ("vnc", "TightVNC"), ("outro", "Outro app") })
            {
                var pill = new Border
                {
                    Padding = new Thickness(10, 5, 10, 5),
                    Margin = new Thickness(0, 0, 6, 0),
                    CornerRadius = new CornerRadius(8),
                    Cursor = Cursors.Hand,
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                    Child = new TextBlock { Text = text, FontSize = 12 },
                };
                if (kind == key) pill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                else pill.Background = new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF));
                var k = key;
                pill.MouseLeftButtonUp += (_, _) => { kind = k; Refresh(); };
                kinds.Children.Add(pill);
            }
            var showProgram = kind != "rdp";
            programLabel.Visibility = programRow.Visibility = showProgram ? Visibility.Visible : Visibility.Collapsed;
            argsLabel.Visibility = args.Visibility = kind == "outro" ? Visibility.Visible : Visibility.Collapsed;
            vncHint.Visibility = kind == "vnc" ? Visibility.Visible : Visibility.Collapsed;
            vncHint.Text = RemoteAccess.FindTightVnc() is { } found
                ? "TightVNC encontrado: " + found
                : "TightVNC não encontrado: indique o tvnviewer.exe acima.";
            programLabel.Text = kind == "vnc" ? "Programa (opcional)" : "Programa";
        }
        Refresh();

        var error = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(2, 0, 0, 8) };
        error.SetResourceReference(TextBlock.ForegroundProperty, "ErrorBrush");

        var hostRow = new DockPanel();
        var portStack = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
        portStack.Children.Add(Label("Porta"));
        portStack.Children.Add(port);
        DockPanel.SetDock(portStack, System.Windows.Controls.Dock.Right);
        var hostStack = new StackPanel();
        hostStack.Children.Add(Label("Endereço (IP ou nome)"));
        hostStack.Children.Add(host);
        hostRow.Children.Add(portStack);
        hostRow.Children.Add(hostStack);

        var save = new Button { Content = "Salvar", Style = (Style)Application.Current.Resources["PrimaryButton"], Padding = new Thickness(18, 6, 18, 6) };
        var cancel = new Button { Content = "Cancelar", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(14, 6, 14, 6) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);

        var form = new StackPanel { Margin = new Thickness(4, 0, 4, 0) };
        form.Children.Add(Label("Nome"));
        form.Children.Add(name);
        form.Children.Add(Label("Tipo"));
        form.Children.Add(kinds);
        form.Children.Add(hostRow);
        form.Children.Add(Label("Usuário"));
        form.Children.Add(user);
        form.Children.Add(Label("Senha (guardada protegida neste Windows)"));
        form.Children.Add(password);
        form.Children.Add(programLabel);
        form.Children.Add(programRow);
        form.Children.Add(vncHint);
        form.Children.Add(argsLabel);
        form.Children.Add(args);
        form.Children.Add(error);
        form.Children.Add(buttons);

        cancel.Click += (_, _) => { EndTyping(); Build(); };
        save.Click += (_, _) =>
        {
            int? portValue = null;
            if (!string.IsNullOrWhiteSpace(port.Text))
            {
                if (!int.TryParse(port.Text.Trim(), out var p) || p is < 1 or > 65535)
                {
                    error.Text = "A porta precisa ser um número entre 1 e 65535.";
                    error.Visibility = Visibility.Visible;
                    return;
                }
                portValue = p;
            }
            if (kind != "outro" && string.IsNullOrWhiteSpace(host.Text))
            {
                error.Text = "Informe o endereço do computador.";
                error.Visibility = Visibility.Visible;
                return;
            }
            if (kind == "outro" && string.IsNullOrWhiteSpace(program.Text))
            {
                error.Text = "Escolha o programa de acesso remoto.";
                error.Visibility = Visibility.Visible;
                return;
            }
            c.Name = name.Text.Trim();
            c.Kind = kind;
            c.Host = host.Text.Trim();
            c.Port = portValue;
            c.User = user.Text.Trim();
            c.Password = password.Password;
            c.Program = program.Text.Trim();
            c.Args = args.Text.Trim();
            if (_settings is not null)
            {
                _settings.RemoteConnections ??= new();
                if (existing is null) _settings.RemoteConnections.Add(c);
                _settings.Save();
            }
            EndTyping();
            Build();
        };

        _title.Text = existing is null ? "Novo acesso remoto" : "Editar acesso";
        _caption.Text = "Área de Trabalho Remota, TightVNC ou outro app";
        _body.Content = form;
        Anim.EnterView(form);
        BeginTyping();
        Dispatcher.BeginInvoke(new Action(() => name.Focus()), DispatcherPriority.Input);
        // A janela cresce com o formulário: mantém dentro da tela.
        Dispatcher.BeginInvoke(new Action(KeepOnScreen), DispatcherPriority.Loaded);
    }

    private void KeepOnScreen()
    {
        var area = SystemParameters.WorkArea;
        if (Top + ActualHeight > area.Bottom) Top = Math.Max(area.Top + 8, area.Bottom - ActualHeight);
    }

    // ---------- Win32 ----------

    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExAppWindow = 0x00040000;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
