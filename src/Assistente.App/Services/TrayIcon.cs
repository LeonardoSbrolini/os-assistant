using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Assistente.App.Services;

/// <summary>Ícone na bandeja do sistema com o menu de opções.</summary>
public sealed class TrayIcon : IDisposable
{
    private static TrayIcon? _current;
    private readonly NotifyIcon _icon;

    public TrayIcon(MainWindow window, AppSettings settings)
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add(new ToolStripMenuItem("Abrir painel", null, (_, _) => window.ExpandPinned()));
        menu.Items.Add(new ToolStripSeparator());

        // Borda da tela
        var edgeMenu = new ToolStripMenuItem("Borda da tela");
        foreach (var (label, edge) in new[] { ("Topo", DockEdge.Top), ("Direita", DockEdge.Right), ("Esquerda", DockEdge.Left) })
        {
            var item = new ToolStripMenuItem(label) { Checked = settings.Edge == edge, Tag = edge };
            item.Click += (_, _) =>
            {
                settings.Edge = edge;
                settings.Save();
                foreach (var i in edgeMenu.DropDownItems.OfType<ToolStripMenuItem>()) i.Checked = Equals(i.Tag, edge);
                window.ApplyEdge();
            };
            edgeMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(edgeMenu);

        // Posição ao longo da borda
        var positionMenu = new ToolStripMenuItem("Posição na borda");
        foreach (var (label, value) in new[] { ("Início", 0.2), ("Centro", 0.5), ("Fim", 0.8) })
        {
            var item = new ToolStripMenuItem(label) { Checked = Math.Abs(settings.Position - value) < 0.01, Tag = value };
            item.Click += (_, _) =>
            {
                settings.Position = value;
                settings.Save();
                foreach (var i in positionMenu.DropDownItems.OfType<ToolStripMenuItem>()) i.Checked = Equals(i.Tag, value);
                window.ApplyEdge();
            };
            positionMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(positionMenu);

        var startup = new ToolStripMenuItem("Iniciar com o Windows")
        {
            CheckOnClick = true,
            Checked = StartupManager.IsEnabled,
        };
        startup.CheckedChanged += (_, _) => StartupManager.SetEnabled(startup.Checked);
        menu.Items.Add(startup);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Verificar atualizações", null,
            async (_, _) => await UpdateService.Instance.CheckAsync(manual: true)));
        menu.Items.Add(new ToolStripMenuItem($"Versão {UpdateService.CurrentVersionText}") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Sair", null, (_, _) => System.Windows.Application.Current.Shutdown()));

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Assistente",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => window.ExpandPinned();
        _current = this;
    }

    /// <summary>Mostra um aviso do Windows (balão/notificação) a partir do ícone da bandeja.</summary>
    public static void Notify(string title, string text)
    {
        _current?._icon.ShowBalloonTip(5000, title, text, ToolTipIcon.Info);
    }

    private static Icon LoadIcon()
    {
        try
        {
            if (Environment.ProcessPath is { } path && Icon.ExtractAssociatedIcon(path) is { } icon)
                return icon;
        }
        catch (Exception)
        {
            // Usa o ícone padrão abaixo.
        }
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        _current = null;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
