using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
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
    private readonly WrapPanel _tiles = new();
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
            _right?.Watch(enabled: mainEdge != DockEdge.Right);
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
        Width = PanelWidth + ShadowSpace * 2;
        SizeToContent = SizeToContent.Height;

        var title = new TextBlock
        {
            Text = isLeft ? "Meus atalhos" : "Atalhos do Windows",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(6, 0, 0, 2),
        };
        var caption = new TextBlock
        {
            Text = isLeft ? "Apps e pastas que você escolheu" : "Impressoras, painel de controle e mais",
            FontSize = 11.5,
            Opacity = 0.55,
            Margin = new Thickness(6, 0, 0, 12),
        };

        var content = new StackPanel();
        content.Children.Add(title);
        content.Children.Add(caption);
        content.Children.Add(_tiles);

        _card = new Border
        {
            Width = PanelWidth,
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(12, 14, 12, 12),
            Margin = new Thickness(ShadowSpace),
            BorderThickness = new Thickness(1),
            BorderBrush = new LinearGradientBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), 90),
            Background = new LinearGradientBrush(Color.FromArgb(0xF2, 0x26, 0x27, 0x30), Color.FromArgb(0xF2, 0x15, 0x16, 0x1C), 90),
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.5 },
            RenderTransform = _slide,
            Child = content,
        };
        Content = _card;

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

        if (!_open)
        {
            if (!enabled) return;
            // Mouse encostado na borda (fora dos cantos, onde ficam a barra do topo e o dock).
            var atEdge = (_isLeft ? cx <= 1 : cx >= screenW - 2)
                         && cy > (area.Top + 60) * dpi.DpiScaleY && cy < (area.Bottom - 90) * dpi.DpiScaleY;
            if (!atEdge)
            {
                _edgeSince = DateTime.MaxValue;
                return;
            }
            // Só na área de trabalho, e com uma pausa curtinha para não abrir sem querer.
            if (Native.ForegroundAppRect() is not null || Native.IsForegroundFullscreen() || Mouse.LeftButton == MouseButtonState.Pressed) return;
            if (_edgeSince == DateTime.MaxValue) _edgeSince = DateTime.Now;
            if (DateTime.Now - _edgeSince > TimeSpan.FromMilliseconds(120)) Open();
            return;
        }

        var rect = new Rect(Left * dpi.DpiScaleX, Top * dpi.DpiScaleY, ActualWidth * dpi.DpiScaleX, ActualHeight * dpi.DpiScaleY);
        rect.Inflate(12, 12);
        var inside = rect.Contains(cx, cy) || (_isLeft ? cx <= 1 : cx >= screenW - 2);
        if (inside || _busy)
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
        Left = _isLeft ? SystemParameters.WorkArea.Left - ShadowSpace + 8 : area.Right - Width + ShadowSpace - 8;
        Top = Math.Max(area.Top + 8, area.Top + (area.Height - ActualHeight) / 2);

        var from = _isLeft ? -(PanelWidth + ShadowSpace) : PanelWidth + ShadowSpace;
        var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
        _slide.BeginAnimation(TranslateTransform.XProperty, Anim.FromTo(from, 0, 260, ease));
        _card.BeginAnimation(OpacityProperty, Anim.FromTo(0, 1, 180));
    }

    private void Hide(bool animate)
    {
        if (!_open) return;
        _open = false;
        _leftSince = DateTime.MaxValue;
        if (!animate)
        {
            base.Hide();
            return;
        }
        var to = _isLeft ? -(PanelWidth + ShadowSpace) : PanelWidth + ShadowSpace;
        var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn };
        var slide = Anim.To(to, 200, ease);
        slide.Completed += (_, _) => { if (!_open) base.Hide(); };
        _slide.BeginAnimation(TranslateTransform.XProperty, slide);
        _card.BeginAnimation(OpacityProperty, Anim.To(0, 200));
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
        if (!_isLeft)
        {
            foreach (var s in WindowsShortcuts)
                _tiles.Children.Add(Tile(s.Name, GlyphIcon(s.Glyph), () => Launch(s.Target, s.Args), null));
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
            Width = 46,
            Height = 46,
            CornerRadius = new CornerRadius(13),
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
            Width = 92,
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

    // ---------- Win32 ----------

    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExAppWindow = 0x00040000;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
