using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Assistente.App.Tools;
using Assistente.App.Widgets;

namespace Assistente.App.Docking;

/// <summary>
/// Painel próprio da setinha "^" do dock: lista os apps que estão rodando em segundo plano
/// (sem janela aberta, como Discord, Steam, OneDrive) no lugar do menu padrão do Windows.
/// </summary>
internal static class TrayPanel
{
    private sealed record BackgroundApp(string Path, string Name, int[] Pids);

    private static Popup? _popup;
    private static StackPanel? _list;
    private static TextBlock? _count;
    private static readonly ScaleTransform Scale = new(1, 1);

    public static bool IsOpen => _popup?.IsOpen == true;

    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "svchost", "RuntimeBroker", "conhost", "dllhost", "sihost", "taskhostw", "ctfmon",
        "ApplicationFrameHost", "SearchHost", "SearchApp", "ShellExperienceHost", "StartMenuExperienceHost",
        "TextInputHost", "LockApp", "SystemSettings", "backgroundTaskHost", "smartscreen", "SecurityHealthSystray",
        "msedgewebview2", "WidgetService", "Widgets", "CompPkgSrv", "UserOOBEBroker", "GameBar", "GameBarFTServer",
        "CrossDeviceResume", "CrossDeviceService", "AggregatorHost", "WindowsTerminal", "OpenConsole", "dwm",
    };

    private static readonly string[] IgnoredWords = { "helper", "crashpad", "crash", "update", "elevation", "webview", "renderer", "broker", "notifier" };

    public static void Toggle(FrameworkElement anchor)
    {
        if (IsOpen)
        {
            _popup!.IsOpen = false;
            return;
        }
        Build();
        _popup!.PlacementTarget = anchor;
        _popup.HorizontalOffset = -(300 - anchor.ActualWidth) / 2;
        _popup.VerticalOffset = -8;
        _popup.IsOpen = true;
        var card = (FrameworkElement)_popup.Child;
        card.BeginAnimation(UIElement.OpacityProperty, Anim.FromTo(0, 1, 140));
        Scale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.FromTo(0.92, 1, 220, Anim.Back));
        Scale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.FromTo(0.92, 1, 220, Anim.Back));
        _ = LoadAsync();
    }

    public static void Close()
    {
        if (_popup is not null) _popup.IsOpen = false;
    }

    private static void Build()
    {
        if (_popup is not null) return;

        _count = new TextBlock { FontSize = 11.5, Opacity = 0.55, VerticalAlignment = VerticalAlignment.Center };
        var header = new DockPanel { Margin = new Thickness(4, 0, 4, 10) };
        DockPanel.SetDock(_count, System.Windows.Controls.Dock.Right);
        header.Children.Add(_count);
        header.Children.Add(new TextBlock { Text = "Em segundo plano", FontSize = 14, FontWeight = FontWeights.SemiBold });

        _list = new StackPanel();
        var scroll = new ScrollViewer
        {
            Content = _list,
            MaxHeight = 360,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        var footer = Row("", "Ícones da bandeja do Windows", null);
        footer.Margin = new Thickness(0, 8, 0, 0);
        footer.MouseLeftButtonUp += (_, _) =>
        {
            Close();
            Native.OpenTrayOverflow();
        };

        var line = new Border { Height = 1, Margin = new Thickness(4, 8, 4, 0), Background = new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF)) };

        var content = new StackPanel();
        content.Children.Add(header);
        content.Children.Add(scroll);
        content.Children.Add(line);
        content.Children.Add(footer);

        var card = new Border
        {
            Width = 300,
            CornerRadius = new CornerRadius(26),
            Padding = new Thickness(12, 14, 12, 12),
            Margin = new Thickness(14),
            BorderThickness = new Thickness(1),
            BorderBrush = new LinearGradientBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), 90),
            Background = new LinearGradientBrush(Color.FromArgb(0xF7, 0x12, 0x12, 0x14), Color.FromArgb(0xFA, 0x00, 0x00, 0x00), 90),
            Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 5, Opacity = 0.5 },
            RenderTransformOrigin = new Point(0.5, 1),
            RenderTransform = Scale,
            Child = content,
        };
        TextElement.SetForeground(card, Brushes.White);
        TextElement.SetFontFamily(card, new FontFamily("Segoe UI"));

        _popup = new Popup
        {
            AllowsTransparency = true,
            Placement = PlacementMode.Top,
            StaysOpen = false,
            PopupAnimation = PopupAnimation.None,
            Child = Ui.LiftShadow(card),
        };
    }

    // ---------- Lista ----------

    private static async Task LoadAsync()
    {
        _list!.Children.Clear();
        _count!.Text = "";
        _list.Children.Add(new TextBlock { Text = "Procurando…", Opacity = 0.5, Margin = new Thickness(6, 4, 0, 6) });

        List<BackgroundApp> apps;
        try
        {
            apps = await Task.Run(FindApps);
        }
        catch (Exception)
        {
            apps = new();
        }

        _list.Children.Clear();
        _count.Text = apps.Count == 1 ? "1 app" : $"{apps.Count} apps";
        if (apps.Count == 0)
        {
            _list.Children.Add(new TextBlock { Text = "Nenhum app rodando escondido.", Opacity = 0.5, Margin = new Thickness(6, 4, 0, 6) });
            return;
        }
        foreach (var app in apps) _list.Children.Add(AppRow(app));
        Anim.EnterView(_list);
    }

    private static List<BackgroundApp> FindApps()
    {
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var ownPid = Environment.ProcessId;
        var session = Process.GetCurrentProcess().SessionId;
        var withWindow = new HashSet<string>(Native.GetOpenWindows().Select(w => w.ExePath), StringComparer.OrdinalIgnoreCase);

        var byPath = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == ownPid || p.SessionId != session || Ignored.Contains(p.ProcessName)) continue;
                var lower = p.ProcessName.ToLowerInvariant();
                if (IgnoredWords.Any(lower.Contains)) continue;
                var path = Native.GetProcessPath((uint)p.Id);
                if (string.IsNullOrEmpty(path) || withWindow.Contains(path)) continue;
                if (path.StartsWith(windowsDir, StringComparison.OrdinalIgnoreCase)) continue;
                if (path.Contains("\\EdgeWebView\\", StringComparison.OrdinalIgnoreCase)) continue;
                if (!byPath.TryGetValue(path, out var pids)) byPath[path] = pids = new();
                pids.Add(p.Id);
            }
            catch (Exception)
            {
                // Processo protegido ou que já fechou.
            }
            finally
            {
                p.Dispose();
            }
        }

        return byPath
            .Select(kv => new BackgroundApp(kv.Key, Native.DisplayName(kv.Key), kv.Value.ToArray()))
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static Border AppRow(BackgroundApp app)
    {
        var row = Row(null, app.Name, Native.GetIcon(app.Path, 32));
        row.ToolTip = app.Path;
        var grid = (DockPanel)row.Child;

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Opacity = 0, VerticalAlignment = VerticalAlignment.Center };
        var folder = new PillButton("", () => OpenFolder(app.Path), false, 24) { ToolTip = "Abrir local do arquivo" };
        PillButton? kill = null;
        var armed = false;
        kill = new PillButton("", () =>
        {
            if (!armed)
            {
                armed = true;
                kill!.Glyph = "";
                kill.ToolTip = "Clique de novo para fechar";
                kill.Background = new SolidColorBrush(Color.FromArgb(0xC0, 0xE5, 0x48, 0x4D));
                return;
            }
            Kill(app);
            row.BeginAnimation(UIElement.OpacityProperty, Anim.To(0, 160));
            row.IsHitTestVisible = false;
        }, false, 24) { ToolTip = "Fechar app" };
        actions.Children.Add(folder);
        actions.Children.Add(kill);
        DockPanel.SetDock(actions, System.Windows.Controls.Dock.Right);
        grid.Children.Insert(0, actions);

        row.MouseEnter += (_, _) => actions.BeginAnimation(UIElement.OpacityProperty, Anim.To(1, 120));
        row.MouseLeave += (_, _) =>
        {
            actions.BeginAnimation(UIElement.OpacityProperty, Anim.To(0, 160));
            if (armed)
            {
                armed = false;
                kill.Glyph = "";
                kill.ToolTip = "Fechar app";
                kill.Background = Brushes.Transparent;
            }
        };
        row.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && IsInside(d, actions)) return;
            Close();
            Open(app.Path);
        };
        return row;
    }

    private static bool IsInside(DependencyObject d, DependencyObject parent)
    {
        for (var cur = d; cur is not null; cur = VisualTreeHelper.GetParent(cur))
            if (ReferenceEquals(cur, parent)) return true;
        return false;
    }

    /// <summary>Linha com ícone (imagem ou glifo), texto e destaque ao passar o mouse.</summary>
    private static Border Row(string? glyph, string text, ImageSource? icon = null)
    {
        FrameworkElement iconView = icon is not null
            ? new Image { Source = icon, Width = 20, Height = 20 }
            : new TextBlock { Text = glyph ?? "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var iconBox = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(9),
            Margin = new Thickness(0, 0, 10, 0),
            Background = new SolidColorBrush(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF)),
            Child = iconView,
        };
        iconView.HorizontalAlignment = HorizontalAlignment.Center;
        iconView.VerticalAlignment = VerticalAlignment.Center;

        var label = new TextBlock { Text = text, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var panel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(iconBox, System.Windows.Controls.Dock.Left);
        panel.Children.Add(iconBox);
        panel.Children.Add(label);

        var hover = new SolidColorBrush(Colors.Transparent);
        var row = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(6, 5, 6, 5),
            Margin = new Thickness(0, 1, 0, 1),
            Background = hover,
            Cursor = Cursors.Hand,
            Child = panel,
        };
        row.MouseEnter += (_, _) => hover.BeginAnimation(SolidColorBrush.ColorProperty, Anim.ColorTo(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF), 120));
        row.MouseLeave += (_, _) => hover.BeginAnimation(SolidColorBrush.ColorProperty, Anim.ColorTo(Colors.Transparent, 180));
        return row;
    }

    // ---------- Ações ----------

    /// <summary>Abrir o exe de novo faz a maioria dos apps de bandeja mostrar a janela.</summary>
    private static void Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) ?? "" });
        }
        catch (Exception)
        {
            Native.OpenTrayOverflow();
        }
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch (Exception)
        {
            // Sem explorer: nada a fazer.
        }
    }

    private static void Kill(BackgroundApp app)
    {
        foreach (var pid in app.Pids)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                p.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
                // Já fechou ou é protegido.
            }
        }
    }
}
